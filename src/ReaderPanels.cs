using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace FloatLeaf
{
    internal static partial class ReaderPanels
    {
        public static FrameworkElement Appearance(ReaderStore prefs, Action changed, Action close, string shortcut = null)
        {
            DockPanel page = Page(ReaderText.Get("阅读设置", "Reading settings"), close);
            var settings = new StackPanel { Margin = new Thickness(18, 0, 18, 6) };

            var languages = new WrapPanel();
            settings.Children.Add(Label(ReaderText.Get("界面语言", "Interface language"), 12, "PanelInk"));
            settings.Children.Add(languages);
            Action<bool> language = delegate(bool english) {
                if (prefs.English == english) return;
                prefs.English = english;
                changed();
            };
            AddChoice(languages, "中文", "ChineseLanguage", !prefs.English, delegate { language(false); });
            AddChoice(languages, "English", "EnglishLanguage", prefs.English, delegate { language(true); });

            var fontHeader = new DockPanel { Margin = new Thickness(0, 6, 0, 3) };
            var import = ActionButton(page, ReaderText.Get("导入字体…", "Import fonts…"), "ImportFont");
            import.Padding = new Thickness(6, 1, 6, 1);
            DockPanel.SetDock(import, Dock.Right);
            fontHeader.Children.Add(import);
            fontHeader.Children.Add(Label(ReaderText.Get("阅读字体", "Reading fonts"), 12, "PanelInk"));
            settings.Children.Add(fontHeader);

            var fontButton = ActionButton(page, "", "FontMenu");
            var fontButtonRow = new DockPanel { LastChildFill = true };
            var chevron = new System.Windows.Shapes.Path {
                Data = Geometry.Parse("M 1,2 L 5,6 L 9,2"), Stroke = Brushes.Gainsboro,
                StrokeThickness = 1.2, Width = 10, Height = 8, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(7, 0, 0, 0)
            };
            DockPanel.SetDock(chevron, Dock.Right);
            fontButtonRow.Children.Add(chevron);
            var fontSummary = Label("", 12, "PanelInk");
            fontSummary.TextTrimming = TextTrimming.CharacterEllipsis;
            fontButtonRow.Children.Add(fontSummary);
            fontButton.Content = fontButtonRow;
            settings.Children.Add(fontButton);

            var sample = Label("读书人的事，能叫摸鱼吗", 17, "PanelInk");
            sample.HorizontalAlignment = HorizontalAlignment.Center;
            sample.TextWrapping = TextWrapping.Wrap;
            sample.TextAlignment = TextAlignment.Center;
            var sampleFrame = new Border {
                Margin = new Thickness(0, 6, 0, 6), MinHeight = 40, Padding = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = sample
            };
            sampleFrame.SetResourceReference(Border.BorderBrushProperty, "PanelEdge");
            settings.Children.Add(sampleFrame);
            Action updateSample = delegate {
                sample.FontSize = prefs.FontSize;
                sample.Opacity = prefs.TextOpacity;
                sample.Foreground = new SolidColorBrush(prefs.InkColor);
                sampleFrame.Background = new SolidColorBrush(prefs.PaperColor) { Opacity = prefs.BackOpacity };
            };
            Action updateFonts = delegate {
                sample.FontFamily = ReaderFonts.Resolve(prefs.ChineseFontId, prefs.EnglishFontId);
                fontSummary.Text = FontName(true, prefs.ChineseFontId) + "  /  " + FontName(false, prefs.EnglishFontId);
                AutomationProperties.SetName(fontButton, ReaderText.Get("阅读字体", "Reading fonts") + " · " + fontSummary.Text);
            };
            Action apply = delegate { updateSample(); changed(); };
            Action<bool, string> selectFont = delegate(bool chinese, string id) {
                if (chinese) prefs.ChineseFontId = id;
                else prefs.EnglishFontId = id;
                updateFonts();
                changed();
            };
            updateSample();
            updateFonts();
            fontButton.Click += delegate {
                var menu = Dropdown(page, fontButton);
                foreach (bool chinese in new[] { true, false }) {
                    var group = MenuEntry(page, FontGroup(chinese), chinese ? "ChineseFonts" : "EnglishFonts");
                    foreach (var font in ReaderFonts.Choices(chinese)) {
                        string id = font.Id;
                        var entry = MenuEntry(page, prefs.English ? font.EnglishLabel : font.Label, "Font_" + id);
                        entry.IsCheckable = true;
                        entry.IsChecked = id == (chinese ? prefs.ChineseFontId : prefs.EnglishFontId);
                        entry.Click += delegate { selectFont(chinese, id); };
                        group.Items.Add(entry);
                    }
                    menu.Items.Add(group);
                }
                menu.IsOpen = true;
            };
            import.Click += delegate {
                var menu = Dropdown(page, import);
                foreach (bool chinese in new[] { true, false }) {
                    var entry = MenuEntry(page, FontGroup(chinese), chinese ? "ImportChinese" : "ImportEnglish");
                    entry.Click += delegate {
                        var owner = Window.GetWindow(page);
                        var dialog = new Microsoft.Win32.OpenFileDialog {
                            Title = ReaderText.Get("导入字体", "Import a font"),
                            Filter = ReaderText.Get("字体文件", "Font files") + " (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc"
                        };
                        if (dialog.ShowDialog(owner) != true) return;
                        try { selectFont(chinese, ReaderFonts.Import(dialog.FileName, chinese).Id); }
                        catch (Exception error) {
                            MessageBox.Show(owner, ReaderText.Get("无法导入此字体。", "Could not import this font.") + "\n" + error.Message,
                                ReaderText.Get("导入字体", "Import font"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    };
                    menu.Items.Add(entry);
                }
                menu.IsOpen = true;
            };

            AddColor(settings, page, ReaderText.Get("背景颜色", "Background color"), "Paper", prefs.PaperColor,
                delegate(Color value) { prefs.PaperColor = value; apply(); });
            AddColor(settings, page, ReaderText.Get("字体颜色", "Text color"), "Ink", prefs.InkColor,
                delegate(Color value) { prefs.InkColor = value; apply(); });
            AddSlider(settings, ReaderText.Get("字号", "Font size"), 12, 40, prefs.FontSize, "", delegate(double val) {
                prefs.FontSize = val; apply();
            });
            AddSlider(settings, ReaderText.Get("背景不透明度", "Background opacity"), 0, 100, prefs.BackOpacity * 100, "%", delegate(double val) {
                prefs.BackOpacity = val / 100; apply();
            });
            AddSlider(settings, ReaderText.Get("文字不透明度", "Text opacity"), 0, 100, prefs.TextOpacity * 100, "%", delegate(double val) {
                prefs.TextOpacity = val / 100; apply();
            });

            var modeLabel = Label(ReaderText.Get("翻页模式", "Page turns"), 12, "PanelInk");
            modeLabel.Margin = new Thickness(0, 7, 0, 0);
            settings.Children.Add(modeLabel);
            var modes = new WrapPanel();
            settings.Children.Add(modes);
            var pagingHint = Label(ReaderText.Get("滚轮、← / → 键或点击阅读区左右各 25% 可翻页。",
                "Turn pages with the wheel, ← / →, or the outer quarters of the page."), 11, "PanelMuted");
            pagingHint.TextWrapping = TextWrapping.Wrap;
            pagingHint.Margin = new Thickness(0, 1, 0, 4);
            Action<bool> setMode = delegate(bool paged) {
                prefs.Paged = paged;
                pagingHint.Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
                changed();
            };
            pagingHint.Visibility = prefs.Paged ? Visibility.Visible : Visibility.Collapsed;
            AddChoice(modes, ReaderText.Get("滑动", "Scroll"), "ScrollMode", !prefs.Paged, delegate { setMode(false); });
            AddChoice(modes, ReaderText.Get("左右翻页", "Paginated"), "PagedMode", prefs.Paged, delegate { setMode(true); });
            settings.Children.Add(pagingHint);
            AddSwitch(settings, ReaderText.Get("移开鼠标时隐藏", "Hide when pointer leaves"), prefs.HoverHide, delegate(bool on) {
                prefs.HoverHide = on; changed();
            });
            var shortcutLine = Label(shortcut == null ? ReaderText.Get("可通过任务栏最小化和恢复浮页", "Use the taskbar to minimize or restore FloatLeaf.")
                : ReaderText.Get("隐藏 / 唤回小窗", "Hide / show window") + "     " + shortcut, 11, "PanelMuted");
            shortcutLine.Margin = new Thickness(0, 6, 0, 2);
            shortcutLine.TextWrapping = TextWrapping.Wrap;
            settings.Children.Add(shortcutLine);
            var settingsScroll = new ScrollViewer {
                Content = settings, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
            };
            settingsScroll.SetResourceReference(FrameworkElement.StyleProperty, "PanelScroll");
            page.Children.Add(settingsScroll);
            return page;
        }

        private static string FontGroup(bool chinese)
        {
            return chinese ? ReaderText.Get("中文字体", "Chinese font") : ReaderText.Get("英文字体", "English font");
        }

        private static string FontName(bool chinese, string id)
        {
            var choices = ReaderFonts.Choices(chinese);
            foreach (var font in choices)
                if (font.Id == id) return ReaderText.English ? font.EnglishLabel : font.Label;
            return ReaderText.English ? choices[0].EnglishLabel : choices[0].Label;
        }

        private static Button ActionButton(FrameworkElement page, string label, string id)
        {
            var button = new Button { Content = label, Style = (Style)page.FindResource("PanelButton") };
            AutomationProperties.SetAutomationId(button, id);
            if (label.Length > 0) AutomationProperties.SetName(button, label);
            return button;
        }

        private static ContextMenu Dropdown(FrameworkElement page, Button target)
        {
            return new ContextMenu {
                PlacementTarget = target, Placement = PlacementMode.Bottom, VerticalOffset = 3,
                Style = (Style)page.FindResource("FontDropdown"),
                FontFamily = ReaderStyle.Interface
            };
        }

        private static MenuItem MenuEntry(FrameworkElement page, string label, string id)
        {
            var item = new MenuItem { Header = label, Style = (Style)page.FindResource("FontEntry") };
            AutomationProperties.SetAutomationId(item, id);
            return item;
        }

        private static void AddColor(Panel panel, FrameworkElement page, string caption, string id, Color initial, Action<Color> set)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var eye = ActionButton(page, "", "Sample" + id);
            eye.ToolTip = ReaderText.Get("从屏幕吸取颜色", "Pick a color from the screen");
            AutomationProperties.SetName(eye, caption + " · " + eye.ToolTip);
            eye.Content = new System.Windows.Shapes.Path {
                Data = Geometry.Parse("M 4,12 L 11,5 L 14,8 L 7,15 L 3,16 Z M 9,4 L 15,10 M 12,5 L 14,3 Q 16,1 17,3 Q 18,4 16,6 L 15,7"),
                Width = 15, Height = 15, Stretch = Stretch.Uniform, Stroke = Brushes.Gainsboro,
                StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round, StrokeEndLineCap = PenLineCap.Round
            };
            eye.Margin = new Thickness(5, 0, 0, 0);
            DockPanel.SetDock(eye, Dock.Right);
            row.Children.Add(eye);

            var button = ActionButton(page, "", "Pick" + id);
            button.ToolTip = ReaderText.Get("选择颜色", "Choose color");
            AutomationProperties.SetName(button, caption);
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal };
            var chip = new Border { Width = 13, Height = 13, Margin = new Thickness(0, 0, 6, 0), CornerRadius = new CornerRadius(3),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) };
            var hex = Label("", 11, "PanelInk");
            chipRow.Children.Add(chip);
            chipRow.Children.Add(hex);
            button.Content = chipRow;
            DockPanel.SetDock(button, Dock.Right);
            row.Children.Add(button);
            row.Children.Add(Label(caption, 12, "PanelInk"));
            panel.Children.Add(row);
            Color current = initial;
            Action paint = delegate {
                chip.Background = new SolidColorBrush(current);
                hex.Text = String.Format("#{0:X2}{1:X2}{2:X2}", current.R, current.G, current.B);
            };
            paint();
            Action<bool> choose = delegate(bool screen) {
                Color picked;
                var owner = Window.GetWindow(page);
                bool success = screen ? ReaderColorPicker.Sample(owner, out picked) : ReaderColorPicker.Choose(owner, current, out picked);
                if (!success) return;
                current = picked;
                paint();
                set(picked);
            };
            eye.Click += delegate { choose(true); };
            button.Click += delegate { choose(false); };
        }

        public static FrameworkElement Contents(IList<string> titles, int selected, Action<int> choose, Action close, double contentOpacity = 1)
        {
            DockPanel page = Page(ReaderText.Get("章节目录", "Contents"), close);
            var list = new ListBox {
                ItemsSource = titles, SelectedIndex = selected, FontSize = 13,
                Margin = new Thickness(12, 0, 8, 8), Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            list.SetResourceReference(FrameworkElement.StyleProperty, "PanelList");
            list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "PanelChapter");
            list.SetResourceReference(Control.ForegroundProperty, "PanelInk");
            list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(list, ReaderText.Get("章节目录", "Contents"));
            list.Loaded += delegate {
                if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
            };
            list.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args) {
                var source = args.OriginalSource as DependencyObject;
                var item = source == null ? null : ItemsControl.ContainerFromElement(list, source) as ListBoxItem;
                int index = item == null ? -1 : list.ItemContainerGenerator.IndexFromContainer(item);
                if (index >= 0) { args.Handled = true; choose(index); }
            };
            list.PreviewKeyDown += delegate(object sender, KeyEventArgs args) {
                if ((args.Key == Key.Enter || args.Key == Key.Space) && list.SelectedIndex >= 0) {
                    args.Handled = true; choose(list.SelectedIndex);
                }
            };
            page.Children.Add(list);
            return page;
        }

        private static DockPanel Page(string title, Action close)
        {
            var page = new DockPanel {
                LastChildFill = true, Background = Brushes.Transparent,
                Resources = (ResourceDictionary)XamlReader.Parse(Styles)
            };
            TextElementFont(page);

            page.SetResourceReference(Panel.BackgroundProperty, "PanelPaper");
            var header = new DockPanel { Margin = new Thickness(18, 3, 9, 4), Height = 27 };
            var dismiss = new Button {
                Width = 28, Height = 26, Cursor = Cursors.Hand
            };
            AutomationProperties.SetName(dismiss, ReaderText.Get("关闭面板", "Close panel"));
            AutomationProperties.SetHelpText(dismiss, "Esc");
            dismiss.SetResourceReference(FrameworkElement.StyleProperty, "PanelClose");
            dismiss.Click += delegate { close(); };
            DockPanel.SetDock(dismiss, Dock.Right);
            header.Children.Add(dismiss);
            var heading = Label(title, 14, "PanelInk");
            heading.FontWeight = FontWeights.Medium;
            header.Children.Add(heading);
            DockPanel.SetDock(header, Dock.Top);
            page.Children.Add(header);
            page.PreviewKeyDown += delegate(object sender, KeyEventArgs args) {
                if (args.Key == Key.Escape) { args.Handled = true; close(); }
            };
            return page;
        }

        private static void TextElementFont(DependencyObject element)
        {
            element.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty,
                new FontFamily("Microsoft YaHei UI, Segoe UI"));
        }

        private static TextBlock Label(string text, double size, string brush)
        {
            var label = new TextBlock { Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return label;
        }

        private static void AddSlider(Panel owner, string caption, int min, int max, double start, string suffix, Action<double> set)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            var value = Label(Math.Round(start).ToString(CultureInfo.InvariantCulture) + suffix, 11, "PanelMuted");
            value.HorizontalAlignment = HorizontalAlignment.Right;
            row.Children.Add(Label(caption, 12, "PanelInk"));
            row.Children.Add(value);
            owner.Children.Add(row);
            var slider = new Slider {
                Minimum = min, Maximum = max, Value = Math.Round(start), IsSnapToTickEnabled = true,
                TickFrequency = 1, SmallChange = 1, LargeChange = suffix == "%" ? 10 : 2,
                IsMoveToPointEnabled = true, Height = 20
            };
            slider.SetResourceReference(FrameworkElement.StyleProperty, "PanelSlider");
            AutomationProperties.SetName(slider, caption);
            slider.ValueChanged += delegate {
                value.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture) + suffix;
                set(slider.Value);
            };
            owner.Children.Add(slider);
        }

        private static void AddSwitch(Panel owner, string caption, bool initial, Action<bool> set, string id = null)
        {
            var box = new CheckBox { Content = caption, IsChecked = initial, Height = 29, Cursor = Cursors.Hand };
            AutomationProperties.SetName(box, caption);
            if (id != null) AutomationProperties.SetAutomationId(box, id);
            box.SetResourceReference(FrameworkElement.StyleProperty, "PanelSwitch");
            box.Checked += delegate { set(true); };
            box.Unchecked += delegate { set(false); };
            owner.Children.Add(box);
        }

        private static void AddChoice(Panel owner, string caption, string id, bool selected, Action select)
        {
            var option = new RadioButton { Content = caption, IsChecked = selected };
            option.SetResourceReference(FrameworkElement.StyleProperty, "PanelChoice");
            AutomationProperties.SetName(option, caption);
            AutomationProperties.SetAutomationId(option, id);
            option.Checked += delegate { select(); };
            owner.Children.Add(option);
        }

        private const string Styles = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <SolidColorBrush x:Key='PanelPaper' Color='#F51F2022'/>
  <SolidColorBrush x:Key='PanelInk' Color='#F0F0F0'/>
  <SolidColorBrush x:Key='PanelMuted' Color='#BEBEC2'/>
  <SolidColorBrush x:Key='PanelEdge' Color='#49494D'/>
  <SolidColorBrush x:Key='PanelTrack' Color='#717176'/>
  <Style x:Key='PanelButton' TargetType='Button'>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Padding' Value='8,4'/>
    <Setter Property='Foreground' Value='{StaticResource PanelInk}'/>
    <Setter Property='Background' Value='#2A2B2D'/>
    <Setter Property='BorderBrush' Value='#49494D'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border x:Name='frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='5' Padding='{TemplateBinding Padding}'>
          <ContentPresenter VerticalAlignment='Center' RecognizesAccessKey='True'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Background' Value='#37383B'/></Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='#BEBEC2'/></Trigger>
          <Trigger Property='IsPressed' Value='True'><Setter TargetName='frame' Property='Background' Value='#202124'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='FontDropdown' TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ContextMenu'>
        <Border Background='#27282B' BorderBrush='#515257' BorderThickness='1' CornerRadius='7' Padding='4'>
          <ScrollViewer MaxHeight='320' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
            <ItemsPresenter/>
          </ScrollViewer>
        </Border>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='FontEntry' TargetType='MenuItem'>
    <Setter Property='Foreground' Value='#EFEFF0'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='MinWidth' Value='148'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='MenuItem'>
        <Grid>
          <Border x:Name='frame' Background='Transparent' CornerRadius='4' Padding='5,6'>
            <Grid>
              <Grid.ColumnDefinitions><ColumnDefinition Width='15'/><ColumnDefinition Width='*'/><ColumnDefinition Width='12'/></Grid.ColumnDefinitions>
              <Path x:Name='mark' Visibility='Collapsed' Data='M1,5 L4,8 L10,2' Stroke='#EFEFF0' StrokeThickness='1.35' Width='11' Height='10' HorizontalAlignment='Left' VerticalAlignment='Center'/>
              <TextBlock Grid.Column='1' Text='{TemplateBinding Header}' Foreground='{TemplateBinding Foreground}' TextTrimming='CharacterEllipsis' MaxWidth='245' HorizontalAlignment='Left' VerticalAlignment='Center'/>
              <Path x:Name='arrow' Grid.Column='2' Visibility='Collapsed' Data='M1,1 L5,5 L1,9' Stroke='#BCBDC2' StrokeThickness='1.15' Width='7' Height='10' VerticalAlignment='Center'/>
            </Grid>
          </Border>
          <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-1' IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'
                 Focusable='False' AllowsTransparency='True' PopupAnimation='None'>
            <Border Background='#27282B' BorderBrush='#515257' BorderThickness='1' CornerRadius='7' Padding='4'>
              <ScrollViewer MaxHeight='320' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'>
                <ItemsPresenter x:Name='ItemsPresenter' KeyboardNavigation.DirectionalNavigation='Cycle'/>
              </ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='HasItems' Value='True'><Setter TargetName='arrow' Property='Visibility' Value='Visible'/></Trigger>
          <Trigger Property='IsChecked' Value='True'><Setter TargetName='mark' Property='Visibility' Value='Visible'/></Trigger>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='frame' Property='Background' Value='#414247'/></Trigger>
          <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='frame' Property='Background' Value='#414247'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelClose' TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource PanelMuted}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border x:Name='b' Background='Transparent' BorderThickness='1' CornerRadius='3'>
          <Path Data='M1,1 L10,10 M10,1 L1,10' Width='11' Height='11' StrokeThickness='1.2' Stroke='{TemplateBinding Foreground}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='{DynamicResource PanelInk}'/></Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource PanelTrack}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelRepeat' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RepeatButton'>
        <Grid Background='Transparent'><Border CornerRadius='1' Height='2' Background='{TemplateBinding Background}'/></Grid>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelSlider' TargetType='Slider'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Slider'>
        <Border x:Name='frame' CornerRadius='4' BorderThickness='1' Padding='2,0' Background='Transparent'>
          <Track x:Name='PART_Track' Minimum='{TemplateBinding Minimum}' Maximum='{TemplateBinding Maximum}' Value='{Binding Value, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
            <Track.DecreaseRepeatButton><RepeatButton Style='{StaticResource PanelRepeat}' Background='{DynamicResource PanelMuted}' Command='{x:Static Slider.DecreaseLarge}'/></Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb Width='13' Height='18' Focusable='False' Cursor='Hand'>
                <Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'><Ellipse Width='8' Height='8' Fill='{DynamicResource PanelInk}'/></Grid></ControlTemplate></Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Style='{StaticResource PanelRepeat}' Background='{DynamicResource PanelTrack}' Command='{x:Static Slider.IncreaseLarge}'/></Track.IncreaseRepeatButton>
          </Track>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='{DynamicResource PanelTrack}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelChoice' TargetType='RadioButton'>
    <Setter Property='Foreground' Value='{DynamicResource PanelMuted}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='MinHeight' Value='25'/>
    <Setter Property='Margin' Value='0,0,8,0'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RadioButton'>
        <Border x:Name='frame' CornerRadius='3' BorderThickness='1' Background='Transparent' Padding='2,1'>
          <DockPanel VerticalAlignment='Center'>
            <Grid Width='12' Height='12' Margin='0,0,5,0' DockPanel.Dock='Left'>
              <Ellipse Stroke='{TemplateBinding Foreground}' StrokeThickness='1'/>
              <Ellipse x:Name='dot' Width='6' Height='6'/>
            </Grid>
            <ContentPresenter VerticalAlignment='Center'/>
          </DockPanel>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='{DynamicResource PanelInk}'/></Trigger>
          <Trigger Property='IsChecked' Value='True'>
            <Setter Property='Foreground' Value='{DynamicResource PanelInk}'/>
            <Setter TargetName='dot' Property='Fill' Value='{DynamicResource PanelInk}'/>
          </Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='{DynamicResource PanelTrack}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelSwitch' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='{DynamicResource PanelInk}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='CheckBox'>
        <Border x:Name='frame' Background='Transparent' BorderThickness='1' CornerRadius='4'>
          <Grid>
            <ContentPresenter VerticalAlignment='Center'/>
            <Border x:Name='track' BorderBrush='{DynamicResource PanelTrack}' BorderThickness='1' Width='25' Height='13' HorizontalAlignment='Right' VerticalAlignment='Center' CornerRadius='7'>
              <Ellipse x:Name='dot' Margin='2,0' Width='7' Height='7' Fill='{DynamicResource PanelMuted}' HorizontalAlignment='Left'/>
            </Border>
          </Grid>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsChecked' Value='True'>
            <Setter TargetName='track' Property='BorderBrush' Value='{DynamicResource PanelInk}'/>
            <Setter TargetName='dot' Property='Fill' Value='{DynamicResource PanelInk}'/>
            <Setter TargetName='dot' Property='HorizontalAlignment' Value='Right'/>
          </Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='{DynamicResource PanelTrack}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelScrollSpace' TargetType='RepeatButton'>
    <Setter Property='Focusable' Value='False'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelScrollBar' TargetType='ScrollBar'>
    <Setter Property='Width' Value='9'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Grid Background='Transparent'>
          <Track x:Name='PART_Track' Orientation='Vertical' IsDirectionReversed='True'>
            <Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Style='{StaticResource PanelScrollSpace}'/></Track.DecreaseRepeatButton>
            <Track.Thumb><Thumb Focusable='False'>
              <Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'><Border Width='3' Margin='0,2' Background='{DynamicResource PanelTrack}' HorizontalAlignment='Center' CornerRadius='1.5'/></Grid></ControlTemplate></Thumb.Template>
            </Thumb></Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Style='{StaticResource PanelScrollSpace}'/></Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelScroll' TargetType='ScrollViewer'>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ScrollViewer'>
        <Grid Background='Transparent'>
          <Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
          <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' CanContentScroll='{TemplateBinding CanContentScroll}'/>
          <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Style='{StaticResource PanelScrollBar}' Minimum='0' Maximum='{TemplateBinding ScrollableHeight}' Value='{TemplateBinding VerticalOffset}' ViewportSize='{TemplateBinding ViewportHeight}' Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}'/>
        </Grid>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelList' TargetType='ListBox'>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ListBox'>
        <ScrollViewer x:Name='Scroller' Style='{StaticResource PanelScroll}' CanContentScroll='True' VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' Focusable='False'>
          <ItemsPresenter/>
        </ScrollViewer>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style x:Key='PanelChapter' TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource PanelMuted}'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='Padding' Value='5,5'/>
    <Setter Property='Margin' Value='0,1'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <Border x:Name='frame' BorderThickness='1' CornerRadius='3' Background='Transparent' Padding='{TemplateBinding Padding}'>
          <DockPanel>
            <Border x:Name='selection' Width='2' Height='11' Margin='0,0,7,0' CornerRadius='1' VerticalAlignment='Center' DockPanel.Dock='Left'/>
            <TextBlock Text='{Binding Content, RelativeSource={RelativeSource TemplatedParent}}' TextWrapping='Wrap' Foreground='{TemplateBinding Foreground}'/>
          </DockPanel>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='{DynamicResource PanelInk}'/></Trigger>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='selection' Property='Background' Value='{DynamicResource PanelMuted}'/>
            <Setter Property='Foreground' Value='{DynamicResource PanelInk}'/>
          </Trigger>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='{DynamicResource PanelTrack}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
</ResourceDictionary>";
    }
}
