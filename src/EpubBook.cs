using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace FloatLeaf
{
    public sealed class EpubNote
    {
        public int Start { get; internal set; }
        public int Length { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class EpubChapter
    {
        private readonly string title;
        private readonly int number;
        public string Title
        {
            get { return title ?? String.Format(CultureInfo.InvariantCulture, ReaderText.Get("第 {0} 节", "Section {0}"), number); }
        }
        public List<string> Paragraphs { get; private set; }
        private readonly Dictionary<int, List<EpubNote>> notes = new Dictionary<int, List<EpubNote>>();

        public IEnumerable<EpubNote> NotesFor(int paragraphIndex)
        {
            List<EpubNote> found;
            return notes.TryGetValue(paragraphIndex, out found)
                ? found.Where(n => !String.IsNullOrEmpty(n.Text)) : Enumerable.Empty<EpubNote>();
        }

        internal void SetNotes(int paragraphIndex, List<EpubNote> value) { notes[paragraphIndex] = value; }

        public EpubChapter(string title, List<string> paragraphs)
        {
            this.title = title;
            Paragraphs = paragraphs;
        }

        internal EpubChapter(int number, List<string> paragraphs) : this(null, paragraphs)
        {
            this.number = number;
        }
    }

    public sealed partial class EpubBook
    {
        public string Title { get; private set; }
        public List<EpubChapter> Chapters { get; private set; }

        internal EpubBook(string title, List<EpubChapter> chapters)
        {
            Title = title;
            Chapters = chapters;
        }

        public static EpubBook Load(string path)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                return new Package(archive).Read(Path.GetFileNameWithoutExtension(path));
            }
        }

        internal static string ReadTitle(string path)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                var package = new Package(archive);
                string packagePath = package.PackagePath();
                return Package.TitleOf(package.Xml(packagePath), Path.GetFileNameWithoutExtension(path));
            }
        }

        private const long EntryLimit = 24L * 1024 * 1024;
        private const long BookLimit = 96L * 1024 * 1024;
        private static readonly Regex Spaces = new Regex(@"[\t\r\n ]+", RegexOptions.Compiled);
        private static readonly Regex NamedEntity = new Regex(@"&([A-Za-z][A-Za-z0-9]+);", RegexOptions.Compiled);
        private static readonly Regex DeclaredEncoding = new Regex(@"<\?xml\s+[^>]*?encoding\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> Blocks = Words("address article aside blockquote br caption dd div dl dt figcaption figure footer h1 h2 h3 h4 h5 h6 header hr li main nav ol p pre section table tbody tr ul");
        private static readonly HashSet<string> Omitted = Words("audio canvas head iframe math object rp rt script style svg template video");

        private static HashSet<string> Words(string value)
        {
            return new HashSet<string>(value.Split(' '), StringComparer.OrdinalIgnoreCase);
        }

        private static bool Named(XElement element, string name)
        {
            return element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Attr(XElement element, string name)
        {
            return element == null ? null : (string)element.Attribute(name);
        }

        private static string Clean(string value)
        {
            return String.IsNullOrEmpty(value) ? "" : Spaces.Replace(value, " ").Trim();
        }

        private static bool HasToken(string value, string token)
        {
            return (value ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Contains(token);
        }

        // All references resolve to ZIP members. Nothing from a book is extracted or fetched.
        private static string Resolve(string referringFile, string href)
        {
            if (String.IsNullOrWhiteSpace(href)) return null;
            string raw = href.Trim().Split('#', '?')[0];
            if (raw.Length == 0) return referringFile;
            string name = Uri.UnescapeDataString(raw).Replace('\\', '/');
            if (name.StartsWith("//", StringComparison.Ordinal) || name.IndexOf(':') >= 0) return null;
            int slash = referringFile.LastIndexOf('/');
            string prefix = slash >= 0 ? referringFile.Substring(0, slash + 1) : "";
            string combined = name.StartsWith("/", StringComparison.Ordinal) ? name : prefix + name;
            var components = new List<string>();
            foreach (string part in combined.Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..")
                {
                    if (components.Count == 0) return null;
                    components.RemoveAt(components.Count - 1);
                }
                else components.Add(part);
            }
            return String.Join("/", components);
        }

        private sealed class NavPoint
        {
            internal string Anchor;
            internal string Title;
        }

        private sealed partial class Package
        {
            private readonly ZipArchive archive;
            private readonly Dictionary<string, List<NavPoint>> navigation = new Dictionary<string, List<NavPoint>>(StringComparer.Ordinal);
            private readonly Dictionary<string, List<NoteReference>> pendingNotes = new Dictionary<string, List<NoteReference>>(StringComparer.Ordinal);
            private readonly HashSet<string> counted = new HashSet<string>(StringComparer.Ordinal);
            private long bytesRead;

            internal Package(ZipArchive archive) { this.archive = archive; }

            internal string PackagePath()
            {
                XDocument container = Xml("META-INF/container.xml");
                var roots = container.Descendants().Where(e => Named(e, "rootfile")).ToList();
                XElement root = roots.FirstOrDefault(e => Attr(e, "media-type") == "application/oebps-package+xml") ?? roots.FirstOrDefault();
                string packagePath = Resolve("", Attr(root, "full-path"));
                if (String.IsNullOrEmpty(packagePath))
                    throw new InvalidDataException(ReaderText.Get("这本 EPUB 缺少书籍描述文件。", "The EPUB package document is missing."));
                return packagePath;
            }

            internal static string TitleOf(XDocument opf, string fallbackTitle)
            {
                XElement metadata = Child(opf.Root, "metadata");
                XElement bookTitle = metadata == null ? null : metadata.Elements().FirstOrDefault(e => Named(e, "title"));
                string title = Clean(bookTitle == null ? "" : bookTitle.Value);
                return title.Length == 0 ? fallbackTitle : title;
            }

            internal EpubBook Read(string fallbackTitle)
            {
                string packagePath = PackagePath();
                XDocument opf = Xml(packagePath);
                var book = new EpubBook(TitleOf(opf, fallbackTitle), new List<EpubChapter>());
                XElement manifest = Child(opf.Root, "manifest");
                XElement spine = Child(opf.Root, "spine");
                if (manifest == null || spine == null)
                    throw new InvalidDataException(ReaderText.Get("这本 EPUB 缺少章节清单或阅读顺序。", "The EPUB has no chapter list or reading order."));
                var items = new Dictionary<string, XElement>(StringComparer.Ordinal);
                foreach (XElement item in manifest.Elements().Where(e => Named(e, "item")))
                {
                    string id = Attr(item, "id");
                    if (!String.IsNullOrEmpty(id)) items[id] = item;
                }
                XElement nav = items.Values.FirstOrDefault(e => HasToken(Attr(e, "properties"), "nav"));
                XElement ncx;
                if (!items.TryGetValue(Attr(spine, "toc") ?? "", out ncx))
                    ncx = items.Values.FirstOrDefault(e => Attr(e, "media-type") == "application/x-dtbncx+xml");
                Navigation(packagePath, nav, false);
                Navigation(packagePath, ncx, true);
                HashSet<string> encrypted = EncryptedMembers();
                foreach (XElement reference in spine.Elements().Where(e => Named(e, "itemref")))
                {
                    XElement item;
                    if (!items.TryGetValue(Attr(reference, "idref") ?? "", out item)) continue;
                    // EPUB foreign content can provide an HTML fallback in the manifest.
                    var tried = new HashSet<XElement>();
                    while (item != null && !IsText(item) && tried.Add(item))
                        items.TryGetValue(Attr(item, "fallback") ?? "", out item);
                    if (item == null || !IsText(item) || item == nav) continue;
                    string chapterPath = Resolve(packagePath, Attr(item, "href"));
                    if (String.IsNullOrEmpty(chapterPath)) continue;
                    if (encrypted.Contains(chapterPath))
                        throw new InvalidDataException(ReaderText.Get("这本 EPUB 的正文已加密，无法读取。", "The text in this EPUB is encrypted."));
                    XDocument content = Xml(chapterPath);
                    AppendChapter(book, content, chapterPath);
                }
                if (book.Chapters.Count == 0)
                    throw new InvalidDataException(ReaderText.Get("这本 EPUB 没有可显示的文字章节。纯图片书暂不支持。", "No text chapters were found. Image-only books are unsupported."));
                var textMembers = new HashSet<string>(items.Values.Where(IsText).Select(e => Resolve(packagePath, Attr(e, "href"))), StringComparer.Ordinal);
                foreach (string path in pendingNotes.Keys.Where(textMembers.Contains).ToList())
                {
                    if (encrypted.Contains(path) || archive.GetEntry(path) == null) continue;
                    try { ResolveNotes(path, Xml(path)); }
                    catch (InvalidDataException) { } // An unreadable optional endnote should not prevent reading the book.
                }
                return book;
            }

            private static XElement Child(XElement parent, string name)
            {
                return parent == null ? null : parent.Elements().FirstOrDefault(e => Named(e, name));
            }

            private static bool IsText(XElement item)
            {
                string media = Attr(item, "media-type") ?? "";
                if (media == "application/xhtml+xml" || media == "text/html" || media == "text/x-oeb1-document") return true;
                string name = (Attr(item, "href") ?? "").Split('#', '?')[0];
                return name.EndsWith(".xhtml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
            }

            private HashSet<string> EncryptedMembers()
            {
                var result = new HashSet<string>(StringComparer.Ordinal);
                if (archive.GetEntry("META-INF/encryption.xml") == null) return result;
                XDocument encryption = Xml("META-INF/encryption.xml");
                foreach (XElement cipher in encryption.Descendants().Where(e => Named(e, "CipherReference")))
                {
                    string path = Resolve("", Attr(cipher, "URI"));
                    if (path != null) result.Add(path);
                }
                return result;
            }

            private void Navigation(string packagePath, XElement item, bool legacy)
            {
                string navPath = Resolve(packagePath, Attr(item, "href"));
                if (navPath == null || archive.GetEntry(navPath) == null) return;
                XDocument document;
                try { document = Xml(navPath); }
                catch (InvalidDataException) { return; } // Chapter headings provide a usable fallback.
                if (legacy)
                {
                    foreach (XElement point in document.Descendants().Where(e => Named(e, "navPoint")))
                    {
                        XElement label = Child(point, "navLabel");
                        AddPoint(navPath, Attr(Child(point, "content"), "src"), label == null ? "" : label.Value);
                    }
                    return;
                }
                XElement toc = document.Descendants().FirstOrDefault(e => Named(e, "nav") &&
                    (e.Attributes().Any(a => a.Name.LocalName == "type" && HasToken(a.Value, "toc")) || HasToken(Attr(e, "role"), "doc-toc")));
                if (toc == null) return;
                foreach (XElement link in toc.Descendants().Where(e => Named(e, "a")))
                    AddPoint(navPath, Attr(link, "href"), link.Value);
            }

            private void AddPoint(string referringFile, string href, string label)
            {
                string path = Resolve(referringFile, href);
                label = Clean(label);
                if (path == null || label.Length == 0) return;
                int hash = href.IndexOf('#');
                string anchor = hash < 0 ? "" : Uri.UnescapeDataString(href.Substring(hash + 1));
                List<NavPoint> points;
                if (!navigation.TryGetValue(path, out points)) navigation[path] = points = new List<NavPoint>();
                points.Add(new NavPoint { Anchor = anchor, Title = label });
            }

            private void AppendChapter(EpubBook book, XDocument document, string path)
            {
                XElement body = document.Descendants().FirstOrDefault(e => Named(e, "body")) ?? document.Root;
                if (body == null) return;
                var paragraphs = new List<string>();
                var anchors = new Dictionary<string, int>(StringComparer.Ordinal);
                var notes = new Dictionary<int, List<EpubNote>>();
                var links = CollectReferences(body, path);
                ResolveNotes(path, document);
                Extract(body, paragraphs, anchors, links, notes);
                if (paragraphs.Count == 0) return;
                var starts = new SortedDictionary<int, string>();
                List<NavPoint> points;
                if (navigation.TryGetValue(path, out points))
                {
                    foreach (NavPoint point in points)
                    {
                        int offset = 0;
                        if (point.Anchor.Length != 0 && !anchors.TryGetValue(point.Anchor, out offset)) continue;
                        if (offset < paragraphs.Count && !starts.ContainsKey(offset)) starts.Add(offset, point.Title);
                    }
                }
                if (!starts.ContainsKey(0))
                {
                    XElement heading = body.Descendants().FirstOrDefault(e => Named(e, "h1") || Named(e, "h2") || Named(e, "h3"));
                    XElement title = document.Descendants().FirstOrDefault(e => Named(e, "title"));
                    string label = Clean(heading == null ? "" : heading.Value);
                    if (label.Length == 0) label = Clean(title == null ? "" : title.Value);
                    starts.Add(0, label);
                }
                var offsets = starts.Keys.ToList();
                for (int i = 0; i < offsets.Count; i++)
                {
                    int start = offsets[i];
                    int end = i + 1 < offsets.Count ? offsets[i + 1] : paragraphs.Count;
                    var content = paragraphs.GetRange(start, end - start);
                    var chapter = starts[start].Length == 0
                        ? new EpubChapter(book.Chapters.Count + 1, content)
                        : new EpubChapter(starts[start], content);
                    for (int p = start; p < end; p++) {
                        List<EpubNote> found;
                        if (notes.TryGetValue(p, out found)) chapter.SetNotes(p - start, found);
                    }
                    book.Chapters.Add(chapter);
                }
            }

            internal XDocument Xml(string path)
            {
                ZipArchiveEntry entry = archive.GetEntry(path);
                if (entry == null) throw new InvalidDataException(ReaderText.Get("EPUB 内缺少文件：", "Missing file in EPUB: ") + path);
                bool firstRead = !counted.Contains(path);
                if (entry.Length > EntryLimit || firstRead && bytesRead + entry.Length > BookLimit)
                    throw new InvalidDataException(ReaderText.Get("这本 EPUB 的文字数据过大，超出当前阅读器的载入范围。", "This EPUB exceeds the reader’s text size limit."));
                if (firstRead) { counted.Add(path); bytesRead += entry.Length; }
                byte[] data = new byte[(int)entry.Length];
                using (Stream stream = entry.Open())
                {
                    int position = 0;
                    while (position < data.Length)
                    {
                        int received = stream.Read(data, position, data.Length - position);
                        if (received == 0) throw new InvalidDataException(ReaderText.Get("EPUB 内的文件不完整：", "Incomplete file in EPUB: ") + path);
                        position += received;
                    }
                    if (stream.ReadByte() != -1) throw new InvalidDataException(ReaderText.Get("EPUB 内的文件大小异常：", "Unexpected file size in EPUB: ") + path);
                }
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = EntryLimit };
                try
                {
                    using (var input = new MemoryStream(data))
                    using (XmlReader reader = XmlReader.Create(input, settings))
                        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                }
                catch (XmlException)
                {
                    // EPUB 2 XHTML often references named HTML entities defined in its external DTD.
                    // Resolve known entities locally while leaving XML's built-in escapes untouched.
                    string text = Decode(data);
                    text = NamedEntity.Replace(text, NumericEntity);
                    try
                    {
                        using (var input = new StringReader(text))
                        using (XmlReader reader = XmlReader.Create(input, settings))
                            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
                    }
                    catch (XmlException ex) { throw new InvalidDataException(ReaderText.Get("EPUB 内的文档格式有误：", "Invalid document in EPUB: ") + path, ex); }
                }
            }

            private static string Decode(byte[] data)
            {
                string header = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 240));
                Match match = DeclaredEncoding.Match(header);
                Encoding encoding = Encoding.UTF8;
                if (match.Success)
                {
                    try { encoding = Encoding.GetEncoding(match.Groups[1].Value); }
                    catch (ArgumentException) { }
                }
                using (var input = new StreamReader(new MemoryStream(data), encoding, true))
                    return input.ReadToEnd();
            }

            private static string NumericEntity(Match match)
            {
                switch (match.Groups[1].Value)
                {
                    case "lt": case "gt": case "amp": case "apos": case "quot": return match.Value;
                }
                string value = WebUtility.HtmlDecode(match.Value);
                if (value == match.Value) return match.Value;
                var result = new StringBuilder();
                for (int i = 0; i < value.Length; i++)
                {
                    int code = Char.ConvertToUtf32(value, i);
                    result.Append("&#").Append(code.ToString(CultureInfo.InvariantCulture)).Append(';');
                    if (Char.IsHighSurrogate(value[i])) i++;
                }
                return result.ToString();
            }
        }

        private static void AppendText(StringBuilder line, string text)
        {
            string value = Spaces.Replace(text, " ");
            int skip = value.Length > 0 && value[0] == ' ' && line.Length > 0 && line[line.Length - 1] == ' ' ? 1 : 0;
            line.Append(value, skip, value.Length - skip);
        }

        private static void Extract(XElement body, List<string> paragraphs, Dictionary<string, int> anchors,
            List<EpubNote> links = null, Dictionary<int, List<EpubNote>> notes = null)
        {
            var line = new StringBuilder();
            var lineNotes = new List<EpubNote>();
            EpubNote active = null;
            using (XmlReader reader = body.CreateReader())
            {
                bool more = reader.Read();
                while (more)
                {
                    string name = reader.LocalName;
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (Omitted.Contains(name) || reader.GetAttribute("hidden") != null || reader.GetAttribute("aria-hidden") == "true")
                        {
                            reader.Skip();
                            more = !reader.EOF;
                            continue;
                        }
                        if (Blocks.Contains(name)) Flush(line, paragraphs, notes, lineNotes);
                        if (name == "td" || name == "th") AppendText(line, " ");
                        int index;
                        if (links != null && !reader.IsEmptyElement && name == "a" &&
                            Int32.TryParse(reader.GetAttribute("index", NoteMetadata), out index) && index >= 0 && index < links.Count)
                        {
                            active = links[index];
                            active.Start = line.Length;
                        }
                        string id = reader.GetAttribute("id") ?? reader.GetAttribute("id", XNamespace.Xml.NamespaceName) ?? reader.GetAttribute("name");
                        if (!String.IsNullOrEmpty(id) && !anchors.ContainsKey(id)) anchors.Add(id, paragraphs.Count);
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (name == "a" && active != null) {
                            active.Length = line.Length - active.Start;
                            if (active.Length > 0) lineNotes.Add(active);
                            active = null;
                        }
                        if (Blocks.Contains(name)) Flush(line, paragraphs, notes, lineNotes);
                    }
                    else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA || reader.NodeType == XmlNodeType.Whitespace || reader.NodeType == XmlNodeType.SignificantWhitespace)
                        AppendText(line, reader.Value);
                    more = reader.Read();
                }
            }
            Flush(line, paragraphs, notes, lineNotes);
        }

        private static void Flush(StringBuilder line, List<string> paragraphs, Dictionary<int, List<EpubNote>> notes, List<EpubNote> lineNotes)
        {
            if (line.Length == 0) return;
            string raw = line.ToString();
            string value = raw.Trim();
            if (lineNotes.Count > 0) {
                int leading = raw.Length - raw.TrimStart().Length;
                foreach (var note in lineNotes) {
                    int end = note.Start + note.Length;
                    while (note.Start < end && Char.IsWhiteSpace(raw[note.Start])) note.Start++;
                    while (end > note.Start && Char.IsWhiteSpace(raw[end - 1])) end--;
                    note.Length = end - note.Start;
                    note.Start -= leading;
                }
                var visible = lineNotes.Where(n => n.Length > 0).ToList();
                if (visible.Count > 0) notes[paragraphs.Count] = visible;
                lineNotes.Clear();
            }
            if (value.Length != 0) paragraphs.Add(value);
            line.Length = 0;
        }
    }
}
