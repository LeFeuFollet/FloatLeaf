using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace FloatLeaf
{
    internal sealed class WelcomeView : Grid
    {
        private readonly TextBlock heading = ReaderStyle.Label("", 19, FontWeights.SemiBold);
        private readonly TextBlock hint = ReaderStyle.Label("", 13, FontWeights.Normal);
        private readonly Button openFile, openSample;
        private readonly StackPanel titles = new StackPanel();
        private readonly Action<string> openBook;
        private readonly Style titleStyle;
        private int generation;
        private bool loading;
        private string loadError;

        internal WelcomeView(Action open, Action sample, Action<string> openShelfBook)
        {
            openBook = openShelfBook;
            Margin = new Thickness(17, 4, 15, 11);
            Background = Brushes.Transparent;
            AutomationProperties.SetAutomationId(this, "WelcomeView");
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            heading.Margin = new Thickness(3, 1, 0, 5);
            Children.Add(heading);

            titleStyle = (Style)XamlReader.Parse(TitleStyle);
            var list = new ScrollViewer {
                Content = titles, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false
            };
            AutomationProperties.SetAutomationId(list, "ShelfBooks");
            SetRow(list, 1);
            Children.Add(list);

            hint.Margin = new Thickness(3, 2, 2, 2);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.IsHitTestVisible = false;
            SetRow(hint, 1);
            Children.Add(hint);
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            openFile = ReaderStyle.Tool("WelcomeOpenButton", "open", "", "", open);
            openSample = ReaderStyle.Tool("SampleButton", "book", "", "", sample);
            openFile.Margin = new Thickness(0, 0, 7, 0);
            actions.Children.Add(openFile);
            actions.Children.Add(openSample);
            SetRow(actions, 2);
            Children.Add(actions);
            Loaded += delegate { if (generation == 0) RefreshBooks(); };
        }

        internal async void RefreshBooks()
        {
            int request = ++generation;
            loading = true;
            loadError = null;
            UpdateHint();
            List<Bookshelf.Entry> books = null;
            string error = null;
            try { books = await Task.Run(() => Bookshelf.ListBooks()); }
            catch (IOException ex) { error = ex.Message; }
            catch (UnauthorizedAccessException ex) { error = ex.Message; }

            if (request != generation) return;
            loading = false;
            loadError = error;
            titles.Children.Clear();
            if (books != null) ShowTitles(books);
            UpdateHint();
        }

        private void ShowTitles(List<Bookshelf.Entry> books)
        {
            var repeatedTitles = new HashSet<string>(books.GroupBy(b => b.Title, StringComparer.CurrentCultureIgnoreCase)
                .Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.CurrentCultureIgnoreCase);
            foreach (var book in books) {
                string label = repeatedTitles.Contains(book.Title) ? book.Title + "  ·  " + Path.GetFileName(book.Path) : book.Title;
                var text = ReaderStyle.Label(label, 14, FontWeights.Normal);
                var button = new Button { Content = text, Style = titleStyle, ToolTip = book.Title + "\n" + Path.GetFileName(book.Path) };
                button.SetResourceReference(Control.ForegroundProperty, "ShelfInk");
                AutomationProperties.SetName(button, label);
                button.Click += delegate { openBook(book.Path); };
                titles.Children.Add(button);
            }
        }

        internal void Apply(double textOpacity, Color ink)
        {
            heading.Text = ReaderText.Get("书架", "Bookshelf");
            SetAction(openFile, ReaderText.Get("导入 EPUB", "Import EPUB"));
            SetAction(openSample, ReaderText.Get("内置试读", "Read sample"));
            Resources["ShelfInk"] = ReaderStyle.Brush(ink, 1);
            Resources["ShelfHover"] = ReaderStyle.Brush(ink, .12);
            heading.Foreground = (Brush)Resources["ShelfInk"];
            hint.Foreground = openFile.Foreground = openSample.Foreground = ReaderStyle.Brush(ink, .82);
            Opacity = textOpacity;
            UpdateHint();
        }

        private void UpdateHint()
        {
            hint.Visibility = titles.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            hint.Text = loadError != null ? ReaderText.Get("无法读取书架：", "Could not read the bookshelf: ") + loadError :
                loading ? ReaderText.Get("正在读取书架…", "Loading bookshelf…") :
                ReaderText.Get("拖入 EPUB，或点击下方按钮导入第一本书。", "Drop an EPUB here or import your first book below.");
        }

        private static void SetAction(Button button, string label)
        {
            var row = (StackPanel)button.Content;
            ((TextBlock)row.Children[1]).Text = label;
            button.ToolTip = label;
            AutomationProperties.SetName(button, label);
        }

        private const string TitleStyle = @"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value>
    <ControlTemplate TargetType='Button'>
      <Border x:Name='surface' Padding='6,6' CornerRadius='4' Background='{TemplateBinding Background}'>
        <ContentPresenter VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='{DynamicResource ShelfHover}'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Background' Value='{DynamicResource ShelfHover}'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate>
  </Setter.Value></Setter>
</Style>";
    }
}
