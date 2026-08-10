using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace NexZeus
{
    public partial class OverlayWindow : Window
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int GWL_EXSTYLE = -20;

        [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static partial int GetWindowLong(nint hwnd, int index);

        [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static partial int SetWindowLong(nint hwnd, int index, int newStyle);

        private nint _hwnd;
        private bool _isLocked = true;

        public bool IsLocked => _isLocked;

        public OverlayWindow()
        {
            InitializeComponent();
            Left = AppSettings.OverlayLeft;
            Top = AppSettings.OverlayTop;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThrough(_isLocked);
        }

        private void ApplyClickThrough(bool clickThrough)
        {
            int style = GetWindowLong(_hwnd, GWL_EXSTYLE);
            style = clickThrough ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
            int result = SetWindowLong(_hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED);
            if (result == 0)
                System.Diagnostics.Debug.WriteLine("SetWindowLong failed to apply overlay click-through style.");
        }

        /// <summary>Called from the tray menu. When unlocked, the overlay can be dragged by its body.</summary>
        public void SetLocked(bool locked)
        {
            _isLocked = locked;
            ApplyClickThrough(locked);
            RootBorder.BorderBrush = locked ? null : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xA7, 0xD1, 0x29));
            RootBorder.BorderThickness = locked ? new Thickness(0) : new Thickness(1);

            if (locked)
            {
                // These setters persist to disk themselves — no separate Save() call needed.
                AppSettings.OverlayLeft = Left;
                AppSettings.OverlayTop = Top;
            }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLocked) return;
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }

        /// <summary>gameLabel = which game/mode is currently being tracked, e.g. "BloxStrike" or "Roblox".</summary>
        public void UpdateStats(string gameLabel, int fps, double frameTimeMs, long pingMs, bool isStuttering, double[] frameHistory)
        {
            GameNameText.Text = gameLabel;
            FpsText.Text = fps > 0 ? $"FPS: {fps}" : "FPS: --";
            FrameTimeText.Text = frameTimeMs > 0 ? $"Frame: {frameTimeMs:F1} ms" : "Frame: -- ms";
            PingText.Text = pingMs > 0 ? $"Ping: {pingMs} ms" : "Ping: -- ms";
            StutterText.Text = isStuttering ? "⚠ STUTTER" : "";

            DrawGraph(frameHistory);
        }

        private void DrawGraph(double[] frameHistory)
        {
            if (frameHistory.Length < 2)
            {
                FrameTimeGraph.Points.Clear();
                return;
            }

            double w = GraphCanvas.Width;
            double h = GraphCanvas.Height;
            double max = Math.Max(frameHistory.Max(), 1);

            System.Windows.Media.PointCollection points = [];
            for (int i = 0; i < frameHistory.Length; i++)
            {
                double x = i * (w / (frameHistory.Length - 1));
                double y = h - (frameHistory[i] / max * h);
                points.Add(new System.Windows.Point(x, y));
            }
            FrameTimeGraph.Points = points;
        }
    }
}