using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ShapePath = System.Windows.Shapes.Path;

namespace FloatLeaf
{
    internal sealed class ReaderWindow : Window
    {
        private readonly ReaderStore prefs;
        private EpubBook book;
        private string bookPath;
        private int chapter;
        private int openRequest;
        private bool closed;
        private string hotKeyLabel;
        private bool dialogOpen;
        private bool compactChrome;
        private bool dragging;
        private DateTime showControlsUntil;
        private double displayedSize;
        private string displayedChineseFont, displayedEnglishFont;
        private int wheelDelta;
        private HwndSource source;
        private readonly DispatcherTimer saveTimer = new DispatcherTimer();
        private readonly DispatcherTimer hoverTimer = new DispatcherTimer();
        private readonly List<Action> languageUpdates = new List<Action>();
        private readonly Grid layout = new Grid();
        private readonly Border frame = new Border();
        private readonly Border headerSurface = new Border();
        private readonly Border footerSurface = new Border();
        private readonly Border dragHandle = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        private readonly TextBlock location = ReaderStyle.Label("", 11, FontWeights.Normal);
        private readonly ReaderContent reading = new ReaderContent();
        private readonly Grid overlay = new Grid();
        private readonly TextBlock noticeText = ReaderStyle.Label("", 12, FontWeights.Normal);
        private readonly Border notice = new Border();
        private readonly List<Button> buttons = new List<Button>();
        private Button pin, privacy, settings, dismissNotice;
        private ShapePath gripIcon;
        private WelcomeView welcome;
        private ReaderSearch search;
        private int originChapter;
        private int? originAnchor;
        private double originFraction;
        private bool visitingMatch;
        private Button returnToText;
        private Thumb grip;
        private string panelName;
        private const int ToggleKey = 0x464C;

        internal ReaderWindow(ReaderStore preferences)
        {
            prefs = preferences;
            ReaderText.English = prefs.English;
            FontFamily = ReaderStyle.Interface;
            Resources[typeof(ToolTip)] = ReaderStyle.HintStyle;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.CanMinimize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            MinWidth = 300;
            MinHeight = 245;
            AllowDrop = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            PositionWindow();
            BuildLayout();
            ApplyLanguage();
            ApplyAppearance();
            UpdateView();
            saveTimer.Interval = TimeSpan.FromMilliseconds(700);
            saveTimer.Tick += delegate { saveTimer.Stop(); SaveState(); };
            hoverTimer.Interval = TimeSpan.FromMilliseconds(110);
            hoverTimer.Tick += delegate { UpdateHover(); };
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate { RevealControls(); hoverTimer.Start(); };
            Closing += delegate { closed = true; saveTimer.Stop(); hoverTimer.Stop(); SaveState(); };
            Closed += delegate {
                if (source != null) {
                    if (hotKeyLabel != null) UnregisterHotKey(source.Handle, ToggleKey);
                    source.RemoveHook(WindowMessage);
                }
            };
            LocationChanged += delegate { if (IsLoaded) QueueSave(); };
            SizeChanged += delegate { if (IsLoaded) { UpdateNavigation(); QueueSave(); } };
            PreviewDragOver += OnDragOver;
            PreviewDrop += OnDrop;
            PreviewKeyDown += KeyPressed;
        }

        private void PositionWindow()
        {
            Rect area = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(prefs.WindowWidth, area.Width));
            Height = Math.Max(MinHeight, Math.Min(prefs.WindowHeight, area.Height));
            if (!Double.IsNaN(prefs.WindowLeft) && !Double.IsNaN(prefs.WindowTop)) {
                var proposed = new Rect(prefs.WindowLeft, prefs.WindowTop, Width, Height);
                var desktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                Rect visible = Rect.Intersect(proposed, desktop);
                if (!visible.IsEmpty && visible.Width >= 100 && visible.Height >= 50 &&
                    proposed.Top >= desktop.Top && proposed.Top + 42 <= desktop.Bottom) {
                    Left = proposed.Left;
                    Top = proposed.Top;
                    return;
                }
            }
            Left = Math.Max(area.Left, area.Right - Width - 32);
            Top = Math.Max(area.Top, Math.Min(area.Top + 85, area.Bottom - Height));
        }

