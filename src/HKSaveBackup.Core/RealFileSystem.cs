using System.Collections.Generic;
using System.IO;

namespace HKSaveBackup.Core
{
    public sealed class RealFileSystem : IBackupFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);

        public void CopyFile(string source, string destination, bool overwrite) =>
            File.Copy(source, destination, overwrite);

        public void DeleteFile(string path) => File.Delete(path);

        public string ReadAllText(string path) => File.ReadAllText(path);

        public void WriteAllText(string path, string contents) => File.WriteAllText(path, contents);

        public void CreateDirectory(string path) => Directory.CreateDirectory(path);

        public IReadOnlyList<string> ListFiles(string directory, string searchPattern)
        {
            if (!Directory.Exists(directory))
                return new string[0];
            return Directory.GetFiles(directory, searchPattern);
        }
    }
}
