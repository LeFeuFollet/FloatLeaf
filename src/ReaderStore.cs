using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace FloatLeaf
{
    internal sealed class ReaderStore
    {
        private const int MaxBooks = 100;
        private readonly string settingsPath;
        private readonly List<Bookmark> books = new List<Bookmark>();
        private readonly HashSet<string> changedBooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public double BackOpacity { get; set; }
        public Color PaperColor { get; set; }
        public Color InkColor { get; set; }
        public bool English { get; set; }
        public bool ShowFutureMatches { get; set; }
        public double TextOpacity { get; set; }
        public double FontSize { get; set; }
        public string ChineseFontId { get; set; }
        public string EnglishFontId { get; set; }
        public bool Paged { get; set; }
        public bool Pinned { get; set; }
        public bool HoverHide { get; set; }
        public double WindowLeft { get; set; }
        public double WindowTop { get; set; }
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }

        private sealed class Bookmark
        {
            public string Path;
            public int Chapter;
            public double Fraction;
            public int? Anchor;
        }

        private ReaderStore(string path)
        {
            settingsPath = path;
            BackOpacity = .18;
            PaperColor = Color.FromRgb(28, 28, 28);
            InkColor = Color.FromRgb(240, 240, 240);
            TextOpacity = .8;
            FontSize = 19;
            ChineseFontId = ReaderFonts.DefaultChineseId;
            EnglishFontId = ReaderFonts.DefaultEnglishId;
            Pinned = true;
            WindowLeft = double.NaN;
            WindowTop = double.NaN;
            WindowWidth = 360;
            WindowHeight = 496;
        }

        public static ReaderStore Load()
        {
            string directory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(directory)) return new ReaderStore(null);
            return ReadFile(Path.Combine(directory, "FloatLeaf", "settings.xml"));
        }

        private static ReaderStore ReadFile(string path)
        {
            ReaderStore store = new ReaderStore(path);
            try
            {
                if (!File.Exists(path)) return store;
                XmlReaderSettings options = new XmlReaderSettings();
                options.XmlResolver = null;
                options.DtdProcessing = DtdProcessing.Prohibit;
                options.MaxCharactersInDocument = 4 * 1024 * 1024;
                XElement root;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (XmlReader reader = XmlReader.Create(stream, options)) root = XElement.Load(reader);
                if (root.Name != "reader") return store;

                store.BackOpacity = Number(root, "background", store.BackOpacity, 0, 1);
                store.PaperColor = ReadColor(root, "paperColor", store.PaperColor);
                store.InkColor = ReadColor(root, "inkColor", store.InkColor);
                store.English = (string)root.Attribute("language") == "en";
                store.ShowFutureMatches = Flag(root, "showFutureMatches", false);
                store.TextOpacity = Number(root, "text", store.TextOpacity, 0, 1);
                store.FontSize = Number(root, "fontSize", store.FontSize, 6, 200);
                string oldFont = (string)root.Attribute("font");
                store.ChineseFontId = (string)root.Attribute("chineseFont") ??
                    (oldFont == "noto-sans-sc" || oldFont == "noto-serif-sc" ? oldFont : store.ChineseFontId);
                store.EnglishFontId = (string)root.Attribute("englishFont") ??
                    (oldFont == "source-serif-4" ? oldFont : store.EnglishFontId);
                store.Paged = Flag(root, "paged", store.Paged);
                store.Pinned = Flag(root, "pinned", store.Pinned);
                store.HoverHide = Flag(root, "hoverHide", store.HoverHide);
                store.WindowLeft = Number(root, "left", store.WindowLeft, -1000000, 1000000);
                store.WindowTop = Number(root, "top", store.WindowTop, -1000000, 1000000);
                store.WindowWidth = Number(root, "width", store.WindowWidth, 1, 100000);
                store.WindowHeight = Number(root, "height", store.WindowHeight, 1, 100000);

                foreach (XElement book in root.Elements("book").Take(MaxBooks))
                {
                    string key = PathKey((string)book.Attribute("path"));
                    if (key == null || store.Find(key) != null) continue;
                    int chapter;
                    int.TryParse((string)book.Attribute("chapter"), NumberStyles.Integer, CultureInfo.InvariantCulture, out chapter);
                    int anchor;
                    bool hasAnchor = int.TryParse((string)book.Attribute("anchor"), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out anchor) && anchor >= 0;
                    store.books.Add(new Bookmark {
                        Path = key, Chapter = Math.Max(0, chapter), Fraction = Number(book, "fraction", 0, 0, 1),
                        Anchor = hasAnchor ? (int?)anchor : null
                    });
                }
            }
            catch (Exception error)
            {
                if (!IsStorageError(error)) throw;
            }
            return store;
        }

        private static bool IsStorageError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is SecurityException || error is XmlException;
        }

        private static double Number(XElement node, string name, double fallback, double minimum, double maximum)
        {
            double value;
            if (!double.TryParse((string)node.Attribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return fallback;
            return value >= minimum && value <= maximum ? value : fallback;
        }

        private static bool Flag(XElement node, string name, bool fallback)
        {
            bool value;
            return bool.TryParse((string)node.Attribute(name), out value) ? value : fallback;
        }

        private static Color ReadColor(XElement node, string name, Color fallback)
        {
            string hex = (string)node.Attribute(name);
            int rgb;
            if (hex == null || hex.Length != 7 || hex[0] != '#' ||
                !int.TryParse(hex.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                return fallback;
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        private static string Hex(Color color)
        {
            return String.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        private static string PathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return XmlConvert.VerifyXmlChars(Path.GetFullPath(path)); }
            catch (Exception error)
            {
                if (!IsStorageError(error) && !(error is ArgumentException) && !(error is NotSupportedException)) throw;
                return null;
            }
        }

        private Bookmark Find(string path)
        {
            return books.Find(book => string.Equals(book.Path, path, StringComparison.OrdinalIgnoreCase));
        }

        private void Remember(Bookmark book)
        {
            books.Remove(Find(book.Path));
            books.Insert(0, book);
            if (books.Count > MaxBooks) books.RemoveAt(MaxBooks);
        }

        public void SetPosition(string path, int chapter, double fraction, int? anchor)
        {
            string key = PathKey(path);
            if (key == null) return;
            double offset = double.IsNaN(fraction) ? 0 : Math.Max(0, Math.Min(1, fraction));
            Remember(new Bookmark { Path = key, Chapter = Math.Max(0, chapter), Fraction = offset,
                Anchor = anchor >= 0 ? anchor : null });
            changedBooks.Add(key);
            if (changedBooks.Count > MaxBooks) changedBooks.IntersectWith(books.Select(book => book.Path));
        }

        public void GetPosition(string path, out int chapter, out double fraction, out int? anchor)
        {
            Bookmark book = Find(PathKey(path));
            chapter = book == null ? 0 : book.Chapter;
            fraction = book == null ? 0 : book.Fraction;
            anchor = book == null ? null : book.Anchor;
        }

        public bool HasPosition(string path)
        {
            return Find(PathKey(path)) != null;
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(settingsPath)) return;
            try
            {
                using (Mutex mutex = new Mutex(false, @"Local\FloatLeaf.ReaderSettings"))
                {
                    try { if (!mutex.WaitOne(400)) return; }
                    catch (AbandonedMutexException) { /* The previous writer stopped while holding the mutex. */ }
                    try { SaveSettings(); }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                if (!IsStorageError(error)) throw;
            }
        }

        private void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            ReaderStore merged = ReadFile(settingsPath);
            for (int index = books.Count - 1; index >= 0; index--)
            {
                if (changedBooks.Contains(books[index].Path)) merged.Remember(books[index]);
            }
            XElement root = new XElement("reader", new XAttribute("background", BackOpacity), new XAttribute("text", TextOpacity),
                new XAttribute("paperColor", Hex(PaperColor)), new XAttribute("inkColor", Hex(InkColor)),
                new XAttribute("language", English ? "en" : "zh"), new XAttribute("fontSize", FontSize),
                new XAttribute("chineseFont", ChineseFontId), new XAttribute("englishFont", EnglishFontId), new XAttribute("paged", Paged),
                new XAttribute("pinned", Pinned), new XAttribute("showFutureMatches", ShowFutureMatches),
                new XAttribute("hoverHide", HoverHide), new XAttribute("width", WindowWidth), new XAttribute("height", WindowHeight));
            if (!double.IsNaN(WindowLeft)) root.SetAttributeValue("left", WindowLeft);
            if (!double.IsNaN(WindowTop)) root.SetAttributeValue("top", WindowTop);
            foreach (Bookmark book in merged.books)
            {
                var entry = new XElement("book", new XAttribute("path", book.Path), new XAttribute("chapter", book.Chapter),
                    new XAttribute("fraction", book.Fraction));
                if (book.Anchor.HasValue) entry.SetAttributeValue("anchor", book.Anchor.Value);
                root.Add(entry);
            }

            string temp = settingsPath + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                root.Save(stream);
                stream.Flush(true);
            }
            if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, null);
            else File.Move(temp, settingsPath);

            books.Clear();
            books.AddRange(merged.books);
            changedBooks.Clear();
        }
    }
}
