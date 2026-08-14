using System.Collections.Generic;

namespace HKSaveBackup.Core
{
    /// <summary>
    /// Filesystem abstraction so backup/retention logic is unit-testable without disk.
    /// </summary>
    public interface IBackupFileSystem
    {
        bool FileExists(string path);
        void CopyFile(string source, string destination, bool overwrite);
        void DeleteFile(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        void CreateDirectory(string path);

        /// <summary>Full paths of files in <paramref name="directory"/> matching a "*.ext" pattern. Empty if the directory does not exist.</summary>
        IReadOnlyList<string> ListFiles(string directory, string searchPattern);
    }
}
