using System.IO;

namespace VideoShelf.Services;

public static class FunscriptService
{
    public static IReadOnlyList<string> GetCandidatePaths(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath)) return [];

        return
        [
            Path.ChangeExtension(mediaPath, ".funscript"),
            Path.ChangeExtension(mediaPath, ".Lnip.funscript"),
            Path.ChangeExtension(mediaPath, ".Rnip.funscript")
        ];
    }

    public static IReadOnlyList<string> GetExistingPaths(string mediaPath)
    {
        try { return GetCandidatePaths(mediaPath).Where(File.Exists).ToArray(); }
        catch { return []; }
    }

    public static bool HasMatchingScript(string mediaPath) => GetExistingPaths(mediaPath).Count > 0;
}
