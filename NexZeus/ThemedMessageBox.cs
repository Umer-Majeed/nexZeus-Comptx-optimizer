using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfGrid = System.Windows.Controls.Grid;
using WpfRowDefinition = System.Windows.Controls.RowDefinition;
using WpfColumnDefinition = System.Windows.Controls.ColumnDefinition;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfContentPresenter = System.Windows.Controls.ContentPresenter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;

namespace NexZeus
{
    public enum ThemedMessageBoxIcon
    {
        Question,
        Warning
    }

    public class ThemedMessageBox : Window
    {
        private bool _result;

        private static readonly SolidColorBrush BrandLime = new(WpfColor.FromRgb(0xA7, 0xD1, 0x29));
        private static readonly SolidColorBrush BrightRed = new(WpfColor.FromRgb(0xFF, 0x33, 0x33));
        private static readonly SolidColorBrush TextMuted = new(WpfColor.FromRgb(0x9C, 0xA3, 0xAF));
        private static readonly SolidColorBrush TextWhite = new(WpfColor.FromRgb(0xF9, 0xFA, 0xFB));
        private static readonly SolidColorBrush PanelBg = new(WpfColor.FromRgb(0x16, 0x18, 0x1C));
        private static readonly SolidColorBrush TitleBarBg = new(WpfColor.FromRgb(0x12, 0x13, 0x16));
        private static readonly SolidColorBrush BorderCol = new(WpfColor.FromRgb(0x20, 0x22, 0x27));
        private static readonly SolidColorBrush BtnBg = new(WpfColor.FromRgb(0x1B, 0x1D, 0x23));

        public ThemedMessageBox(string message, string title, ThemedMessageBoxIcon icon)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.Height;
            Width = 380;
            Topmost = true;

            var outer = new WpfBorder
            {
                Background = PanelBg,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Effect = new DropShadowEffect { Color = WpfColors.Black, Direction = 270, ShadowDepth = 6, BlurRadius = 18, Opacity = 0.6 }
            };

            var root = new WpfGrid();
            root.RowDefinitions.Add(new WpfRowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new WpfRowDefinition { Height = GridLength.Auto });

            // Title bar
            var titleBar = new WpfBorder { Background = TitleBarBg, Height = 36, CornerRadius = new CornerRadius(8, 8, 0, 0) };
            titleBar.MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            var titleGrid = new WpfGrid();
            titleGrid.ColumnDefinitions.Add(new WpfColumnDefinition());
            titleGrid.ColumnDefinitions.Add(new WpfColumnDefinition { Width = GridLength.Auto });

            var titleText = new WpfTextBlock
            {
                Text = title,
                Foreground = TextWhite,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            WpfGrid.SetColumn(titleText, 0);

            var closeBtn = new WpfButton
            {
                Content = "✕",
                Width = 36,
                Height = 36,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = TextMuted,
                Cursor = WpfCursors.Hand
            };
            closeBtn.Click += (s, e) => { _result = false; Close(); };
            WpfGrid.SetColumn(closeBtn, 1);

            titleGrid.Children.Add(titleText);
            titleGrid.Children.Add(closeBtn);
            titleBar.Child = titleGrid;
            WpfGrid.SetRow(titleBar, 0);

            // Body
            var bodyPanel = new WpfStackPanel { Orientation = WpfOrientation.Horizontal, Margin = new Thickness(18, 18, 18, 8) };

            var iconBorder = new WpfBorder
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = icon == ThemedMessageBoxIcon.Warning ? BrightRed : BrandLime,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 12, 0)
            };
            var iconText = new WpfTextBlock
            {
                Text = icon == ThemedMessageBoxIcon.Warning ? "!" : "?",
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Foreground = System.Windows.Media.Brushes.Black,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            iconBorder.Child = iconText;

            var msgText = new WpfTextBlock
            {
                Text = message,
                Foreground = TextWhite,
                FontSize = 13,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Width = 300
            };

            bodyPanel.Children.Add(iconBorder);
            bodyPanel.Children.Add(msgText);
            WpfGrid.SetRow(bodyPanel, 1);

            // Buttons
            var btnPanel = new WpfStackPanel
            {
                Orientation = WpfOrientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new Thickness(0, 4, 16, 16)
            };

            var yesBtn = new WpfButton
            {
                Content = "Yes",
                Width = 80,
                Height = 30,
                Margin = new Thickness(0, 0, 8, 0),
                Background = BrandLime,
                Foreground = System.Windows.Media.Brushes.Black,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand
            };
            ApplyRoundedTemplate(yesBtn, 6);
            yesBtn.Click += (s, e) => { _result = true; Close(); };

            var noBtn = new WpfButton
            {
                Content = "No",
                Width = 80,
                Height = 30,
                Background = BtnBg,
                Foreground = TextWhite,
                BorderBrush = BorderCol,
                BorderThickness = new Thickness(1),
                Cursor = WpfCursors.Hand
            };
            ApplyRoundedTemplate(noBtn, 6);
            noBtn.Click += (s, e) => { _result = false; Close(); };

            btnPanel.Children.Add(yesBtn);
            btnPanel.Children.Add(noBtn);
            WpfGrid.SetRow(btnPanel, 2);

            root.Children.Add(titleBar);
            root.Children.Add(bodyPanel);
            root.Children.Add(btnPanel);
            outer.Child = root;
            Content = outer;

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { _result = false; Close(); }
                else if (e.Key == Key.Enter) { _result = true; Close(); }
            };
        }

        private static void ApplyRoundedTemplate(WpfButton btn, double radius)
        {
            var template = new System.Windows.Controls.ControlTemplate(typeof(WpfButton));
            var border = new FrameworkElementFactory(typeof(WpfBorder));
            border.SetValue(WpfBorder.BackgroundProperty, new TemplateBindingExtension(WpfButton.BackgroundProperty));
            border.SetValue(WpfBorder.BorderBrushProperty, new TemplateBindingExtension(WpfButton.BorderBrushProperty));
            border.SetValue(WpfBorder.BorderThicknessProperty, new TemplateBindingExtension(WpfButton.BorderThicknessProperty));
            border.SetValue(WpfBorder.CornerRadiusProperty, new CornerRadius(radius));

            var content = new FrameworkElementFactory(typeof(WpfContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            border.AppendChild(content);

            template.VisualTree = border;
            btn.Template = template;
        }

        public static bool Show(Window? owner, string message, string title, ThemedMessageBoxIcon icon = ThemedMessageBoxIcon.Question)
        {
            var box = new ThemedMessageBox(message, title, icon);
            if (owner != null) box.Owner = owner;
            box.ShowDialog();
            return box._result;
        }
    }
}