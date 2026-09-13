using System.Text.RegularExpressions;
using StorageStandby.Backend.Models;

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

        // Returns the total size of all files beneath a watched folder, in bytes.
        public long GetWatchedFolderSize(
            WatchedFolder watchedFolder,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(watchedFolder);
            ArgumentException.ThrowIfNullOrWhiteSpace(watchedFolder.LocalPath);

            long totalBytes = 0;
            foreach (string filePath in Directory.EnumerateFiles(
                watchedFolder.LocalPath,
                "*",
                SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalBytes = checked(totalBytes + new FileInfo(filePath).Length);
            }

            return totalBytes;
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

        public class FileFolders {
            public List<string> Files { get; set; } = new();
            public List<string> Folders { get; set; } = new();
        }

        public FileFolders? GetAllNestedChildren(string path) {
            if (Directory.Exists(path)) {
                List<string> files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToList();
                List<string> folders = Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories).ToList();

                return new FileFolders{
                    Files = files,
                    Folders = folders,
                };

            }

            return null;
        }


        // ------------------------------------------------------------------------------------------------------------
        // Ignore Rules-Related Methods (see WatchedFolder and FileSystemWatcherWorker)
        // https://code.visualstudio.com/docs/editor/glob-patterns
        public bool IsFileIgnored(string ignoreRulesGlob, string path) // glob pattern reader
        {
            ArgumentNullException.ThrowIfNull(ignoreRulesGlob);
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            string normalizedPath = path.Replace('\\', '/');

            return ignoreRulesGlob
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(rule => MatchesGlob(rule, normalizedPath));
        }

        private static bool MatchesGlob(string rule, string normalizedPath)
        {
            bool matchesDescendants = rule.EndsWith('/');
            string normalizedRule = rule.Trim().Replace('\\', '/').TrimEnd('/');

            if (normalizedRule.Length == 0)
            {
                return false;
            }

            string pattern = GlobToRegex(normalizedRule);

            if (matchesDescendants)
            {
                pattern += "(?:/.*)?";
            }

            return Regex.IsMatch(
                normalizedPath,
                $"^{pattern}$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string GlobToRegex(string glob)
        {
            var pattern = new System.Text.StringBuilder();

            for (int index = 0; index < glob.Length; index++)
            {
                char character = glob[index];

                switch (character)
                {
                    case '*':
                        if (index + 1 < glob.Length && glob[index + 1] == '*')
                        {
                            index++;
                            if (index + 1 < glob.Length && glob[index + 1] == '/')
                            {
                                pattern.Append("(?:.*/)?");
                                index++;
                            }
                            else
                            {
                                pattern.Append(".*");
                            }
                        }
                        else
                        {
                            pattern.Append("[^/]*");
                        }
                        break;
                    case '?':
                        pattern.Append("[^/]");
                        break;
                    case '[':
                        index = AppendCharacterClass(pattern, glob, index);
                        break;
                    case '{':
                        index = AppendBraceAlternation(pattern, glob, index);
                        break;
                    default:
                        pattern.Append(Regex.Escape(character.ToString()));
                        break;
                }
            }

            return pattern.ToString();
        }

        private static int AppendCharacterClass(
            System.Text.StringBuilder pattern,
            string glob,
            int openingBracketIndex)
        {
            int closingBracketIndex = glob.IndexOf(']', openingBracketIndex + 1);
            if (closingBracketIndex < 0)
            {
                pattern.Append("\\[");
                return openingBracketIndex;
            }

            string characterClass = glob[(openingBracketIndex + 1)..closingBracketIndex];
            if (characterClass.StartsWith('!'))
            {
                characterClass = '^' + characterClass[1..];
            }

            pattern.Append('[').Append(characterClass).Append(']');
            return closingBracketIndex;
        }

        private static int AppendBraceAlternation(
            System.Text.StringBuilder pattern,
            string glob,
            int openingBraceIndex)
        {
            int closingBraceIndex = FindMatchingBrace(glob, openingBraceIndex);
            if (closingBraceIndex < 0)
            {
                pattern.Append("\\{");
                return openingBraceIndex;
            }

            string contents = glob[(openingBraceIndex + 1)..closingBraceIndex];
            string[] alternatives = contents.Split(',');
            pattern.Append("(?:");
            for (int index = 0; index < alternatives.Length; index++)
            {
                if (index > 0)
                {
                    pattern.Append('|');
                }

                pattern.Append(GlobToRegex(alternatives[index]));
            }

            pattern.Append(")");
            return closingBraceIndex;
        }

        private static int FindMatchingBrace(string glob, int openingBraceIndex)
        {
            int depth = 0;
            for (int index = openingBraceIndex; index < glob.Length; index++)
            {
                if (glob[index] == '{')
                {
                    depth++;
                }
                else if (glob[index] == '}' && --depth == 0)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}