        private Button ButtonFor(string id, string icon, string chinese, string english, Action action, string labelZh = null, string labelEn = null)
        {
            string label = labelZh == null ? null : ReaderText.Get(labelZh, labelEn);
            Button button = ReaderStyle.Tool(id, icon, label, ReaderText.Get(chinese, english), action);
            buttons.Add(button);
            languageUpdates.Add(delegate {
                SetHint(button, ReaderText.Get(chinese, english));
                if (labelZh != null)
                    ((TextBlock)((StackPanel)button.Content).Children[1]).Text = ReaderText.Get(labelZh, labelEn);
            });
            return button;
        }

        private static void SetHint(Button button, string hint)
        {
            button.ToolTip = hint;
            AutomationProperties.SetName(button, hint);
        }

        private void ApplyLanguage()
        {
            foreach (Action update in languageUpdates) update();
            location.ToolTip = ReaderText.Get("当前章节 / 总章节", "Current chapter / total chapters");
            grip.ToolTip = ReaderText.Get("拖动调整大小", "Drag to resize");
            AutomationProperties.SetName(reading, ReaderText.Get("阅读正文", "Reading area"));
            UpdateDragHint();
            Title = book == null ? ReaderText.Get("浮页", "FloatLeaf") : book.Title + ReaderText.Get(" · 浮页", " · FloatLeaf");
            UpdateNavigation();
        }

        private void UpdateDragHint()
        {
            dragHandle.ToolTip = hotKeyLabel != null ? ReaderText.Get("拖动小窗 · 隐藏或唤回 ", "Drag window · hide or restore with ") + hotKeyLabel :
                ReaderText.Get("拖动小窗", "Drag window");
        }

