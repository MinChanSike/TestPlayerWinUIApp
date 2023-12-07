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
            Config.Demuxer.AllowFindStreamInfo = false; //To reduce video initial play time.
            Config.Demuxer.ReadLiveTimeout = 50000000L; //5 Seconds
            Config.Demuxer.AllowTimeouts = true;

            //===== Low Latency Tuning ======
            Config.Player.MaxLatency = 150 * 10000;

            Config.Audio.Enabled = false;
            Config.Subtitles.Enabled = false;

            Config.Demuxer.FormatFlags |= 0x0040;

            Config.Demuxer.FormatOpt["flags"] = "low_delay";
            Config.Demuxer.FormatOpt["rtsp_transport"] = "tcp";
            ////=====================================================================

            Player = new Player(Config);
            FullScreenContainer.CustomizeFullScreenWindow += FullScreenContainer_CustomizeFullScreenWindow;

            Title = "TestPlayerWinUIApp";
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
            Player.BufferingCompleted += (s, e) => {
                Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.BufferingCompleted");

            };
            Player.OpenCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.OpenCompleted");
            Player.OpenSessionCompleted += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.OpenSessionCompleted");
            Player.VideoDemuxer.TimedOut += (s, e) => Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.VideoDemuxer.TimedOut");
            Player.PlaybackStopped += (s, e) => {
                Debug.WriteLine($"{DateTime.Now.Dump()}\t Player.PlaybackStopped, Error => {e.Error}");
            };
        }

        private async void OnBtnPlayStopClicked(object sender, RoutedEventArgs e) {
            Button button = (Button)sender;

            if (Player.IsPlaying) {
                Debug.WriteLine($"{DateTime.Now.Dump()}\t Stop player.");                 
                Player.Stop();                 
                return;
            }

            var _streamUrl = txtUrl.Text.Trim();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) {
                try {
                    cts.Token.Register(() => {
                        DispatcherQueue.TryEnqueue(() => {
                            System.Windows.Forms.MessageBox.Show($"Could not play input stream url.", "TIMEOUT!");
                        });
                    });

                    await Task.Run(() => {
                        try {
                            Debug.WriteLine($"{DateTime.Now.Dump()}\t Open player.");
                            var result = Player.Open(_streamUrl);
                            Debug.WriteLine($"{DateTime.Now.Dump()}\t Open player result => success:{result.Success}, error:{result.Error}");

                            if (!result.Success && result.Error != "Cancelled") {
                                DispatcherQueue.TryEnqueue(() => {
                                    System.Windows.Forms.MessageBox.Show($"Could not play input stream url.\n{result.Error}", "ERROR!");
                                });
                            }
                        } catch (Exception ex) {
                            DispatcherQueue.TryEnqueue(() => {
                                System.Windows.Forms.MessageBox.Show(ex.Message, "ERROR!");
                            });
                        }
                    }, cts.Token);
                } catch (OperationCanceledException) {
                } catch (Exception ex) {
                    Debug.WriteLine($"{DateTime.Now.Dump()}\t ERROR => {ex.Message}");
                }
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
