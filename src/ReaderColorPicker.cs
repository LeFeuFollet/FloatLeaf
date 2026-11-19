using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Draw = System.Drawing;
using Forms = System.Windows.Forms;

namespace FloatLeaf
{
    internal static class ReaderColorPicker
    {
        internal static bool IsActive { get; private set; }

        internal static bool Choose(Window owner, Color initial, out Color picked)
        {
            IsActive = true;
            try {
                var editor = new ColorEditor(initial) { Owner = owner };
                bool accepted = editor.ShowDialog() == true;
                picked = accepted ? editor.Value : initial;
                return accepted;
            } finally { IsActive = false; }
        }

        private sealed class ColorEditor : Window
        {
            internal Color Value;
            private readonly Slider[] channels = new Slider[3];
            private readonly TextBlock[] numbers = new TextBlock[3];
            private readonly TextBox hex = new TextBox();
            private readonly Border sample = new Border();
            private readonly Button accept = new Button();
            private bool syncing;

            internal ColorEditor(Color initial)
            {
                Title = ReaderText.Get("选择颜色", "Choose color");
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                SizeToContent = SizeToContent.Height;
                ResizeMode = ResizeMode.NoResize;
                ShowInTaskbar = false;
                Width = 310;
                FontFamily = ReaderStyle.Interface;
                Background = new SolidColorBrush(Color.FromRgb(36, 36, 38));
                Foreground = Brushes.WhiteSmoke;
                var body = new StackPanel { Margin = new Thickness(16) };
                Content = body;

                var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                sample.Width = 60;
                sample.CornerRadius = new CornerRadius(4);
                sample.BorderThickness = new Thickness(1);
                sample.BorderBrush = Brushes.Gray;
                sample.Margin = new Thickness(0, 0, 10, 0);
                header.Children.Add(sample);
                hex.Padding = new Thickness(7, 4, 7, 4);
                AutomationProperties.SetName(hex, "HEX");
                hex.TextChanged += delegate {
                    if (syncing) return;
                    string digits = hex.Text.Trim();
                    if (digits.StartsWith("#", StringComparison.Ordinal)) digits = digits.Substring(1);
                    int rgb = 0;
                    accept.IsEnabled = digits.Length == 6 && Int32.TryParse(digits, NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture, out rgb);
                    if (accept.IsEnabled) SetColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, false);
                };
                header.Children.Add(hex);
                body.Children.Add(header);

                string[] names = { ReaderText.Get("红", "Red"), ReaderText.Get("绿", "Green"), ReaderText.Get("蓝", "Blue") };
                for (int i = 0; i < channels.Length; i++) {
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    row.Children.Add(new TextBlock { Text = names[i], Width = 42, VerticalAlignment = VerticalAlignment.Center });
                    numbers[i] = new TextBlock { Width = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    DockPanel.SetDock(numbers[i], Dock.Right);
                    row.Children.Add(numbers[i]);
                    channels[i] = new Slider { Minimum = 0, Maximum = 255, TickFrequency = 1, IsSnapToTickEnabled = true,
                        IsMoveToPointEnabled = true, VerticalAlignment = VerticalAlignment.Center };
                    AutomationProperties.SetName(channels[i], names[i]);
                    channels[i].ValueChanged += delegate {
                        if (!syncing) SetColor((byte)channels[0].Value, (byte)channels[1].Value, (byte)channels[2].Value, true);
                    };
                    row.Children.Add(channels[i]);
                    body.Children.Add(row);
                }
                var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 14, 0, 0) };
                var cancel = new Button { Content = ReaderText.Get("取消", "Cancel"), MinWidth = 68, Height = 27, IsCancel = true };
                accept.Content = ReaderText.Get("确定", "OK");
                accept.MinWidth = 68;
                accept.Height = 27;
                accept.Margin = new Thickness(8, 0, 0, 0);
                accept.IsDefault = true;
                accept.Click += delegate { DialogResult = true; };
                actions.Children.Add(cancel);
                actions.Children.Add(accept);
                body.Children.Add(actions);
                SetColor(initial.R, initial.G, initial.B, true);
                Loaded += delegate { hex.Focus(); hex.SelectAll(); };
            }

