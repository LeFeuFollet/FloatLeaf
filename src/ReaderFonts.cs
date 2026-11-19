using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;

namespace FloatLeaf
{
    internal static class ReaderFonts
    {
        internal const string DefaultChineseId = "noto-serif-sc";
        internal const string DefaultEnglishId = "source-serif-4";

        internal sealed class Choice
        {
            internal readonly string Id, Label, EnglishLabel, Reference;

            internal Choice(string id, string label, string english, string reference)
            {
                Id = id;
                Label = label;
                EnglishLabel = english;
                Reference = reference;
            }
        }

        private static Choice Bundled(string id, string chinese, string english, string family)
        {
            return new Choice(id, chinese, english, "./assets/fonts/#" + family);
        }

        private static readonly Choice[] Chinese = {
            Bundled(DefaultChineseId, "Noto 宋体", "Noto Serif SC", "Noto Serif SC"),
            Bundled("noto-sans-sc", "Noto 黑体", "Noto Sans SC", "Noto Sans SC"),
            Bundled("lxgw-wenkai", "霞鹜文楷", "LXGW WenKai", "LXGW WenKai GB Lite"),
            Bundled("zhuque-fangsong", "朱雀仿宋", "Zhuque Fangsong", "Zhuque Fangsong (technical preview)")
        };
        private static readonly Choice[] English = {
            Bundled(DefaultEnglishId, "Source Serif 4", "Source Serif 4", "Source Serif 4"),
            Bundled("crimson-pro", "Crimson Pro", "Crimson Pro", "Crimson Pro"),
            Bundled("inter", "Inter", "Inter", "Inter"),
            Bundled("fira-mono", "Fira Mono", "Fira Mono", "Fira Mono")
        };
        private static readonly string Imports = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "FloatLeaf", "fonts");
        private static readonly List<Choice>[] imported = new List<Choice>[2];

        private static string ImportFolder(bool chinese)
        {
            return Path.Combine(Imports, chinese ? "zh" : "en");
        }

        internal static Choice[] Choices(bool chinese)
        {
            return (chinese ? Chinese : English).Concat(Imported(chinese)).ToArray();
        }

        private static Choice Find(string id, bool chinese)
        {
            Choice[] builtins = chinese ? Chinese : English;
            return Array.Find(builtins, item => item.Id == id) ??
                (id != null && id.StartsWith("import-", StringComparison.Ordinal) ?
                    Imported(chinese).Find(item => item.Id == id) : null);
        }

        internal static bool IsKnown(string id, bool chinese) { return Find(id, chinese) != null; }

        internal static FontFamily Resolve(string chineseId, string englishId)
        {
            var cjk = Find(chineseId, true) ?? Chinese[0];
            var latin = Find(englishId, false) ?? English[0];
            var references = new[] { latin.Reference, cjk.Reference, Chinese[0].Reference, English[0].Reference };
            return new FontFamily(new Uri("pack://application:,,,/FloatLeaf;component/", UriKind.Absolute),
                String.Join(", ", references.Distinct()));
        }

        private static bool Supported(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".ttf" || ext == ".otf" || ext == ".ttc";
        }

        private static List<Choice> Imported(bool chinese)
        {
            int index = chinese ? 0 : 1;
            if (imported[index] != null) return imported[index];
            var result = new List<Choice>();
            string folder = ImportFolder(chinese);
            if (Directory.Exists(folder)) {
                try {
                    foreach (string path in Directory.GetFiles(folder).Where(Supported)) {
                        try { result.AddRange(ReadChoices(path, path, chinese)); }
                        catch (Exception e) {
                            if (!(e is IOException || e is UnauthorizedAccessException ||
                                  e is FileFormatException || e is ArgumentException)) throw;
                        }
                    }
                } catch (IOException) { }
                  catch (UnauthorizedAccessException) { }
            }
            imported[index] = result;
            return result;
        }

        private static List<Choice> ReadChoices(string source, string storedPath, bool chineseGroup)
        {
            var folder = new Uri(Path.GetDirectoryName(source) + Path.DirectorySeparatorChar);
            var result = new List<Choice>();
            string local = new Uri(storedPath).AbsoluteUri.Replace(",", "%2C");
            string fileId = Path.GetFileNameWithoutExtension(storedPath);
            foreach (var family in Fonts.GetFontFamilies(folder, Uri.EscapeDataString(Path.GetFileName(source)))) {
                string english, chinese;
                if (!family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-us"), out english))
                    english = family.FamilyNames.Values.FirstOrDefault();
                if (String.IsNullOrEmpty(english)) continue;
                if (!family.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("zh-cn"), out chinese))
                    chinese = english;
                string reference = local + "#" + english.Replace(",", ",,");
                if (!chineseGroup) {
                    // An imported English face may also cover Han. Limit its Unicode ranges
                    // so Chinese text and full-width punctuation reach the selected Chinese face.
                    string wrapper = storedPath + "-" + result.Count + ".CompositeFont";
                    SaveOnce(wrapper, temp => WriteEnglishMap(temp, storedPath, english));
                    reference = new Uri(wrapper).AbsoluteUri.Replace(",", "%2C") + "#FloatLeaf Latin";
                }
                result.Add(new Choice("import-" + fileId + "-" + english, chinese, english, reference));
            }
            return result;
        }

        private static void WriteEnglishMap(string path, string storedFont, string familyName)
        {
            XNamespace f = "http://schemas.microsoft.com/winfx/2006/xaml/composite-font";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            XNamespace sys = "clr-namespace:System;assembly=mscorlib";
            var map = new XElement(f + "FontFamily", new XAttribute(XNamespace.Xmlns + "x", x),
                new XAttribute(XNamespace.Xmlns + "sys", sys),
                new XElement(f + "FontFamily.FamilyNames",
                    new XElement(sys + "String", new XAttribute(x + "Key", "en-US"), "FloatLeaf Latin")),
                new XElement(f + "FontFamily.FamilyMaps", new XElement(f + "FontFamilyMap",
                    new XAttribute("Unicode", "0000-2E7F,A000-ABFF,FB00-FDFF,FE20-FE2F,FE70-FEFE,10000-1FFFF"),
                    new XAttribute("Target", "./" + Path.GetFileName(storedFont) + "#" + familyName.Replace(",", ",,")))));
            map.Save(path);
        }

        private static void SaveOnce(string path, Action<string> writer)
        {
            if (File.Exists(path)) return;
            string pending = Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp");
            try {
                writer(pending);
                try { File.Move(pending, path); }
                catch (IOException) { if (!File.Exists(path)) throw; }
            } finally { if (File.Exists(pending)) File.Delete(pending); }
        }

        internal static Choice Import(string path, bool chinese)
        {
            path = Path.GetFullPath(path);
            if (!Supported(path)) throw new FileFormatException();
            var catalog = Imported(chinese);
            string folder = ImportFolder(chinese);
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var hash = SHA256.Create()) {
                string id = BitConverter.ToString(hash.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
                string stored = Path.Combine(folder, id + Path.GetExtension(path).ToLowerInvariant());
                Directory.CreateDirectory(folder);
                var choices = ReadChoices(path, stored, chinese);
                if (choices.Count == 0) throw new FileFormatException();
                SaveOnce(stored, delegate(string pending) {
                    source.Position = 0;
                    using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        source.CopyTo(output);
                });
                foreach (var item in choices)
                    if (!catalog.Any(other => other.Id == item.Id)) catalog.Add(item);
                return choices[0];
            }
        }
    }
}
