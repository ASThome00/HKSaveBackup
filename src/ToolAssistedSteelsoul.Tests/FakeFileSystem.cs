using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ToolAssistedSteelsoul.Core;

namespace ToolAssistedSteelsoul.Tests
{
    /// <summary>In-memory IBackupFileSystem. Paths are compared case-insensitively, like NTFS.</summary>
    public sealed class FakeFileSystem : IBackupFileSystem
    {
        private readonly Dictionary<string, string> _files =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _directories =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Files => _files;

        public void AddFile(string path, string contents = "")
        {
            _files[Normalize(path)] = contents;
            CreateDirectory(Path.GetDirectoryName(path));
        }

        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));

        public void CopyFile(string source, string destination, bool overwrite)
        {
            string src = Normalize(source);
            string dst = Normalize(destination);
            if (!_files.ContainsKey(src))
                throw new FileNotFoundException(source);
            if (!overwrite && _files.ContainsKey(dst))
                throw new IOException($"File already exists: {destination}");
            _files[dst] = _files[src];
        }

        public void DeleteFile(string path)
        {
            if (!_files.Remove(Normalize(path)))
                throw new FileNotFoundException(path);
        }

        public string ReadAllText(string path)
        {
            if (!_files.TryGetValue(Normalize(path), out string contents))
                throw new FileNotFoundException(path);
            return contents;
        }

        public void WriteAllText(string path, string contents) => _files[Normalize(path)] = contents;

        public void CreateDirectory(string path)
        {
            if (!string.IsNullOrEmpty(path))
                _directories.Add(Normalize(path));
        }

        public IReadOnlyList<string> ListFiles(string directory, string searchPattern)
        {
            string dir = Normalize(directory);
            if (!searchPattern.StartsWith("*", StringComparison.Ordinal))
                throw new NotSupportedException("FakeFileSystem only supports *.ext patterns");
            string extension = searchPattern.Substring(1);
            return _files.Keys
                .Where(p => string.Equals(Path.GetDirectoryName(p), dir, StringComparison.OrdinalIgnoreCase)
                            && p.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string Normalize(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
    }
}
