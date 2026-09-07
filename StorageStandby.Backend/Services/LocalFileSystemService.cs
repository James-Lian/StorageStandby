namespace StorageStandby.Backend.Services
{
    // For now: stateless method reference
    public class LocalFileSystemService
    {
        // Returns null if not a valid path
        public bool? IsFolder(string path) {
            if (File.Exists(path)) {
                return false;
            } 
            else if (Directory.Exists(path)) {
                return true;
            }

            return null;
        }
        // Returns null if not a valid path
        public long? GetPathSize(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (Directory.Exists(path))
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(filePath => new FileInfo(filePath).Length);
            }

            return null;
        }

        public long GetFolderSize(string folderPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

            return Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
                .Sum(filePath => new FileInfo(filePath).Length);
        }

        public long GetFileSize(string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            return new FileInfo(filePath).Length;
        }
    
        public class FileFolderCount {
            public long Files { get; set; }
            public long Folders { get; set; }
        }

        // Returns null if path not a folder
        public FileFolderCount? GetChildrenCount(string path) {
            long files = 0;
            long folders = 0;
            if (Directory.Exists(path)) {
                files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
                folders = Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).Count();

                return new FileFolderCount{
                    Files = files,
                    Folders = folders
                };
            }

            return null;
        }
    }
}