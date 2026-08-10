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

            var (safeLeft, safeTop) = ClampToVirtualScreen(AppSettings.OverlayLeft, AppSettings.OverlayTop);
            Left = safeLeft;
            Top = safeTop;

            // If the saved position was off-screen (e.g. a monitor was
            // unplugged, resolution changed, or a second monitor was
            // removed), persist the corrected position immediately so
            // we don't have to re-clamp every launch.
            if (safeLeft != AppSettings.OverlayLeft || safeTop != AppSettings.OverlayTop)
            {
                AppSettings.OverlayLeft = safeLeft;
                AppSettings.OverlayTop = safeTop;
            }
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
                // Clamp before saving — the user could have dragged the
                // overlay partly (or fully) off a monitor edge before
                // re-locking it.
                var (safeLeft, safeTop) = ClampToVirtualScreen(Left, Top);
                Left = safeLeft;
                Top = safeTop;

                // These setters persist to disk themselves — no separate Save() call needed.
                AppSettings.OverlayLeft = Left;
                AppSettings.OverlayTop = Top;
            }
        }

        /// <summary>
        /// Keeps the overlay's top-left corner within the combined bounds of
        /// all connected monitors, so it never opens fully off-screen after a
        /// monitor is unplugged, a resolution changes, or a saved position
        /// from a different multi-monitor setup gets restored.
        /// </summary>
        private (double left, double top) ClampToVirtualScreen(double left, double top)
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = SystemParameters.VirtualScreenWidth;
            double virtualHeight = SystemParameters.VirtualScreenHeight;

            // Fall back to sane defaults if WPF hasn't resolved monitor info yet.
            if (virtualWidth <= 0) virtualWidth = SystemParameters.PrimaryScreenWidth;
            if (virtualHeight <= 0) virtualHeight = SystemParameters.PrimaryScreenHeight;

            double overlayWidth = ActualWidth > 0 ? ActualWidth : (Width > 0 ? Width : 260);
            double overlayHeight = ActualHeight > 0 ? ActualHeight : (Height > 0 ? Height : 120);

            // Keep at least a sliver of the overlay (60px) visible on-screen
            // rather than forcing it fully inside, so it still "snaps back"
            // naturally if the user was dragging near an edge on purpose.
            const double minVisible = 60;

            double minLeft = virtualLeft - overlayWidth + minVisible;
            double maxLeft = virtualLeft + virtualWidth - minVisible;
            double minTop = virtualTop - overlayHeight + minVisible;
            double maxTop = virtualTop + virtualHeight - minVisible;

            double clampedLeft = Math.Clamp(left, minLeft, Math.Max(minLeft, maxLeft));
            double clampedTop = Math.Clamp(top, minTop, Math.Max(minTop, maxTop));

            return (clampedLeft, clampedTop);
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