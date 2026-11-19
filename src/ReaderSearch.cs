using System;
using System.Collections.Generic;
using System.Threading;

namespace FloatLeaf
{
    internal sealed class ReaderSearch
    {
        internal sealed class Hit
        {
            internal int Chapter, Paragraph, Offset, Length;
        }

        private readonly EpubBook book;
        private readonly int chapter, paragraph, offset;
        internal string Query = "";
        internal bool Later;

        internal ReaderSearch(EpubBook book, int chapter, int paragraph, int offset)
        {
            this.book = book;
            this.chapter = chapter;
            this.paragraph = paragraph;
            this.offset = offset;
        }

        // A match that straddles the reading position belongs to the protected side.
        internal List<Hit> Find(string term, bool later, Hit after, int limit, CancellationToken token)
        {
            var result = new List<Hit>();
            if (term.Length == 0) return result;
            int firstChapter = after == null ? chapter : after.Chapter;
            int firstParagraph = after == null ? paragraph : after.Paragraph;
            int step = later ? 1 : -1;
            for (int c = firstChapter; c >= 0 && c < book.Chapters.Count; c += step) {
                var paragraphs = book.Chapters[c].Paragraphs;
                int first = c == firstChapter ? firstParagraph : (later ? 0 : paragraphs.Count - 1);
                for (int p = first; p >= 0 && p < paragraphs.Count; p += step) {
                    token.ThrowIfCancellationRequested();
                    string text = paragraphs[p];
                    int boundary = later ? 0 : text.Length;
                    if (c == firstChapter && p == firstParagraph) {
                        if (after != null) boundary = later ? after.Offset + 1 : after.Offset + term.Length - 1;
                        else boundary = later ? Math.Max(0, offset - term.Length + 1) : offset;
                    }
                    boundary = Math.Min(text.Length, Math.Max(0, boundary));
                    while (later ? boundary <= text.Length - term.Length : boundary >= term.Length) {
                        token.ThrowIfCancellationRequested();
                        int index = later ? text.IndexOf(term, boundary, StringComparison.OrdinalIgnoreCase) :
                            text.LastIndexOf(term, boundary - 1, boundary, StringComparison.OrdinalIgnoreCase);
                        if (index < 0) break;
                        result.Add(new Hit { Chapter = c, Paragraph = p, Offset = index, Length = term.Length });
                        if (result.Count == limit) return result;
                        boundary = later ? index + 1 : index + term.Length - 1;
                    }
                }
            }
            return result;
        }

        internal string Describe(Hit hit, bool later)
        {
            EpubChapter section = book.Chapters[hit.Chapter];
            string text = section.Paragraphs[hit.Paragraph];
            int visibleEnd = !later && hit.Chapter == chapter && hit.Paragraph == paragraph
                ? Math.Min(text.Length, offset) : text.Length;
            int left = Math.Max(0, hit.Offset - 26);
            int right = Math.Min(visibleEnd, hit.Offset + Math.Min(hit.Length, 80) + 34);
            if (left > 0 && Char.IsLowSurrogate(text[left])) left--;
            if (right < visibleEnd && right > 0 && Char.IsHighSurrogate(text[right - 1])) right++;
            string extract = (left > 0 ? "…" : "") + text.Substring(left, right - left) + (right < visibleEnd ? "…" : "");
            return section.Title + "  ·  " + extract;
        }
    }
}
