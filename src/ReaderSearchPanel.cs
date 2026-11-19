using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FloatLeaf
{
    internal static partial class ReaderPanels
    {
        internal static FrameworkElement Search(ReaderSearch search, bool showFuture, Action<bool> setShowFuture,
            Action<int, int, int, int> navigate, Action returnToOrigin, Action close)
        {
            return new SearchView(search, showFuture, setShowFuture, navigate, returnToOrigin, close).Root;
        }

        private sealed class SearchView
        {
            private const int PageSize = 32;
            private readonly ReaderSearch search;
            private readonly Action<int, int, int, int> navigate;
            internal readonly DockPanel Root;
            private readonly TextBox input;
            private readonly StackPanel rows = new StackPanel();
            private readonly StackPanel spoiler = new StackPanel();
            private readonly ScrollViewer scroller;
            private readonly TextBlock status;
            private readonly List<ReaderSearch.Hit> hits = new List<ReaderSearch.Hit>();
            private readonly List<Button> resultButtons = new List<Button>();
            private Button more;
            private int confirmed = -1;
            private bool showFuture, complete;
            private CancellationTokenSource pending;

            internal SearchView(ReaderSearch search, bool showFuture, Action<bool> setShowFuture,
                Action<int, int, int, int> navigate, Action returnToOrigin, Action close)
            {
                this.search = search;
                this.showFuture = showFuture;
                this.navigate = navigate;
                Root = Page(ReaderText.Get("全文搜索", "Search book"), close);
                var controls = new StackPanel { Margin = new Thickness(17, 0, 13, 1) };
                DockPanel.SetDock(controls, Dock.Top);
                Root.Children.Add(controls);

                var searchRow = new DockPanel();
                var back = ActionButton(Root, ReaderText.Get("返回原段", "Return to passage"), "SearchReturn");
                back.Margin = new Thickness(6, 0, 0, 0);
                back.Click += delegate { returnToOrigin(); };
                DockPanel.SetDock(back, Dock.Right);
                searchRow.Children.Add(back);
                input = new TextBox {
                    FontSize = 13, Padding = new Thickness(6, 2, 4, 2), Text = search.Query,
                    VerticalContentAlignment = VerticalAlignment.Center, Background = ReaderStyle.Brush("#2A2B2D", 1),
                    CaretBrush = Brushes.White
                };
                input.SetResourceReference(Control.ForegroundProperty, "PanelInk");
                input.SetResourceReference(Control.BorderBrushProperty, "PanelTrack");
                AutomationProperties.SetName(input, ReaderText.Get("搜索词", "Search text"));
                AutomationProperties.SetAutomationId(input, "SearchQuery");
                searchRow.Children.Add(input);
                controls.Children.Add(searchRow);
                var directions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                controls.Children.Add(directions);
                AddChoice(directions, ReaderText.Get("向前搜索", "Search earlier"), "SearchEarlier", !search.Later, delegate { SetDirection(false); });
                AddChoice(directions, ReaderText.Get("向后搜索", "Search later"), "SearchLater", search.Later, delegate { SetDirection(true); });
                AddSwitch(spoiler, ReaderText.Get("默认显示后文", "Show later excerpts by default"), showFuture, delegate(bool show) {
                    this.showFuture = show;
                    setShowFuture(show);
                    confirmed = -1;
                    for (int i = 0; i < hits.Count; i++) PaintResult(i);
                });
                controls.Children.Add(spoiler);
                spoiler.Visibility = search.Later ? Visibility.Visible : Visibility.Collapsed;
                status = Label("", 12, "PanelMuted");
                status.Margin = new Thickness(2, 4, 0, 5);
                status.TextWrapping = TextWrapping.Wrap;
                controls.Children.Add(status);

                scroller = new ScrollViewer {
                    Content = rows, Margin = new Thickness(15, 0, 9, 5),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                scroller.SetResourceReference(FrameworkElement.StyleProperty, "PanelScroll");
                Root.Children.Add(scroller);
                input.TextChanged += delegate { search.Query = input.Text; Restart(175); };
                input.KeyDown += delegate(object sender, KeyEventArgs e) {
                    if (e.Key == Key.Enter) { e.Handled = true; Restart(0); }
                };
                Root.Loaded += delegate {
                    input.Dispatcher.BeginInvoke(DispatcherPriority.Input, (Action)delegate {
                        if (input.IsVisible) { input.Focus(); input.SelectAll(); }
                    });
                    if (!complete) Restart(0);
                };
                Root.Unloaded += delegate { Cancel(); Disarm(); };
            }

            private void SetDirection(bool later)
            {
                if (search.Later == later) return;
                search.Later = later;
                spoiler.Visibility = later ? Visibility.Visible : Visibility.Collapsed;
                Restart(0);
            }

            private void Cancel()
            {
                if (pending != null) { pending.Cancel(); pending = null; }
            }

            private void Restart(int delay)
            {
                Cancel();
                confirmed = -1;
                complete = false;
                hits.Clear();
                rows.Children.Clear();
                resultButtons.Clear();
                more = null;
                scroller.ScrollToTop();
                string term = search.Query.Trim();
                if (term.Length == 0) {
                    status.Text = ReaderText.Get("输入要查找的文字", "Enter text to search for");
                    return;
                }
                Fetch(term, delay);
            }

            private async void Fetch(string term, int delay)
            {
                complete = false;
                var request = new CancellationTokenSource();
                pending = request;
                var token = request.Token;
                var after = hits.Count == 0 ? null : hits[hits.Count - 1];
                bool later = search.Later;
                if (more != null) more.IsEnabled = false;
                status.Text = ReaderText.Get("正在搜索…", "Searching…");
                try {
                    if (delay > 0) await Task.Delay(delay, token);
                    var found = await Task.Run(() => search.Find(term, later, after, PageSize + 1, token), token);
                    if (token.IsCancellationRequested) return;
                    if (more != null) { rows.Children.Remove(more); more = null; }
                    bool hasMore = found.Count > PageSize;
                    int count = Math.Min(PageSize, found.Count);
                    for (int i = 0; i < count; i++) AddResult(found[i]);
                    if (hasMore) {
                        more = ActionButton(Root, ReaderText.Get("更多结果", "More results"), "SearchMore");
                        more.Click += delegate { Fetch(term, 0); };
                        rows.Children.Add(more);
                    }
                    if (hits.Count == 0) status.Text = later
                        ? ReaderText.Get("原位置之后未找到匹配", "No matches after your original position")
                        : ReaderText.Get("原位置之前未找到匹配", "No matches before your original position");
                    else status.Text = String.Format(ReaderText.Get("匹配 {0} 处", "{0} matches"), hits.Count + (hasMore ? "+" : ""));
                    complete = true;
                } catch (OperationCanceledException) {
                    // A new query, another direction, or closing the panel superseded this request.
                } finally {
                    if (pending == request) pending = null;
                    request.Dispose();
                }
            }

            private void AddResult(ReaderSearch.Hit hit)
            {
                int index = hits.Count;
                hits.Add(hit);
                var button = ActionButton(Root, "", "SearchHit" + index);
                button.Margin = new Thickness(0, 0, 2, 3);
                button.Content = new TextBlock { TextWrapping = TextWrapping.Wrap };
                button.Click += delegate {
                    if (search.Later && !showFuture && confirmed != index) {
                        Disarm();
                        confirmed = index;
                        PaintResult(index);
                        return;
                    }
                    Disarm();
                    navigate(hit.Chapter, hit.Paragraph, hit.Offset, hit.Length);
                };
                rows.Children.Add(button);
                resultButtons.Add(button);
                PaintResult(index);
            }

            private void Disarm()
            {
                if (confirmed < 0) return;
                int previous = confirmed;
                confirmed = -1;
                PaintResult(previous);
            }

            private void PaintResult(int index)
            {
                string label;
                if (search.Later && !showFuture) {
                    label = String.Format(ReaderText.Get("后文结果 {0}", "Later match {0}"), index + 1) + "  ·  " +
                        (confirmed == index ? ReaderText.Get("再次点击查看", "Click again to open") : ReaderText.Get("点击确认查看", "Click to confirm"));
                } else label = search.Describe(hits[index], search.Later);
                var button = resultButtons[index];
                ((TextBlock)button.Content).Text = label;
                AutomationProperties.SetName(button, label);
            }
        }
    }
}
