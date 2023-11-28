using FlyleafLib;
using FlyleafLib.Controls.WinUI;
using FlyleafLib.MediaPlayer;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;

namespace TestPlayerWinUIApp {
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow : Window {
        SymbolIcon iconNormal = new SymbolIcon(Symbol.BackToWindow);
        SymbolIcon iconFullScreen = new SymbolIcon(Symbol.FullScreen);
         
        LogHandler Log = new LogHandler("[Main] ");
        AppWindow MainAppWindow;

        public Player Player { get; set; }
        public Config Config { get; set; }

        public MainWindow() {
            Title = "TestPlayerWinUIApp";

            //===== Initializes Engine ====== (Specifies FFmpeg libraries path which is required)
            Engine.Start(new EngineConfig() {                 
                FFmpegPath = ":FFmpeg",
                FFmpegDevices = false,
                UIRefresh = false, // For Activity Mode usage
#if DEBUG
                FFmpegLogLevel = FFmpegLogLevel.Warning,
                LogLevel = LogLevel.Debug,
                LogOutput = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestPlayerWinUIApp.log"),
#endif
            });
            Log.Info($"Configure Flyleaf Engine Done.");
            //=====================================================================

            Config = new Config();
            Config.Video.BackgroundColor = System.Windows.Media.Colors.DarkGray;
            Config.Video.AspectRatio = AspectRatio.Keep;             
            Config.Player.Stats = true;
           
            //===== Low Latency Tuning ======
            Config.Player.MaxLatency = 150 * 10000;

            Config.Audio.Enabled = false;
            Config.Subtitles.Enabled = false;

            Config.Demuxer.FormatFlags |= 0x0040;

            Config.Demuxer.FormatOpt["flags"] = "low_delay";
            Config.Demuxer.FormatOpt["rtsp_transport"] = "tcp";
            //=====================================================================


            Player = new Player(Config);
            FullScreenContainer.CustomizeFullScreenWindow += FullScreenContainer_CustomizeFullScreenWindow;

            InitializeComponent();
            rootGrid.DataContext = this;

            btnFullScreen.Content = FSC.IsFullScreen ? iconNormal : iconFullScreen;
            Player.PropertyChanged += Player_PropertyChanged;

            InitDragMove();

            IntPtr hWnd = WindowNative.GetWindowHandle(this);
            WindowId wndId = Win32Interop.GetWindowIdFromWindow(hWnd);
            MainAppWindow = AppWindow.GetFromWindowId(wndId);

            FSC.FullScreenEnter += (o, e) => {
                btnFullScreen.Content = iconNormal;
                MainAppWindow.IsShownInSwitchers = false;
                flyleafHost.KFC.Focus(FocusState.Keyboard);
            };

            FSC.FullScreenExit += (o, e) => {
                btnFullScreen.Content = iconFullScreen;
                MainAppWindow.IsShownInSwitchers = true;
                Task.Run(() => { Thread.Sleep(10); Utils.UIInvoke(() => flyleafHost.KFC.Focus(FocusState.Keyboard)); });
            };

            Player.BufferingStarted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.BufferingStarted");
            Player.BufferingCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.BufferingCompleted");
            Player.PlaybackStopped += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.PlaybackStopped");
            Player.OpenCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.OpenCompleted");
            Player.OpenSessionCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.OpenSessionCompleted");
            Player.OpenVideoStreamCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.OpenVideoStreamCompleted");
            Player.OpenExternalVideoStreamCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.OpenExternalVideoStreamCompleted");

            Player.VideoDemuxer.TimedOut += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.VideoDemuxer.TimedOut");
        }

        private void OnBtnPlayStopClicked(object sender, RoutedEventArgs e) {
            Button button = (Button)sender;
            try {
                button.IsEnabled = false;
                if (Player.IsPlaying) {
                    Debug.WriteLine($"{DateTime.Now.Dump()}\t Stop player.");
                    Player.Stop();
                    return;
                }

                Debug.WriteLine($"{DateTime.Now.Dump()}\t Open player.");
                var result = Player.Open(txtUrl.Text.Trim());
                Debug.WriteLine($"{DateTime.Now.Dump()}\t Open player result => {JsonSerializer.Serialize(result)}");
                if (!result.Success) {
                    System.Windows.Forms.MessageBox.Show("Could not play input stream url.", "ERROR!");
                }
            } catch (Exception ex) {
                System.Windows.Forms.MessageBox.Show(ex.Message, "ERROR!");
            } finally {
                button.IsEnabled = true;
            }
        }

        private void FullScreenContainer_CustomizeFullScreenWindow(object sender, EventArgs e) {
            FullScreenContainer.FSWApp.Title = Title + " (FS)";
            FullScreenContainer.FSW.Closed += (o, e) => Close();
            Log.Info($"CustomizeFullScreenWindow");
        }

        private void Player_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(Player.Status): {
                    playerControlGrid.AllowFocusOnInteraction = Player.Status != Status.Playing;                     
                    btnPlayStop.Content = Player.Status == Status.Playing ? "Stop" : "Play";
                    break;
                }
            }
        }

        #region DragMove (Should be added within FlyleafHost?)
        [DllImport("User32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GetCursorPos(out Windows.Graphics.PointInt32 lpPoint);

        int nX = 0, nY = 0, nXWindow = 0, nYWindow = 0;
        bool bMoving = false;
        AppWindow _apw;

        private void InitDragMove() {
            _apw = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(WindowNative.GetWindowHandle(this)));
            UIElement root = Content;
            root.PointerMoved += Root_PointerMoved;
            root.PointerPressed += Root_PointerPressed;
            root.PointerReleased += Root_PointerReleased;
        }

        Pointer cur;
        private void Root_PointerReleased(object sender, PointerRoutedEventArgs e) {

            ((UIElement)sender).ReleasePointerCaptures();
            bMoving = false;
        }

        private void Root_PointerPressed(object sender, PointerRoutedEventArgs e) {
            cur = null;
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsLeftButtonPressed) {
                cur = e.Pointer;
                ((UIElement)sender).CapturePointer(e.Pointer);
                nXWindow = _apw.Position.X;
                nYWindow = _apw.Position.Y;
                Windows.Graphics.PointInt32 pt;
                GetCursorPos(out pt);
                nX = pt.X;
                nY = pt.Y;
                bMoving = true;
            }
        }
        private void Root_PointerMoved(object sender, PointerRoutedEventArgs e) {
            var properties = e.GetCurrentPoint((UIElement)sender).Properties;
            if (properties.IsLeftButtonPressed) {
                Windows.Graphics.PointInt32 pt;
                GetCursorPos(out pt);

                if (bMoving)
                    _apw.Move(new Windows.Graphics.PointInt32(nXWindow + (pt.X - nX), nYWindow + (pt.Y - nY)));

                e.Handled = true;
            }
        }
        #endregion

    }

    public static class Extensions {
        public static string Dump(this DateTime dt) {
            return dt.ToString("hh:mm:ss.ffff");
        }
    }
}
