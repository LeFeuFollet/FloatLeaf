using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FloatLeaf
{
    internal static class ReaderStyle
    {
        internal static readonly FontFamily Interface = new FontFamily("Segoe UI, Microsoft YaHei UI, Microsoft YaHei");
        internal static readonly DependencyProperty HintPaperProperty = DependencyProperty.RegisterAttached(
            "HintPaper", typeof(Brush), typeof(ReaderStyle), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.Inherits));
        internal static readonly DependencyProperty HintInkProperty = DependencyProperty.RegisterAttached(
            "HintInk", typeof(Brush), typeof(ReaderStyle), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits));
        internal static readonly Style HintStyle = CreateHintStyle();
        private static readonly Style KeyboardFocus = CreateKeyboardFocus();

        internal static SolidColorBrush Brush(string hex, double opacity)
        {
            return Brush((Color)ColorConverter.ConvertFromString(hex), opacity);
        }

        internal static SolidColorBrush Brush(Color color, double opacity)
        {
            var brush = new SolidColorBrush(color);
            brush.Opacity = opacity;
            brush.Freeze();
            return brush;
        }

        internal static TextBlock Label(string value, double size, FontWeight weight)
        {
            return new TextBlock {
                Text = value, FontFamily = Interface, FontSize = size, FontWeight = weight,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
        }

        private static Geometry IconGeometry(string name, double size, double stroke)
        {
            string data;
            switch (name)
            {
                case "open": data = "M3,9 V6 Q3,4.5 4.5,4.5 H9 L11,7 H19.5 Q21,7 21,8.5 V10 M3,10 H22 L19.5,19 H4 Z"; break;
                case "shelf": data = "M3,4 H7 V20 H3 Z M9,4 H13 V20 H9 Z M15,6 L18.5,5 L22,19 L18.5,20 Z"; break;
                case "search": data = "M16,10.5 A6.5,6.5 0 1 1 3,10.5 A6.5,6.5 0 1 1 16,10.5 M14.5,15 L21,21.5"; break;
                case "return": data = "M9,5 L3,11 L9,17 M4,11 H15 Q20,11 20,16 V19"; break;
                case "pin": data = "M7.8,3.5 H16.2 V5 L15.3,5.6 V9.7 Q15.3,11.5 18.2,13.2 V14.7 H5.8 V13.2 Q8.7,11.5 8.7,9.7 V5.6 L7.8,5 Z M12,14.7 V21"; break;
                case "frame": data = "M4,4 H20 V20 H4 Z"; break;
                case "frame-dashed": data = "M4,8 V4 H8 M11,4 H13 M16,4 H20 V8 M20,11 V13 M20,16 V20 H16 M13,20 H11 M8,20 H4 V16 M4,13 V11"; break;
                case "tune": data = "M3,7 L8,7 M14,7 L21,7 M3,17 L12,17 M18,17 L21,17 M14,7 A3,3 0 1 1 8,7 A3,3 0 1 1 14,7 M18,17 A3,3 0 1 1 12,17 A3,3 0 1 1 18,17"; break;
                case "minimize": data = "M5,12 L19,12"; break;
                case "close": data = "M6,6 L18,18 M18,6 L6,18"; break;
                case "list": data = "M4,6 L5,6 M9,6 L20,6 M4,12 L5,12 M9,12 L20,12 M4,18 L5,18 M9,18 L20,18"; break;
                case "grip": data = "M18,6 L6,18 M18,13 L13,18"; break;
                default: data = "M3,5 Q7,4 11.5,7 L11.5,20 Q7,17 3,18 Z M13.5,7 Q17,4 21,5 L21,18 Q17,17 13.5,20 Z"; break;
            }
            var geometry = Geometry.Parse(data).Clone();
            var transform = Matrix.Identity;
            if (name == "pin") transform.RotateAt(37, 12, 12);
            geometry.Transform = new MatrixTransform(transform);
            Rect bounds = geometry.Bounds;
            double scale = (size - 2 * stroke) / Math.Max(bounds.Width, bounds.Height);
            transform.Scale(scale, scale);
            transform.Translate((size - bounds.Width * scale) / 2 - bounds.X * scale,
                                (size - bounds.Height * scale) / 2 - bounds.Y * scale);
            geometry.Transform = new MatrixTransform(transform);
            geometry.Freeze();
            return geometry;
        }

        internal static Path Glyph(string name, double size)
        {
            double stroke = size < 11 ? 1 : 1.1;
            return new Path {
                Width = size, Height = size, Stretch = Stretch.None,
                Data = IconGeometry(name, size, stroke), StrokeThickness = stroke,
                StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            };
        }

        internal static void SetGlyph(Button button, string name)
        {
            var row = (StackPanel)button.Content;
            var icon = (Path)row.Children[0];
            icon.Data = IconGeometry(name, icon.Width, icon.StrokeThickness);
        }

        private static Style CreateHintStyle()
        {
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.BackgroundProperty, new Binding {
                RelativeSource = RelativeSource.Self, Path = new PropertyPath("PlacementTarget.(0)", HintPaperProperty)
            }));
            style.Setters.Add(new Setter(Control.ForegroundProperty, new Binding {
                RelativeSource = RelativeSource.Self, Path = new PropertyPath("PlacementTarget.(0)", HintInkProperty)
            }));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(ToolTip.HasDropShadowProperty, false));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, Interface));
            style.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 4, 7, 4)));
            return style;
        }

        private static Style CreateKeyboardFocus()
        {
            var outline = new FrameworkElementFactory(typeof(Border));
            outline.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            outline.SetValue(Border.BorderBrushProperty, Brush("#888888", .85));
            outline.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            outline.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            outline.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
            var style = new Style(typeof(Control));
            style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = outline }));
            return style;
        }

        internal static Button Tool(string id, string icon, string label, string tip, Action action)
        {
            var button = new Button {
                Height = 27, MinWidth = 28, Padding = new Thickness(label == null ? 5 : 7, 0, label == null ? 5 : 7, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand, ToolTip = tip, FocusVisualStyle = KeyboardFocus
            };
            button.Resources[typeof(ToolTip)] = HintStyle;
            AutomationProperties.SetAutomationId(button, id);
            AutomationProperties.SetName(button, tip);
            ToolTipService.SetInitialShowDelay(button, 300);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var glyph = Glyph(icon, icon == "close" || icon == "minimize" ? 12 : 14);
            glyph.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            row.Children.Add(glyph);
            if (label != null) {
                var title = Label(label, 11, FontWeights.Normal);
                title.Margin = new Thickness(5, 0, 0, 0);
                row.Children.Add(title);
            }
            button.Content = row;
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.Name = "IconContent";
            presenter.SetValue(UIElement.OpacityProperty, .78);
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
            foreach (var property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty }) {
                var highlight = new Trigger { Property = property, Value = true };
                highlight.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "IconContent"));
                template.Triggers.Add(highlight);
            }
            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, .53, "IconContent"));
            template.Triggers.Add(pressed);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .32));
            template.Triggers.Add(disabled);
            button.Template = template;
            button.Click += delegate { action(); };
            return button;
        }
    }
}
