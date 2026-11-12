using Yifan.Core.Logging;
using Yifan.Core.Storage;
using Yifan.Platform;

namespace Yifan.Logging;

internal static class Crash
{
    public static void Write(Exception exception, string message)
    {
        try
        {
            var log = FileLogHolder.Current;
            if (log is null)
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (string.IsNullOrWhiteSpace(root)) return;
                log = new FileLog(AppPaths.LogPath(root));
            }
            log.Error(exception, message);
        }
        catch (Exception)
        {
            // 崩溃记录本身不能再抛。
        }
    }
}
