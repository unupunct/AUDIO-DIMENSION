using Serilog;

namespace AudioDimension.Core.Logging;

public static class LoggingSetup
{
    /// <summary>Configures the global Serilog logger with a daily rolling file sink (local only, 14 days kept).</summary>
    public static void Initialize(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(logDirectory, "audiodimension-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static void Shutdown() => Log.CloseAndFlush();
}
