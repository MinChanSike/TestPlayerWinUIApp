using Microsoft.UI.Xaml;
using System;
using Windows.Media.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace TestPlayerWinUIApp {
    /// <summary>
    /// An empty window that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainWindow2 : Window {
        public MainWindow2() {
            this.InitializeComponent();

            root.Loaded += Root_Loaded;

        }

        private void Root_Loaded(object sender, RoutedEventArgs e) {
            try {                 
                //Can not play rtsps
                MyMediaPlayerElm.Source = MediaSource.CreateFromUri(new Uri("rtsp://127.0.0.1:8554/live/sample1"));
                //MyMediaPlayerElm.Source = MediaSource.CreateFromUri(new Uri("rtsps://127.0.0.1:554/live/sample1"));
                MyMediaPlayerElm.MediaPlayer.Play();
            } catch (Exception ex) {
                System.Windows.Forms.MessageBox.Show(ex.Message, "ERROR!");
            }
        }
    }
}
