using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NexZeus
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            PingThresholdBox.Text = AppSettings.PingThresholdMs.ToString();
            CpuThresholdBox.Text = AppSettings.CpuThresholdPercent.ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(PingThresholdBox.Text, out int ping))
                AppSettings.PingThresholdMs = ping;

            if (int.TryParse(CpuThresholdBox.Text, out int cpu))
                AppSettings.CpuThresholdPercent = cpu;

            MessageBox.Show("Settings saved.", "NexZeus", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}