            private void SetColor(byte red, byte green, byte blue, bool writeHex)
            {
                syncing = true;
                Value = Color.FromRgb(red, green, blue);
                sample.Background = new SolidColorBrush(Value);
                channels[0].Value = red;
                channels[1].Value = green;
                channels[2].Value = blue;
                for (int i = 0; i < channels.Length; i++)
                    numbers[i].Text = channels[i].Value.ToString(CultureInfo.InvariantCulture);
                if (writeHex) hex.Text = String.Format("#{0:X2}{1:X2}{2:X2}", red, green, blue);
                accept.IsEnabled = true;
                syncing = false;
            }
        }

        internal static bool Sample(Window owner, out Color picked)
        {
            picked = Colors.Transparent;
            bool visible = owner.IsVisible;
            IsActive = true;
            try {
                if (visible) owner.Hide();
                DwmFlush();
                Draw.Rectangle bounds = Forms.SystemInformation.VirtualScreen;
                using (var image = new Draw.Bitmap(bounds.Width, bounds.Height, Draw.Imaging.PixelFormat.Format32bppRgb)) {
                    using (Draw.Graphics graphics = Draw.Graphics.FromImage(image)) {
                        IntPtr dest = graphics.GetHdc();
                        IntPtr desktop = GetDC(IntPtr.Zero);
                        try {
                            if (desktop == IntPtr.Zero || !BitBlt(dest, 0, 0, bounds.Width, bounds.Height,
                                desktop, bounds.Left, bounds.Top, 0x40CC0020)) throw new Win32Exception();
                        } finally {
                            if (desktop != IntPtr.Zero) ReleaseDC(IntPtr.Zero, desktop);
                            graphics.ReleaseHdc(dest);
                        }
                    }
                    using (var overlay = new SampleOverlay(image, bounds)) {
                        if (overlay.ShowDialog() != Forms.DialogResult.OK) return false;
                        picked = overlay.Value;
                        return true;
                    }
                }
            } catch (Win32Exception) {
                MessageBox.Show(ReaderText.Get("无法读取屏幕颜色。", "Could not read the screen color."),
                    ReaderText.Get("吸色器", "Eyedropper"), MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            } finally {
                IsActive = false;
                if (visible) { owner.Show(); owner.Activate(); }
            }
        }

        private sealed class SampleOverlay : Forms.Form
        {
            internal Color Value;
            private readonly Draw.Bitmap image;
            private readonly Draw.Rectangle desktop;
            private Draw.Rectangle hint;
            private string caption;

            internal SampleOverlay(Draw.Bitmap image, Draw.Rectangle desktop)
            {
                this.image = image;
                this.desktop = desktop;
                FormBorderStyle = Forms.FormBorderStyle.None;
                StartPosition = Forms.FormStartPosition.Manual;
                AutoScaleMode = Forms.AutoScaleMode.None;
                ShowInTaskbar = false;
                TopMost = true;
                Bounds = desktop;
                Cursor = Forms.Cursors.Cross;
                DoubleBuffered = true;
                KeyPreview = true;
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                // A borderless form can otherwise be constrained to the size of a single monitor.
                SetWindowPos(Handle, new IntPtr(-1), desktop.X, desktop.Y, desktop.Width, desktop.Height, 0x0040);
                UpdateHint();
            }

            private void UpdateHint()
            {
                var point = Forms.Cursor.Position;
                int x = point.X - desktop.X, y = point.Y - desktop.Y;
                if ((uint)x >= image.Width || (uint)y >= image.Height) return;
                Draw.Color pixel = image.GetPixel(x, y);
                Value = Color.FromRgb(pixel.R, pixel.G, pixel.B);
                caption = String.Format("#{0:X2}{1:X2}{2:X2}    {3}", pixel.R, pixel.G, pixel.B,
                    ReaderText.Get("单击取色 · Esc 取消", "Click to pick · Esc to cancel"));
                Draw.Size size = Forms.TextRenderer.MeasureText(caption, Font);
                var screen = Forms.Screen.FromPoint(point).Bounds;
                var old = hint;
                int width = size.Width + 14, height = size.Height + 8;
                int left = point.X + width + 20 < screen.Right ? x + 18 : x - width - 12;
                int top = point.Y + height + 25 < screen.Bottom ? y + 21 : y - height - 10;
                hint = new Draw.Rectangle(left, top, width, height);
                Invalidate(old);
                Invalidate(hint);
            }

            protected override void OnMouseMove(Forms.MouseEventArgs e) { base.OnMouseMove(e); UpdateHint(); }

            protected override void OnPaint(Forms.PaintEventArgs e)
            {
                var rect = e.ClipRectangle;
                e.Graphics.DrawImage(image, rect, rect, Draw.GraphicsUnit.Pixel);
                if (String.IsNullOrEmpty(caption) || !rect.IntersectsWith(hint)) return;
                e.Graphics.FillRectangle(Draw.Brushes.Black, hint);
                Forms.TextRenderer.DrawText(e.Graphics, caption, Font, hint, Draw.Color.White,
                    Forms.TextFormatFlags.HorizontalCenter | Forms.TextFormatFlags.VerticalCenter | Forms.TextFormatFlags.NoPadding);
            }

            protected override void OnMouseUp(Forms.MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button == Forms.MouseButtons.Left) { UpdateHint(); DialogResult = Forms.DialogResult.OK; }
                if (e.Button == Forms.MouseButtons.Right) DialogResult = Forms.DialogResult.Cancel;
            }

            protected override void OnKeyDown(Forms.KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Forms.Keys.Escape) { e.Handled = true; DialogResult = Forms.DialogResult.Cancel; }
            }
        }

        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height,
            IntPtr source, int sourceX, int sourceY, uint operation);
    }
}
