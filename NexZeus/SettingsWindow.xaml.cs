using System.Windows;
using System.Windows.Input;

namespace NexZeus
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            PingThresholdBox.Text = AppSettings.PingThresholdMs.ToString();
            CpuThresholdBox.Text = AppSettings.CpuThresholdPercent.ToString();
            PlaceIdBox.Text = AppSettings.BloxStrikePlaceId;
            StartWithWindowsBox.IsChecked = AppSettings.StartWithWindows;
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PingThresholdBox.Text, out int ping))
                AppSettings.PingThresholdMs = ping;

            if (int.TryParse(CpuThresholdBox.Text, out int cpu))
                AppSettings.CpuThresholdPercent = cpu;

            AppSettings.BloxStrikePlaceId = PlaceIdBox.Text.Trim();
            AppSettings.StartWithWindows = StartWithWindowsBox.IsChecked ?? false;

            ThemedMessageBox.Show(this, "Settings saved.", "NexZeus", ThemedMessageBoxIcon.Question);
            Close();
        }
    }
}