using System.Text.RegularExpressions;

namespace Yifan.Core.Logging;

public static class LogRedactor
{
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var output = Regex.Replace(text, @"sk-[A-Za-z0-9_-]{6,}", "sk-***");
        output = Regex.Replace(output, @"Bearer\s+[A-Za-z0-9._\-]{8,}", "Bearer ***");
        return output;
    }
}

public sealed class FileLog
{
    private readonly string _path;
    private readonly object _gate = new();
    public const long RotateBytes = 2 * 1024 * 1024;

    public FileLog(string path) => _path = path;

    public void Info(string message) => Write("INFO", message, null);

    public void Error(Exception exception, string message) => Write("ERROR", message, exception);

    public void Write(string level, string message, Exception? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {LogRedactor.Redact(message)}";
            if (exception is not null)
                line += " | " + LogRedactor.Redact(exception.GetType().Name + ": " + exception.Message);
            lock (_gate)
            {
                RotateIfNeeded();
                File.AppendAllText(_path, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // 日志失败不能再把进程带崩。
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_path)) return;
        var info = new FileInfo(_path);
        if (info.Length < RotateBytes) return;
        var backup = _path + ".1";
        File.Copy(_path, backup, overwrite: true);
        File.WriteAllText(_path, "");
    }
}
