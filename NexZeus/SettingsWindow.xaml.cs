using System.Windows;

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
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PingThresholdBox.Text, out int ping))
                AppSettings.PingThresholdMs = ping;

            if (int.TryParse(CpuThresholdBox.Text, out int cpu))
                AppSettings.CpuThresholdPercent = cpu;

            AppSettings.BloxStrikePlaceId = PlaceIdBox.Text.Trim();

            System.Windows.MessageBox.Show("Settings saved.", "NexZeus", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}