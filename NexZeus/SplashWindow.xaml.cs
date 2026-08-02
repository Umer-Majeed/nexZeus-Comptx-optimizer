using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;

namespace NexZeus
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            Loaded += async (s, e) => await RunLoadingSequence();
        }

        private async Task RunLoadingSequence()
        {
            await UpdateProgress(20, "Loading configuration settings...");
            _ = AppSettings.PingThresholdMs; // Forces settings initialization

            await UpdateProgress(45, "Verifying hardware monitors...");
            await Task.Delay(250);

            await UpdateProgress(70, "Scanning Roblox environment...");
            await Task.Delay(250);

            await UpdateProgress(95, "Launching dashboard...");
            await Task.Delay(250);

            await UpdateProgress(100, "Ready");
            await Task.Delay(200);

            // Smooth fade out before closing splash
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
            fadeOut.Completed += (s, ev) =>
            {
                var main = new MainWindow();
                main.Show();
                Close();
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }

        private async Task UpdateProgress(int value, string status)
        {
            LoadProgress.Value = value;
            LoadingStatusText.Text = status;
            await Task.Delay(1);
        }
    }
}