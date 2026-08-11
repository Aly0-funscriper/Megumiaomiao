using System.IO;


namespace VideoShelf.Services
{

    public class QuickScanner
    {

        private readonly string[] extensions =
        {
            ".mp4",
            ".mkv",
            ".avi",
            ".mov",
            ".wmv",
            ".webm",
            ".m4v",
            ".mpg",
            ".mpeg",
            ".ts",
            ".m2ts",
            ".flv",
            ".vob"
            ,".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".ape", ".alac", ".aiff", ".aif"
        };



        public List<string> ScanFiles(
            string folder)
        {

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.System
            };

            return Directory.EnumerateFiles(
                folder,
                "*.*",
                options)
                .Where(file => !IsCacheFile(file))
                .Where(IsVideo)
                .ToList();

        }

        private static bool IsCacheFile(string file)
        {
            string full = Path.GetFullPath(file);
            string configuredCache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(StoragePaths.AssetRoot)) + Path.DirectorySeparatorChar;
            if (full.StartsWith(configuredCache, StringComparison.OrdinalIgnoreCase)) return true;
            return full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals(".VideoShelfCache", StringComparison.OrdinalIgnoreCase));
        }



        private bool IsVideo(string file)
        {

            return extensions.Contains(
                Path.GetExtension(file)
                .ToLower());

        }


    }

}
