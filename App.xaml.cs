using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using FixletBuilder.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;

namespace FixletBuilder;

public partial class App : Application
{
    private ILoggerFactory? _loggerFactory;
    private IConfiguration? _configuration;

    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    protected override void OnStartup(StartupEventArgs e)
    {
        _configuration = BuildConfiguration();
        var logPath = _configuration["Logging:LogFilePath"];
        _loggerFactory = LoggingBootstrap.CreateLoggerFactory(logPath);

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (e.Args.Length > 0)
        {
            AllocConsole();
            var logger = _loggerFactory.CreateLogger("App");
            logger.LogInformation("FixletBuilder CLI starting with {Count} args", e.Args.Length);

            var exitCode = CliRunner.Run(e.Args).GetAwaiter().GetResult();
            LoggingBootstrap.Close();
            Environment.Exit(exitCode);
            return;
        }

        base.OnStartup(e);
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "FIXLETBUILDER_")
            .Build();
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Log.Logger?.Fatal(ex, "Unhandled domain exception");
        MessageBox.Show("An unexpected error occurred:\n" + e.ExceptionObject,
            "FixletBuilder Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Logger?.Error(e.Exception, "Unhandled dispatcher exception");
        MessageBox.Show("An error occurred:\n" + e.Exception.Message,
            "FixletBuilder Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Logger?.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggingBootstrap.Close();
        base.OnExit(e);
    }
}