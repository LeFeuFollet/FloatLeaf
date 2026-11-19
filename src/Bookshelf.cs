using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace FloatLeaf
{
    internal static class Bookshelf
    {
        internal static readonly string Folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Books");

        internal sealed class Entry
        {
            internal readonly string Path, Title;
            internal readonly EpubBook Book;

            internal Entry(string path, string title, EpubBook book = null)
            {
                Path = path;
                Title = title;
                Book = book;
            }
        }

        private static IEnumerable<FileInfo> Files()
        {
            return new DirectoryInfo(Folder).EnumerateFiles("*.epub")
                .Where(file => file.Extension.Equals(".epub", StringComparison.OrdinalIgnoreCase));
        }

        internal static List<Entry> ListBooks()
        {
            var entries = new List<Entry>();
            if (!Directory.Exists(Folder)) return entries;
            foreach (FileInfo file in Files()) {
                string title;
                try { title = EpubBook.ReadTitle(file.FullName); }
                catch (FileNotFoundException) { continue; }
                catch (InvalidDataException) { title = Path.GetFileNameWithoutExtension(file.Name); }
                catch (IOException) { title = Path.GetFileNameWithoutExtension(file.Name); }
                catch (UnauthorizedAccessException) { title = Path.GetFileNameWithoutExtension(file.Name); }
                entries.Add(new Entry(file.FullName, title));
            }
            return entries.OrderBy(book => book.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(book => book.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        internal static Entry Import(string sourcePath)
        {
            sourcePath = Path.GetFullPath(sourcePath);
            using (var source = File.OpenRead(sourcePath)) {
                var book = EpubBook.Load(sourcePath);
                if (String.Equals(Path.GetDirectoryName(sourcePath), Folder, StringComparison.OrdinalIgnoreCase))
                    return new Entry(sourcePath, book.Title, book);

                Directory.CreateDirectory(Folder);
                using (var gate = new Mutex(false, @"Local\FloatLeaf_Books_Import")) {
                    try { gate.WaitOne(); }
                    catch (AbandonedMutexException) { /* Ownership passes to this importer. */ }
                    try {
                        string copy = CopyToShelf(source);
                        return new Entry(copy, book.Title, book);
                    } finally { gate.ReleaseMutex(); }
                }
            }
        }

        private static string CopyToShelf(FileStream source)
        {
            byte[] fingerprint = null;
            using (var hash = SHA256.Create()) {
                foreach (FileInfo file in Files()) {
                    if (file.Length != source.Length) continue;
                    if (fingerprint == null) fingerprint = hash.ComputeHash(source);
                    using (var candidate = File.OpenRead(file.FullName))
                        if (fingerprint.SequenceEqual(hash.ComputeHash(candidate))) return file.FullName;
                }
            }

            string stem = Path.GetFileNameWithoutExtension(source.Name);
            string target = Path.Combine(Folder, stem + ".epub");
            for (int number = 2; File.Exists(target); number++)
                target = Path.Combine(Folder, stem + " (" + number + ").epub");
            string staging = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
            try {
                source.Position = 0;
                using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    source.CopyTo(output);
                File.Move(staging, target);
                return target;
            } finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }
}
