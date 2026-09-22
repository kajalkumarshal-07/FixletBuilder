using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace FixletBuilder.Core;

public static class LoggingBootstrap
{
    public static ILoggerFactory CreateLoggerFactory(string? logFilePath = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            appData = Path.GetTempPath();

        var logPath = logFilePath ?? Path.Combine(
            appData,
            "FixletBuilder",
            "fixletbuilder-.log");

        try
        {
            var logDir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir))
                Directory.CreateDirectory(logDir);
        }
        catch
        {
            // logging directory creation is best-effort
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "FixletBuilder")
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Information)
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                restrictedToMinimumLevel: LogEventLevel.Verbose)
            .CreateLogger();

        return LoggerFactory.Create(builder =>
        {
            builder.AddSerilog();
        });
    }

    public static void Close()
    {
        Log.CloseAndFlush();
    }
}