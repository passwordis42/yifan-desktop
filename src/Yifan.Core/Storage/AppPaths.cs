namespace Yifan.Core.Storage;

public static class AppPaths
{
    public const string FolderName = "Yifan";
    public const string SettingsFileName = "settings.json";
    public const string UsageFileName = "usage.json";
    public const string LogDirectoryName = "logs";
    public const string LogFileName = "yifan.log";

    public static string Root(string appData) => Path.Combine(appData, FolderName);
    public static string SettingsPath(string appData) => Path.Combine(Root(appData), SettingsFileName);
    public static string UsagePath(string appData) => Path.Combine(Root(appData), UsageFileName);
    public static string LogDirectory(string appData) => Path.Combine(Root(appData), LogDirectoryName);
    public static string LogPath(string appData) => Path.Combine(LogDirectory(appData), LogFileName);

    public static bool IsUnder(string path, string directory)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static string RequireSettingsPath(string? appData, string exeDirectory)
    {
        if (string.IsNullOrWhiteSpace(appData))
            throw new InvalidOperationException("找不到本机应用数据目录，设置不会写到程序旁边。");
        var path = SettingsPath(appData);
        if (IsUnder(path, exeDirectory))
            throw new InvalidOperationException("拒绝把设置写到程序目录。");
        return path;
    }
}