        private void BuildLayout()
        {
            frame.CornerRadius = new CornerRadius(8);
            frame.BorderThickness = new Thickness(1);
            frame.Child = layout;
            Content = frame;
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(35) });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
            BuildHeader();
            BuildArticle();
            BuildFooter();
            welcome = new WelcomeView(ChooseFile, delegate {
                ++openRequest;
                EndSearch();
                book = App.Sample();
                bookPath = null;
                ClearNotice();
                RenderChapter(0, 0);
                RevealControls();
            }, OpenBook);
            Grid.SetRow(welcome, 1);
            Grid.SetRowSpan(welcome, 2);
            layout.Children.Add(welcome);
            overlay.Visibility = Visibility.Collapsed;
            overlay.Margin = new Thickness(2, 0, 2, 2);
            Grid.SetRow(overlay, 1);
            Grid.SetRowSpan(overlay, 2);
            layout.Children.Add(overlay);

            notice.Margin = new Thickness(14, 6, 14, 5);
            notice.Padding = new Thickness(12, 5, 5, 5);
            notice.VerticalAlignment = VerticalAlignment.Center;
            notice.CornerRadius = new CornerRadius(7);
            var row = new DockPanel();
            dismissNotice = ButtonFor("DismissNotice", "close", "关闭提示", "Dismiss message", ClearNotice);
            DockPanel.SetDock(dismissNotice, Dock.Right);
            row.Children.Add(dismissNotice);
            noticeText.TextWrapping = TextWrapping.Wrap;
            noticeText.TextTrimming = TextTrimming.None;
            row.Children.Add(noticeText);
            notice.Child = row;
            notice.Visibility = Visibility.Collapsed;
            Grid.SetRow(notice, 1);
            layout.Children.Add(notice);
        }

        private void BuildHeader()
        {
            var header = new DockPanel { Margin = new Thickness(9, 0, 8, 0), Background = Brushes.Transparent };
            headerSurface.Child = header;
            var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            privacy = ButtonFor("PrivacyButton", "frame", "移开鼠标时隐藏", "Hide when the pointer leaves", delegate {
                prefs.HoverHide = !prefs.HoverHide;
                ClosePanel();
                ApplyAppearance();
                RevealControls();
                QueueSave();
            });
            tools.Children.Add(privacy);
            settings = ButtonFor("AppearanceButton", "tune", "阅读设置 (Ctrl+,)", "Reading settings (Ctrl+,)", delegate { TogglePanel("settings"); });
            tools.Children.Add(settings);
            pin = ButtonFor("PinButton", "pin", "始终置顶 (Ctrl+T)", "Always on top (Ctrl+T)", TogglePin);
            tools.Children.Add(pin);
            tools.Children.Add(ButtonFor("MinimizeButton", "minimize", "最小化", "Minimize", delegate { WindowState = WindowState.Minimized; }));
            tools.Children.Add(ButtonFor("CloseButton", "close", "关闭浮页", "Close FloatLeaf", Close));
            DockPanel.SetDock(tools, Dock.Right);
            header.Children.Add(tools);
            dragHandle.MouseLeftButtonDown += MoveWindow;
            header.Children.Add(dragHandle);
            header.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (Keyboard.Modifiers == ModifierKeys.Alt) MoveWindow(sender, e);
            };
            layout.Children.Add(headerSurface);
        }

        private void MoveWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.LeftButton != MouseButtonState.Pressed) return;
            dragging = true;
            try { DragMove(); } finally { dragging = false; }
            e.Handled = true;
        }

        private void BuildArticle()
        {
            reading.PreviewMouseWheel += OnReadingWheel;
            reading.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (Keyboard.Modifiers == ModifierKeys.None && TurnPageAt(e.GetPosition(this).X))
                    e.Handled = true;
            };
            reading.PositionChanged += delegate {
                UpdateNavigation();
                if (IsLoaded && book != null) QueueSave();
            };
            Grid.SetRow(reading, 1);
            layout.Children.Add(reading);
        }

        private bool TurnPageAt(double windowX)
        {
            if (!reading.Paged || !IsReading()) return false;
            if (windowX <= ActualWidth * .25) TurnPage(-1);
            else if (windowX >= ActualWidth * .75) TurnPage(1);
            else return false;
            return true;
        }

        private void OnReadingWheel(object sender, MouseWheelEventArgs e)
        {
            if (!IsReading() || Keyboard.Modifiers != ModifierKeys.None || e.Delta == 0) return;
            int wheelDirection = Math.Sign(e.Delta);
            if (!reading.Paged && !reading.AtScrollEdge(-wheelDirection)) {
                wheelDelta = 0;
                return;
            }
            e.Handled = true;
            if (Math.Sign(wheelDelta) != wheelDirection) wheelDelta = 0;
            wheelDelta += e.Delta;
            while (Math.Abs(wheelDelta) >= Mouse.MouseWheelDeltaForOneLine) {
                wheelDelta -= wheelDirection * Mouse.MouseWheelDeltaForOneLine;
                TurnPage(-wheelDirection);
                if (!reading.Paged) { wheelDelta = 0; break; }
            }
        }

        private void BuildFooter()
        {
            Grid.SetRow(footerSurface, 2);
            var nav = new DockPanel { Margin = new Thickness(10, 0, 19, 0) };
            footerSurface.Child = nav;
            nav.Children.Add(ButtonFor("BookshelfButton", "shelf", "返回书架 (Ctrl+Home)", "Bookshelf (Ctrl+Home)", GoHome));
            nav.Children.Add(ButtonFor("OpenButton", "open", "打开 EPUB (Ctrl+O)", "Open EPUB (Ctrl+O)", ChooseFile));
            nav.Children.Add(ButtonFor("ContentsButton", "list", "章节目录 (Ctrl+D)", "Contents (Ctrl+D)", delegate { TogglePanel("contents"); }));
            nav.Children.Add(ButtonFor("SearchButton", "search", "全文搜索 (Ctrl+F)", "Search this book (Ctrl+F)", delegate { TogglePanel("search"); }));
            returnToText = ButtonFor("ReturnToText", "return", "返回搜索前的段落 (Alt+←)", "Return to your original passage (Alt+Left)", ReturnToOrigin, "返回原段", "Go back");
            returnToText.Visibility = Visibility.Collapsed;
            nav.Children.Add(returnToText);
            location.HorizontalAlignment = HorizontalAlignment.Right;
            nav.Children.Add(location);
            layout.Children.Add(footerSurface);

            grip = new Thumb {
                Width = 17, Height = 17, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE,
                Focusable = false
            };
            var target = new FrameworkElementFactory(typeof(Border));
            target.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            grip.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = target };
            grip.DragStarted += delegate { dragging = true; };
            grip.DragCompleted += delegate { dragging = false; };
            grip.DragDelta += delegate(object sender, DragDeltaEventArgs e) {
                Width = Math.Max(MinWidth, ActualWidth + e.HorizontalChange);
                Height = Math.Max(MinHeight, ActualHeight + e.VerticalChange);
            };
            gripIcon = ReaderStyle.Glyph("grip", 7);
            gripIcon.HorizontalAlignment = HorizontalAlignment.Right;
            gripIcon.VerticalAlignment = VerticalAlignment.Bottom;
            gripIcon.Margin = new Thickness(0, 0, 4, 4);
            gripIcon.IsHitTestVisible = false;
            Grid.SetRow(grip, 2);
            Grid.SetRow(gripIcon, 2);
            Panel.SetZIndex(gripIcon, 3);
            Panel.SetZIndex(grip, 4);
            layout.Children.Add(gripIcon);
            layout.Children.Add(grip);
        }

        private void ApplyAppearance()
        {
            Topmost = prefs.Pinned;
            SetHint(pin, prefs.Pinned ? ReaderText.Get("已置顶，点击取消 (Ctrl+T)", "Turn off always on top (Ctrl+T)") :
                ReaderText.Get("点击置顶 (Ctrl+T)", "Turn on always on top (Ctrl+T)"));
            AutomationProperties.SetItemStatus(pin, prefs.Pinned ? ReaderText.Get("已置顶", "Pinned") : ReaderText.Get("未置顶", "Unpinned"));
            ReaderStyle.SetGlyph(privacy, prefs.HoverHide ? "frame-dashed" : "frame");
            SetHint(privacy, prefs.HoverHide ? ReaderText.Get("已开启移开隐藏，点击关闭", "Turn off hiding when the pointer leaves") :
                ReaderText.Get("开启移开鼠标时隐藏", "Hide when the pointer leaves"));
            AutomationProperties.SetItemStatus(privacy, prefs.HoverHide ? ReaderText.Get("已开启", "On") : ReaderText.Get("已关闭", "Off"));

            var ink = ReaderStyle.Brush(prefs.InkColor, 1);
            var muted = ReaderStyle.Brush(prefs.InkColor, .78);
            frame.Background = ReaderStyle.Brush(prefs.PaperColor, prefs.BackOpacity);
            frame.BorderBrush = ReaderStyle.Brush(prefs.InkColor, .12 * prefs.BackOpacity);
            SetValue(ReaderStyle.HintPaperProperty, ReaderStyle.Brush("#242424", .98));
            SetValue(ReaderStyle.HintInkProperty, Brushes.WhiteSmoke);
            reading.SetInk(ink, muted, prefs.TextOpacity);
            foreach (Button button in buttons) button.Foreground = muted;
            location.Foreground = gripIcon.Stroke = muted;
            pin.Foreground = prefs.Pinned ? ink : muted;
            privacy.Foreground = prefs.HoverHide ? ink : muted;
            welcome.Apply(prefs.TextOpacity, prefs.InkColor);
            noticeText.Foreground = dismissNotice.Foreground = Brushes.WhiteSmoke;
            notice.Background = ReaderStyle.Brush("#242424", .97);
            ApplyReadingLayout();
            SetChrome(compactChrome);
        }

        private void ChooseFile()
        {
            dialogOpen = true;
            try {
                var picker = new OpenFileDialog { Filter = ReaderText.Get("EPUB 电子书", "EPUB books") + " (*.epub)|*.epub", Title = ReaderText.Get("打开 EPUB", "Open EPUB"), CheckFileExists = true };
                if (picker.ShowDialog(this) == true) OpenBook(picker.FileName);
            } finally { dialogOpen = false; RevealControls(); }
        }

        internal async void OpenBook(string path)
        {
            int request = ++openRequest;
            ClearNotice();
            ClosePanel();
            try {
                string fullPath = System.IO.Path.GetFullPath(path);
                ShowNotice(ReaderText.Get("正在打开 ", "Opening ") + System.IO.Path.GetFileName(fullPath) + "…");
                Bookshelf.Entry loaded = await Task.Run(delegate { return Bookshelf.Import(fullPath); });
                if (closed || request != openRequest) return;
                RememberPosition();
                EndSearch();
                book = loaded.Book;
                bookPath = loaded.Path;
                int index;
                double fraction;
                int? anchor;
                prefs.GetPosition(prefs.HasPosition(bookPath) ? bookPath : fullPath, out index, out fraction, out anchor);
                ClearNotice();
                RenderChapter(index, fraction, anchor);
                RevealControls();
                QueueSave();
            } catch (Exception error) {
                if (closed || request != openRequest) return;
                string detail = error.Message;
                if (error is FileNotFoundException || error is DirectoryNotFoundException)
                    detail = ReaderText.Get("找不到这个文件，它可能已被移动。", "The file could not be found. It may have been moved.");
                else if (error is UnauthorizedAccessException)
                    detail = ReaderText.Get("请确认 EPUB 可读，并且软件目录下的 Books 文件夹可写。", "Check that the EPUB is readable and the Books folder beside FloatLeaf is writable.");
                else if (error is InvalidDataException && !prefs.English && !detail.Any(ch => ch >= 0x3400 && ch <= 0x9fff))
                    detail = "文件可能已损坏，或不是有效的 EPUB。";
                ShowNotice(ReaderText.Get("无法打开电子书。", "Could not open the book. ") + detail);
            }
        }

        private void GoHome()
        {
            ++openRequest;
            RememberPosition();
            EndSearch();
            ClosePanel();
            book = null;
            bookPath = null;
            Title = ReaderText.Get("浮页", "FloatLeaf");
            ClearNotice();
            welcome.RefreshBooks();
            RevealControls();
            QueueSave();
        }

        private void EndSearch()
        {
            search = null;
            visitingMatch = false;
            returnToText.Visibility = Visibility.Collapsed;
        }

        private FrameworkElement SearchPanel()
        {
            if (search == null) {
                int paragraph, offset;
                reading.GetTextLocation(out paragraph, out offset);
                originChapter = chapter;
                originAnchor = reading.Anchor();
                originFraction = reading.Fraction();
                search = new ReaderSearch(book, chapter, paragraph, offset);
            }
            return ReaderPanels.Search(search, prefs.ShowFutureMatches, delegate(bool show) {
                prefs.ShowFutureMatches = show;
                QueueSave();
            }, VisitMatch, ReturnToOrigin, ClosePanel);
        }

        private void VisitMatch(int targetChapter, int paragraph, int offset, int length)
        {
            visitingMatch = true;
            ClosePanel();
            if (targetChapter != chapter) RenderChapter(targetChapter, 0);
            reading.ShowMatch(paragraph, offset, length);
            returnToText.Visibility = Visibility.Visible;
            RevealControls();
        }

        private void ReturnToOrigin()
        {
            if (search == null) return;
            ClosePanel();
            if (visitingMatch) RenderChapter(originChapter, originFraction, originAnchor);
            EndSearch();
            QueueSave();
        }

        private void ApplyReadingLayout()
        {
            if (displayedSize == prefs.FontSize && displayedChineseFont == prefs.ChineseFontId &&
                displayedEnglishFont == prefs.EnglishFontId && reading.Paged == prefs.Paged) return;
            displayedSize = prefs.FontSize;
            displayedChineseFont = prefs.ChineseFontId;
            displayedEnglishFont = prefs.EnglishFontId;
            wheelDelta = 0;
            reading.Configure(prefs.Paged, ReaderFonts.Resolve(prefs.ChineseFontId, prefs.EnglishFontId), prefs.FontSize);
        }

        private void RenderChapter(int index, double fraction, int? anchor = null)
        {
            chapter = Math.Max(0, Math.Min(index, book.Chapters.Count - 1));
            Title = book.Title + ReaderText.Get(" · 浮页", " · FloatLeaf");
            string label = bookPath == null ? book.Title + ReaderText.Get("  ·  内置试读", "  ·  Sample") : book.Title;
            reading.LoadChapter(label, book.Chapters[chapter], anchor, fraction);
            UpdateNavigation();
        }

        private bool IsReading()
        {
            return book != null && reading.Visibility == Visibility.Visible;
        }

        private void UpdateNavigation()
        {
            if (book == null) return;
            string chapters = String.Format("{0} / {1}", chapter + 1, book.Chapters.Count);
            location.Text = reading.Paged ? String.Format(ReaderText.Get("{0}  ·  第 {1} 页", "{0}  ·  Page {1}"), chapters, reading.PageNumber + 1) : chapters;
        }

        private void TurnPage(int direction)
        {
            if (!IsReading() || reading.Turn(direction)) return;
            int target = chapter + direction;
            if (target >= 0 && target < book.Chapters.Count) {
                RenderChapter(target, direction < 0 ? 1 : 0);
                QueueSave();
            }
        }

        private void TogglePin()
        {
            prefs.Pinned = !prefs.Pinned;
            ApplyAppearance();
            QueueSave();
        }

        private void TogglePanel(string name)
        {
            if (name != "settings" && book == null) return;
            if (panelName == name) { ClosePanel(); return; }
            ShowPanel(name);
        }

        private void ShowPanel(string name)
        {
            ClearNotice();
            if (panelName == "search" && name != "search" && !visitingMatch) EndSearch();
            overlay.Children.Clear();
            panelName = name;
            FrameworkElement panel;
            if (name == "settings") {
                panel = ReaderPanels.Appearance(prefs, PreferencesChanged, ClosePanel, hotKeyLabel);
            } else if (name == "search") {
                panel = SearchPanel();
            } else {
                panel = ReaderPanels.Contents(book.Chapters.Select(c => c.Title).ToList(), chapter, delegate(int selected) {
                    ClosePanel();
                    if (selected != chapter) { RenderChapter(selected, 0); QueueSave(); }
                }, ClosePanel, prefs.TextOpacity);
            }
            overlay.Children.Add(panel);
            UpdateView();
            RevealControls();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate {
                if (panelName == name && name != "search") panel.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
            }));
        }

        private void PreferencesChanged()
        {
            bool switchLanguage = prefs.English != ReaderText.English;
            if (switchLanguage) {
                ReaderText.English = prefs.English;
                if (book != null) RenderChapter(chapter, reading.Fraction(), reading.Anchor());
                ApplyLanguage();
            }
            ApplyAppearance();
            if (switchLanguage) ShowPanel("settings");
            QueueSave();
        }

        private void ClosePanel()
        {
            if (panelName == null) return;
            if (panelName == "search" && !visitingMatch) EndSearch();
            panelName = null;
            overlay.Children.Clear();
            UpdateView();
            if (book != null) reading.Focus();
            else settings.Focus();
            RevealControls();
        }

        private void ShowNotice(string message)
        {
            noticeText.Text = message;
            notice.Visibility = Visibility.Visible;
            UpdateView();
            RevealControls();
        }

        private void ClearNotice()
        {
            notice.Visibility = Visibility.Collapsed;
            UpdateView();
        }

        private void UpdateView()
        {
            bool showPage = panelName == null && notice.Visibility != Visibility.Visible;
            bool showBook = showPage && book != null;
            reading.Visibility = showBook ? Visibility.Visible : Visibility.Hidden;
            footerSurface.Visibility = showBook ? Visibility.Visible : Visibility.Hidden;
            welcome.Visibility = showPage && book == null ? Visibility.Visible : Visibility.Collapsed;
            overlay.Visibility = panelName != null && notice.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            UpdateNavigation();
        }

        private void RememberPosition()
        {
            if (bookPath == null) return;
            if (visitingMatch) prefs.SetPosition(bookPath, originChapter, originFraction, originAnchor);
            else prefs.SetPosition(bookPath, chapter, reading.Fraction(), reading.Anchor());
        }

        private void QueueSave()
        {
            if (closed) return;
            saveTimer.Stop();
            saveTimer.Start();
        }

        private void SaveState()
        {
            RememberPosition();
            if (WindowState == WindowState.Normal) {
                prefs.WindowWidth = Width;
                prefs.WindowHeight = Height;
                prefs.WindowLeft = Left;
                prefs.WindowTop = Top;
            }
            prefs.Save();
        }

        private void KeyPressed(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control) {
                switch (e.Key) {
                    case Key.O: ChooseFile(); break;
                    case Key.D: TogglePanel("contents"); break;
                    case Key.F: TogglePanel("search"); break;
                    case Key.Home: GoHome(); break;
                    case Key.OemComma: TogglePanel("settings"); break;
                    case Key.T: TogglePin(); break;
                    default: return;
                }
            } else if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.Left || e.SystemKey == Key.Left) && visitingMatch) ReturnToOrigin();
            else if (e.Key == Key.Escape && panelName != null) ClosePanel();
            else if (e.Key == Key.Escape && notice.Visibility == Visibility.Visible) ClearNotice();
            else if (IsReading() && Keyboard.Modifiers == ModifierKeys.None) {
                switch (e.Key) {
                    case Key.Left: case Key.PageUp: TurnPage(-1); break;
                    case Key.Right: case Key.PageDown: TurnPage(1); break;
                    case Key.Space:
                        if (Keyboard.FocusedElement is Button) return;
                        TurnPage(1); break;
                    case Key.Up: case Key.Down:
                        if (!reading.Paged) reading.ScrollLine(e.Key == Key.Up ? -1 : 1);
                        break;
                    case Key.Home: case Key.End: reading.ChapterEdge(e.Key == Key.End); break;
                    default: return;
                }
            } else return;
            e.Handled = true;
        }

        private static string DroppedBook(IDataObject data)
        {
            if (!data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            return files == null ? null : files.FirstOrDefault(p => p.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DroppedBook(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            string path = DroppedBook(e.Data);
            if (path != null) OpenBook(path);
            e.Handled = true;
        }

        private void RevealControls()
        {
            showControlsUntil = DateTime.UtcNow.AddSeconds(2.5);
            SetChrome(false);
            Opacity = 1;
        }

        private void SetChrome(bool compact)
        {
            compactChrome = compact;
            double opacity = compact ? 0 : panelName == null ? prefs.TextOpacity : Math.Max(.55, prefs.TextOpacity);
            headerSurface.Opacity = footerSurface.Opacity = gripIcon.Opacity = opacity;
            headerSurface.IsHitTestVisible = footerSurface.IsHitTestVisible = grip.IsHitTestVisible = !compact;
        }

        private void UpdateHover()
        {
            if (!IsVisible || WindowState == WindowState.Minimized || source == null) return;
            NativePoint mouse;
            NativeRect rect;
            if (!GetCursorPos(out mouse) || !GetWindowRect(source.Handle, out rect)) return;
            bool inside = mouse.X >= rect.Left && mouse.X < rect.Right && mouse.Y >= rect.Top && mouse.Y < rect.Bottom;
            bool editing = panelName != null || dialogOpen || dragging || reading.NoteOpen || notice.Visibility == Visibility.Visible;
            bool away = !inside && !editing && DateTime.UtcNow >= showControlsUntil;
            double opacity = away && prefs.HoverHide ? 0 : 1;
            if (Opacity != opacity) Opacity = opacity;
            bool compact = away && book != null && prefs.BackOpacity < .48;
            if (compact != compactChrome) SetChrome(compact);
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source.AddHook(WindowMessage);
            if (RegisterHotKey(source.Handle, ToggleKey, 0x4003, 0x52))
                hotKeyLabel = "Ctrl+Alt+R";
            else if (RegisterHotKey(source.Handle, ToggleKey, 0x4003, 0x78))
                hotKeyLabel = "Ctrl+Alt+F9";
            else if (RegisterHotKey(source.Handle, ToggleKey, 0x4006, 0x78))
                hotKeyLabel = "Ctrl+Shift+F9";
            UpdateDragHint();
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0312 && wParam.ToInt32() == ToggleKey && !dialogOpen && IsEnabled && !ReaderColorPicker.IsActive) {
                if (IsVisible && WindowState != WindowState.Minimized && Opacity > 0) Hide();
                else {
                    Show();
                    WindowState = WindowState.Normal;
                    RevealControls();
                    Activate();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    }
}
