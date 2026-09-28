using Serilog;

namespace Jarvis.Core.Logging;

public static class LoggingSetup
{
    public static Serilog.ILogger CreateLogger(string logDirectory, int retentionDays)
    {
        Directory.CreateDirectory(logDirectory);
        return new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDirectory, "jarvis-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retentionDays)
            .CreateLogger();
    }
}
