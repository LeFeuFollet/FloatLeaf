using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FloatLeaf
{
    internal sealed class ReaderContent : Grid
    {
        private static readonly Thickness PaperMargin = new Thickness(20, 10, 20, 8);
        private static readonly Brush PageHitAreas = CreatePageHitAreas();
        private readonly RichTextBox scroll = new RichTextBox();
        private readonly DocumentPageView page = new DocumentPageView();
        private readonly FlowDocument document = new FlowDocument();
        private readonly Paragraph label = new Paragraph();
        private readonly Paragraph title = new Paragraph();
        private readonly Section body = new Section();
        private readonly List<Paragraph> paragraphs = new List<Paragraph>();
        private readonly List<Paragraph> annotated = new List<Paragraph>();
        private readonly List<KeyValuePair<Rect, EpubNote>> pageNotes = new List<KeyValuePair<Rect, EpubNote>>();
        private DocumentPage mappedPage;
        private readonly List<Run> searchSplits = new List<Run>();
        private readonly Popup notePopup = new Popup { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.RelativePoint };
        private EpubChapter currentChapter;
        private int matchParagraph = -1, matchStart, matchEnd;
        private DynamicDocumentPaginator paginator;
        private int pageAnchor;
        private int? reflowAnchor;
        private int? pendingAnchor;
        private double pendingFraction;
        private bool pending;
        private DispatcherOperation scheduled;

        internal bool Paged { get; private set; }
        internal bool NoteOpen { get { return notePopup.IsOpen; } }
        internal int PageNumber { get { return page.PageNumber; } }
        private double ScrollableHeight { get { return Math.Max(0, scroll.ExtentHeight - scroll.ViewportHeight); } }
        internal event Action PositionChanged;

        private static Brush CreatePageHitAreas()
        {
            // Layered windows pass fully transparent pixels to the window underneath.
            Color edge = Color.FromArgb(1, 28, 28, 28);
            Color clear = Color.FromArgb(0, 28, 28, 28);
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            brush.GradientStops.Add(new GradientStop(edge, .25));
            brush.GradientStops.Add(new GradientStop(clear, .25));
            brush.GradientStops.Add(new GradientStop(clear, .75));
            brush.GradientStops.Add(new GradientStop(edge, .75));
            brush.Freeze();
            return brush;
        }

        internal ReaderContent()
        {
            Background = Brushes.Transparent;
            Focusable = true;
            FocusVisualStyle = null;
            ClipToBounds = true;
            AutomationProperties.SetAutomationId(scroll, "ReadingScroll");
            AutomationProperties.SetAutomationId(page, "ReadingPage");
            scroll.IsReadOnly = true;
            scroll.IsUndoEnabled = false;
            scroll.IsDocumentEnabled = false;
            scroll.IsReadOnlyCaretVisible = false;
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Background = Brushes.Transparent;
            scroll.BorderThickness = new Thickness(0);
            scroll.Padding = PaperMargin;
            scroll.FocusVisualStyle = null;
            scroll.Loaded += delegate {
                var viewport = scroll.Template.FindName("PART_ContentHost", scroll) as ScrollViewer;
                if (viewport != null) viewport.PanningMode = PanningMode.VerticalOnly;
            };
            scroll.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnScrollChanged));
            page.Stretch = Stretch.None;
            page.Visibility = Visibility.Collapsed;
            page.MouseMove += delegate(object sender, MouseEventArgs e) {
                page.Cursor = NoteAt(e.GetPosition(this), null) == null ? null : Cursors.Hand;
            };
            Children.Add(scroll);
            Children.Add(page);
            document.TextAlignment = TextAlignment.Left;
            document.Background = Brushes.Transparent;
            label.FontFamily = ReaderStyle.Interface;
            label.FontSize = 11;
            label.Margin = new Thickness(0, 0, 0, 5);
            title.FontWeight = FontWeights.Medium;
            title.Margin = new Thickness(0, 0, 0, 13);
            AutomationProperties.SetAutomationId(title, "ChapterTitle");
            body.Margin = new Thickness(0);
            document.Blocks.Add(label);
            document.Blocks.Add(title);
            document.Blocks.Add(body);
            scroll.Document = document;
            notePopup.PlacementTarget = this;
            PreviewMouseLeftButtonDown += OpenNoteAtPointer;
            PreviewMouseWheel += delegate { CloseNote(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Escape && notePopup.IsOpen) { CloseNote(); e.Handled = true; }
            };
            IsVisibleChanged += delegate { if (!IsVisible) CloseNote(); };
            SizeChanged += delegate {
                CloseNote();
                if (pending) ScheduleRestore();
                else Restore(reflowAnchor, 0);
            };
        }

        internal void SetInk(Brush ink, Brush muted, double opacity)
        {
            if (!SameBrush(document.Foreground, ink)) document.Foreground = ink;
            if (!SameBrush(label.Foreground, muted)) label.Foreground = muted;
            if (scroll.Opacity != opacity) scroll.Opacity = page.Opacity = opacity;
        }

        private static bool SameBrush(Brush current, Brush next)
        {
            if (ReferenceEquals(current, next)) return true;
            var oldColor = current as SolidColorBrush;
            var newColor = next as SolidColorBrush;
            return oldColor != null && newColor != null &&
                oldColor.Color == newColor.Color && oldColor.Opacity == newColor.Opacity;
        }

        internal void Configure(bool paged, FontFamily family, double size)
        {
            int? anchor = Anchor();
            double fraction = Fraction();
            if (paged != Paged) {
                if (paged) {
                    // A FlowDocument can have only one active formatter.
                    scroll.Document = new FlowDocument();
                    paginator = (DynamicDocumentPaginator)((IDocumentPaginatorSource)document).DocumentPaginator;
                    paginator.IsBackgroundPaginationEnabled = false;
                    document.ColumnWidth = Double.PositiveInfinity;
                    document.PagePadding = PaperMargin;
                    page.DocumentPaginator = paginator;
                } else {
                    page.DocumentPaginator = null;
                    paginator = null;
                    scroll.Document = document;
                }
                Paged = paged;
                Background = paged ? PageHitAreas : Brushes.Transparent;
                scroll.Visibility = paged ? Visibility.Collapsed : Visibility.Visible;
                page.Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
            }
            document.FontFamily = family;
            document.FontSize = size;
            title.FontSize = Math.Max(18, size * 1.2);
            foreach (Paragraph paragraph in annotated)
                foreach (Run run in paragraph.Inlines)
                    if (run.Tag is EpubNote) run.FontSize = NoteSize();
            body.LineHeight = Math.Ceiling(size * 1.68);
            body.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            Restore(anchor, fraction);
        }

        internal void LoadChapter(string bookLabel, EpubChapter chapter, int? anchor, double fraction)
        {
            CloseNote();
            currentChapter = chapter;
            paragraphs.Clear();
            annotated.Clear();
            mappedPage = null;
            searchSplits.Clear();
            matchParagraph = -1;
            label.Inlines.Clear();
            label.Inlines.Add(new Run(bookLabel));
            title.Inlines.Clear();
            title.Inlines.Add(new Run(chapter.Title));
            body.Blocks.Clear();
            for (int i = 0; i < chapter.Paragraphs.Count; i++) {
                string text = chapter.Paragraphs[i];
                Paragraph paragraph = title;
                if (i != 0 || text.Trim() != chapter.Title.Trim()) {
                    paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 12), MinOrphanLines = 1, MinWidowLines = 1 };
                    body.Blocks.Add(paragraph);
                }
                paragraphs.Add(paragraph);
                RenderParagraph(i);
                foreach (Run run in paragraph.Inlines) {
                    if (run.Tag is EpubNote) { annotated.Add(paragraph); break; }
                }
            }
            Restore(anchor, fraction);
        }

        private double NoteSize() { return Math.Max(9, document.FontSize * .72); }

        private void RenderParagraph(int index)
        {
            mappedPage = null;
            Paragraph paragraph = paragraphs[index];
            paragraph.Inlines.Clear();
            string text = currentChapter.Paragraphs[index];
            int start = 0;
            foreach (EpubNote note in currentChapter.NotesFor(index)) {
                AddText(paragraph, index, text, start, note.Start, null);
                AddText(paragraph, index, text, note.Start, note.Start + note.Length, note);
                start = note.Start + note.Length;
            }
            AddText(paragraph, index, text, start, text.Length, null);
        }

        private void AddText(Paragraph paragraph, int index, string text, int start, int end, EpubNote note)
        {
            for (int pos = start; pos < end; ) {
                int next = end;
                bool matched = index == matchParagraph && pos >= matchStart && pos < matchEnd;
                if (index == matchParagraph) {
                    if (pos < matchStart) next = Math.Min(end, matchStart);
                    else if (matched) next = Math.Min(end, matchEnd);
                }
                var run = new Run(text.Substring(pos, next - pos));
                if (note != null) {
                    run.Tag = note;
                    run.BaselineAlignment = BaselineAlignment.Superscript;
                    run.FontSize = NoteSize();
                    run.TextDecorations = TextDecorations.Underline;
                    run.Cursor = Cursors.Hand;
                }
                if (matched) { run.Background = Brushes.Khaki; run.Foreground = Brushes.Black; }
                paragraph.Inlines.Add(run);
                if (pos != start) searchSplits.Add(run);
                pos = next;
            }
        }

        // Bookmarks use the unhighlighted document's symbol offsets. A split run
        // inserts two formatting symbols without adding a character to the text.
        private int StableOffset(TextPointer position)
        {
            int actual = document.ContentStart.GetOffsetToPosition(position), result = actual;
            foreach (Run split in searchSplits) {
                int leftEnd = document.ContentStart.GetOffsetToPosition(split.ElementStart) - 1;
                result -= Math.Max(0, Math.Min(2, actual - leftEnd));
            }
            return result;
        }

        private TextPointer AtStableOffset(int offset)
        {
            foreach (Run split in searchSplits) {
                int leftEnd = document.ContentStart.GetOffsetToPosition(split.ElementStart) - 1;
                if (offset >= leftEnd) offset += 2;
            }
            int length = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
            return document.ContentStart.GetPositionAtOffset(Math.Max(0, Math.Min(offset, length)), LogicalDirection.Forward);
        }

        private static TextPointer AtCharacter(Paragraph paragraph, int character)
        {
            foreach (Run run in paragraph.Inlines) {
                if (character < run.Text.Length) return run.ContentStart.GetPositionAtOffset(character, LogicalDirection.Forward);
                character -= run.Text.Length;
            }
            return paragraph.ContentEnd;
        }

        internal void GetTextLocation(out int paragraph, out int character)
        {
            paragraph = character = 0;
            if (paragraphs.Count == 0) return;
            TextPointer position = AtStableOffset(Anchor() ?? 0);
            while (paragraph < paragraphs.Count - 1 && paragraphs[paragraph].ContentEnd.CompareTo(position) < 0)
                paragraph++;
            foreach (Run run in paragraphs[paragraph].Inlines) {
                if (position.CompareTo(run.ContentStart) <= 0) break;
                if (position.CompareTo(run.ContentEnd) <= 0) {
                    character += run.ContentStart.GetOffsetToPosition(position);
                    break;
                }
                character += run.Text.Length;
            }
        }

        internal void ShowMatch(int paragraph, int character, int length)
        {
            if (paragraph < 0 || paragraph >= paragraphs.Count) return;
            ResetMatch();
            matchParagraph = paragraph;
            int end = currentChapter.Paragraphs[paragraph].Length;
            matchStart = Math.Max(0, Math.Min(character, end));
            matchEnd = matchStart + Math.Min(Math.Max(0, length), end - matchStart);
            RenderParagraph(paragraph);
            Restore(StableOffset(AtCharacter(paragraphs[paragraph], matchStart)), 0);
        }

        private void ResetMatch()
        {
            int prior = matchParagraph;
            if (prior < 0) return;
            matchParagraph = -1;
            searchSplits.Clear();
            RenderParagraph(prior);
        }

        internal void ClearMatch()
        {
            if (matchParagraph < 0) return;
            int? anchor = Anchor();
            double fraction = Fraction();
            ResetMatch();
            Restore(anchor, fraction);
        }

        private void OpenNoteAtPointer(object sender, MouseButtonEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            Point cursor = e.GetPosition(this);
            EpubNote note = NoteAt(cursor, e.OriginalSource as Run);
            if (note == null) return;
            e.Handled = true;
            ShowNote(note.Text, cursor);
        }

        private EpubNote NoteAt(Point cursor, Run run)
        {
            if (Paged && annotated.Count > 0 && page.DocumentPage != null) {
                DocumentPage shown = page.DocumentPage;
                Visual visual = shown.Visual;
                var host = visual as IContentHost;
                if (host != null && IsAncestorOf(visual)) {
                    Point pagePoint = TransformToDescendant(visual).Transform(cursor);
                    if (mappedPage != shown) MapPageNotes(shown, host);
                    foreach (var entry in pageNotes)
                        if (entry.Key.Contains(pagePoint)) return entry.Value;
                }
            }
            if (run == null && !Paged) {
                var pointer = scroll.GetPositionFromPoint(TranslatePoint(cursor, scroll), false);
                if (pointer != null) run = pointer.Parent as Run;
            }
            return run == null ? null : run.Tag as EpubNote;
        }

        private void MapPageNotes(DocumentPage shown, IContentHost host)
        {
            mappedPage = shown;
            pageNotes.Clear();
            var first = paginator.GetPagePosition(shown) as TextPointer;
            foreach (Paragraph paragraph in annotated) {
                if (first != null && paragraph.ContentEnd.CompareTo(first) < 0) continue;
                foreach (Run run in paragraph.Inlines) {
                    var note = run.Tag as EpubNote;
                    if (note == null || (first != null && run.ContentEnd.CompareTo(first) < 0)) continue;
                    var bounds = host.GetRectangles(run);
                    if (bounds.Count == 0) return;
                    foreach (Rect rect in bounds) pageNotes.Add(new KeyValuePair<Rect, EpubNote>(rect, note));
                }
            }
        }

        private void ShowNote(string text, Point point)
        {
            CloseNote();
            var heading = new DockPanel { LastChildFill = true };
            var close = new Button {
                Content = "×", FontSize = 16, MinWidth = 26, Padding = new Thickness(2, 0, 2, 0),
                Foreground = Brushes.WhiteSmoke, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, ToolTip = ReaderText.Get("关闭注释", "Close note")
            };
            AutomationProperties.SetName(close, (string)close.ToolTip);
            close.Click += delegate { CloseNote(); };
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
            heading.Children.Add(new TextBlock { Text = ReaderText.Get("注释", "Note"), Foreground = Brushes.Silver,
                VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            var content = new DockPanel();
            DockPanel.SetDock(heading, Dock.Top);
            content.Children.Add(heading);
            content.Children.Add(new ScrollViewer {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = Math.Max(40, Math.Min(240, ActualHeight - 53)),
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.WhiteSmoke,
                    FontFamily = document.FontFamily, FontSize = Math.Max(14, Math.Min(20, document.FontSize)), Margin = new Thickness(0, 4, 0, 0) }
            });
            var card = new Border { Background = new SolidColorBrush(Color.FromRgb(42, 43, 46)), BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 5, 8, 8),
                MaxWidth = Math.Max(80, Math.Min(360, ActualWidth - 12)), MinWidth = Math.Min(140, Math.Max(80, ActualWidth - 12)),
                Child = content };
            card.PreviewKeyDown += delegate(object s, KeyEventArgs e) {
                if (e.Key == Key.Escape) { CloseNote(); e.Handled = true; }
            };
            AutomationProperties.SetAutomationId(card, "Footnote");
            notePopup.Child = card;
            card.Measure(new Size(card.MaxWidth, Math.Max(90, ActualHeight)));
            notePopup.HorizontalOffset = Math.Max(4, Math.Min(point.X - 8, ActualWidth - card.DesiredSize.Width - 4));
            double below = point.Y + 12;
            notePopup.VerticalOffset = below + card.DesiredSize.Height <= ActualHeight ? below :
                Math.Max(4, point.Y - card.DesiredSize.Height - 6);
            notePopup.IsOpen = true;
            close.Focus();
        }

        private void CloseNote() { notePopup.IsOpen = false; }

        internal int? Anchor()
        {
            if (pending) return pendingAnchor;
            if (Paged) return pageAnchor;
            if (scroll.VerticalOffset <= 0) return 0;
            TextPointer first = scroll.GetPositionFromPoint(new Point(PaperMargin.Left + 1, 1), true);
            return first == null ? (int?)null : StableOffset(first);
        }

        internal double Fraction()
        {
            if (pending) return pendingFraction;
            if (Paged) {
                int length = StableOffset(document.ContentEnd);
                return length == 0 ? 0 : (double)pageAnchor / length;
            }
            return ScrollableHeight <= 0 ? 0 : scroll.VerticalOffset / ScrollableHeight;
        }

        private void Restore(int? anchor, double fraction)
        {
            pendingAnchor = anchor;
            pendingFraction = Math.Max(0, Math.Min(1, fraction));
            pending = true;
            ScheduleRestore();
        }

        private void ScheduleRestore()
        {
            if (scheduled == null) {
                scheduled = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate {
                    scheduled = null;
                    RestoreNow();
                }));
            }
        }

        private void RestoreNow()
        {
            if (!pending || ActualWidth < 1 || ActualHeight < 1) return;
            TextPointer position = null;
            if (pendingAnchor.HasValue) position = AtStableOffset(pendingAnchor.Value);
            if (Paged) {
                paginator.PageSize = new Size(ActualWidth, ActualHeight);
                int number = 0;
                if (position != null) number = paginator.GetPageNumber(position);
                else if (pendingFraction > 0) {
                    paginator.ComputePageCount();
                    number = (int)Math.Round(pendingFraction * (paginator.PageCount - 1));
                }
                ShowPage(Math.Max(0, number));
            } else {
                scroll.UpdateLayout();
                double offset = pendingFraction * ScrollableHeight;
                if (position != null) {
                    Rect rect = position.GetCharacterRect(LogicalDirection.Forward);
                    if (!rect.IsEmpty) offset = pendingAnchor == 0 ? 0 : scroll.VerticalOffset + rect.Top;
                }
                scroll.ScrollToVerticalOffset(offset);
                scroll.UpdateLayout();
            }
            pending = false;
            reflowAnchor = position == null ? Anchor() : StableOffset(position);
            NotifyPosition();
        }

        private bool ShowPage(int number)
        {
            if (number < 0) return false;
            DocumentPage formatted = paginator.GetPage(number);
            if (formatted == DocumentPage.Missing) return false;
            var first = paginator.GetPagePosition(formatted) as TextPointer;
            pageAnchor = number == 0 || first == null ? 0 : StableOffset(first);
            page.PageNumber = number;
            return true;
        }

        internal bool Turn(int direction)
        {
            CloseNote();
            RestoreNow();
            if (Paged) {
                if (!ShowPage(PageNumber + direction)) return false;
                reflowAnchor = pageAnchor;
                NotifyPosition();
                return true;
            }
            if (AtScrollEdge(direction)) return false;
            double distance = Math.Max(1, scroll.ViewportHeight - body.LineHeight);
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset + direction * distance);
            return true;
        }

        internal bool AtScrollEdge(int direction)
        {
            RestoreNow();
            return direction > 0 ? scroll.VerticalOffset >= ScrollableHeight - 1 : scroll.VerticalOffset <= 1;
        }

        internal void ScrollLine(int direction)
        {
            if (direction > 0) scroll.LineDown();
            else scroll.LineUp();
        }

        internal void ChapterEdge(bool end)
        {
            Restore(null, end ? 1 : 0);
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!Paged && !pending && e.VerticalChange != 0 &&
                e.ViewportWidthChange == 0 && e.ViewportHeightChange == 0 && e.ExtentHeightChange == 0) {
                reflowAnchor = Anchor();
                NotifyPosition();
            }
        }

        private void NotifyPosition()
        {
            if (PositionChanged != null) PositionChanged();
        }
    }
}
