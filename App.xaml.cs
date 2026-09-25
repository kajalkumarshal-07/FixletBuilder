using System.Diagnostics;
using System.IO;
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
    private static bool _cliMode;

    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private const int StdInputHandle = -10;
    private const uint FileTypeUnknown = 0x0000;
    private const uint FileTypeChar = 0x0002;
    private const uint FileTypePipe = 0x0003;
    private const uint FileTypeDisk = 0x0001;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr hFile);

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
            _cliMode = true;
            RedirectConsoleStreams();
            AttachOrReuseConsole();
            SynchronizationContext.SetSynchronizationContext(null);

            var logger = _loggerFactory.CreateLogger("App");
            logger.LogInformation("FixletBuilder CLI starting with {Count} args", e.Args.Length);

            int exitCode;
            try
            {
                exitCode = Task.Run(() => CliRunner.Run(e.Args)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FATAL: " + ex);
                exitCode = 99;
            }

            LoggingBootstrap.Close();
            Environment.Exit(exitCode);
            return;
        }

        base.OnStartup(e);
    }

    private static void RedirectConsoleStreams()
    {
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput()));
        }
        catch
        {
            // best effort - keep default streams
        }
    }

    private static void AttachOrReuseConsole()
    {
        var stdout = GetStdHandle(StdOutputHandle);
        var stderr = GetStdHandle(StdErrorHandle);
        var stdoutType = stdout == IntPtr.Zero || stdout == new IntPtr(-1)
            ? FileTypeUnknown
            : GetFileType(stdout);
        var stderrType = stderr == IntPtr.Zero || stderr == new IntPtr(-1)
            ? FileTypeUnknown
            : GetFileType(stderr);

        var redirected = stdoutType is FileTypePipe or FileTypeDisk ||
                         stderrType is FileTypePipe or FileTypeDisk;

        if (redirected)
        {
            RedirectConsoleStreams();
            return;
        }

        if (!AttachConsole(AttachParentProcess))
            AllocConsole();

        RedirectConsoleStreams();
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

        if (_cliMode)
        {
            Console.Error.WriteLine("Unhandled error: " + e.ExceptionObject);
            LoggingBootstrap.Close();
            Environment.Exit(99);
            return;
        }

        MessageBox.Show("An unexpected error occurred:\n" + e.ExceptionObject,
            "FixletBuilder Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Logger?.Error(e.Exception, "Unhandled dispatcher exception");

        if (_cliMode)
        {
            Console.Error.WriteLine("Error: " + e.Exception);
            e.Handled = true;
            LoggingBootstrap.Close();
            Environment.Exit(99);
            return;
        }

        MessageBox.Show("An error occurred:\n" + e.Exception.Message,
            "FixletBuilder Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Logger?.Error(e.Exception, "Unhandled task exception");
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LoggingBootstrap.Close();
        base.OnExit(e);
    }
}
