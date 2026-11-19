using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FloatLeaf
{
    public sealed partial class EpubBook
    {
        private const string NoteMetadata = "urn:floatleaf:epub:reading";
        private static readonly Regex NoteId = new Regex(@"^_?(?:(?:foot|end|rear)?notes?|ftn|edn|fns?|n)[-_.:]*(?:\d|[ivx]+$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex ReturnId = new Regex(@"^_?(?:(?:foot|end)?note[-_.:]?refs?|(?:fn|ftn|edn)[-_.:]?(?:refs?|back|anchor)|backlinks?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Raised = new Regex(@"vertical-align\s*:\s*super\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex WordNote = new Regex(@"(?:^|;)\s*mso-element\s*:\s*(?:footnote|endnote)\s*(?:;|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private sealed class NoteReference
        {
            internal string Fragment;
            internal bool Marked;
            internal EpubNote Note = new EpubNote();
        }

        private static bool HasSemantic(XElement element, params string[] names)
        {
            return element.Attributes().Where(a => a.Name.LocalName == "type" || a.Name.LocalName == "role" || a.Name.LocalName == "class")
                .Any(a => a.Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Any(v => names.Contains(v, StringComparer.OrdinalIgnoreCase)));
        }

        private static bool NoteContainer(XElement element)
        {
            return HasSemantic(element, "footnote", "endnote", "rearnote", "doc-footnote", "doc-endnote", "duokan-footnote-item") ||
                WordNote.IsMatch(Attr(element, "style") ?? "");
        }

        private static bool Superscript(XElement element)
        {
            string style = Attr(element, "style");
            return Named(element, "sup") || style != null && Raised.IsMatch(style);
        }

        private static bool Backlink(XElement link)
        {
            if (!Named(link, "a")) return false;
            string href = Attr(link, "href") ?? "";
            int hash = href.IndexOf('#');
            return HasSemantic(link, "backlink", "doc-backlink", "footnote-back", "footnote-backref", "reversefootnote") ||
                hash >= 0 && ReturnId.IsMatch(href.Substring(hash + 1)) ||
                hash >= 0 && (link.Value.Trim() == "↩" || link.Value.Trim() == "↵" || link.Value.Trim() == "↑");
        }

        private sealed partial class Package
        {
            private List<EpubNote> CollectReferences(XElement body, string path)
            {
                var links = new List<EpubNote>();
                foreach (XElement link in body.Descendants().Where(e => Named(e, "a")))
                {
                    string href = Attr(link, "href") ?? "";
                    int hash = href.IndexOf('#');
                    if (hash < 0 || hash == href.Length - 1 || Backlink(link)) continue;
                    string destination = Resolve(path, href);
                    if (destination == null || link.Descendants().Any(e => Blocks.Contains(e.Name.LocalName))) continue;
                    string fragment = Uri.UnescapeDataString(href.Substring(hash + 1));
                    bool marked = HasSemantic(link, "noteref", "doc-noteref", "note-ref", "footnote-ref", "duokan-footnote") ||
                        link.AncestorsAndSelf().Any(Superscript) || link.Descendants().Any(Superscript) ||
                        NoteId.IsMatch(fragment);
                    if (String.IsNullOrWhiteSpace(link.Value))
                    {
                        if (!marked) continue;
                        var image = link.Descendants().FirstOrDefault(e => Named(e, "img"));
                        string label = Clean(Attr(image, "alt") ?? Attr(link, "aria-label") ?? "");
                        link.Add(new XText(label.Length > 0 ? label : "※"));
                    }
                    var reference = new NoteReference { Fragment = fragment, Marked = marked };
                    List<NoteReference> pending;
                    if (!pendingNotes.TryGetValue(destination, out pending))
                        pendingNotes[destination] = pending = new List<NoteReference>();
                    pending.Add(reference);
                    link.SetAttributeValue(XName.Get("index", NoteMetadata), links.Count.ToString(CultureInfo.InvariantCulture));
                    links.Add(reference.Note);
                }
                return links;
            }

            private void ResolveNotes(string path, XDocument document)
            {
                List<NoteReference> references;
                if (!pendingNotes.TryGetValue(path, out references)) return;
                pendingNotes.Remove(path);
                var byId = references.ToLookup(r => r.Fragment, StringComparer.Ordinal);
                foreach (XElement element in document.Descendants())
                {
                    string id = Attr(element, "id") ?? (string)element.Attribute(XNamespace.Xml + "id") ?? Attr(element, "name");
                    if (id == null || !byId.Contains(id)) continue;
                    var semantic = element.AncestorsAndSelf().FirstOrDefault(NoteContainer);
                    var matches = byId[id].Where(r => r.Marked || semantic != null).ToList();
                    if (matches.Count == 0) continue;
                    XElement target = element.AncestorsAndSelf().FirstOrDefault(e => Named(e, "li")) ??
                        semantic ?? element.AncestorsAndSelf().FirstOrDefault(e => Blocks.Contains(e.Name.LocalName));
                    // An EPUB 2 anchor may stand alone immediately before the actual note paragraph.
                    if (target == null || String.IsNullOrWhiteSpace(element.Value) && target != element && target.Elements().Any(e => Blocks.Contains(e.Name.LocalName)))
                        target = element.ElementsAfterSelf().FirstOrDefault(e => Blocks.Contains(e.Name.LocalName)) ?? target;
                    if (target != null && String.IsNullOrWhiteSpace(target.Value))
                        target = target.ElementsAfterSelf().FirstOrDefault(e => Blocks.Contains(e.Name.LocalName));
                    if (target == null) continue;
                    var excerpt = new XElement(target);
                    excerpt.Descendants().Where(Backlink).Remove();
                    // Notes are sometimes hidden in the reading flow but visible in an EPUB pop-up.
                    excerpt.SetAttributeValue("hidden", null);
                    excerpt.SetAttributeValue("aria-hidden", null);
                    var paragraphs = new List<string>();
                    Extract(excerpt, paragraphs, new Dictionary<string, int>());
                    string text = String.Join("\n", paragraphs);
                    foreach (var reference in matches) reference.Note.Text = text;
                }
            }
        }
    }
}
