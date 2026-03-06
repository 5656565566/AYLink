using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using AYLink.UI;

namespace AYLink;

internal class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // 全局异常捕获
        SetupGlobalExceptionHandling();

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // 捕获启动阶段或主循环直接抛出的致命错误
            LogFatalError(ex, "MainLoop");
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static void SetupGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            LogFatalError(ex, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LogFatalError(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };
    }
    private static void LogFatalError(Exception? ex, string source)
    {
        if (ex == null) return;

        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            string message = $"[{DateTime.Now}] [{source}] Critical Error:\n{ex}\n--------------------------\n";
            File.AppendAllText(logPath, message);
            Debug.WriteLine($"崩溃已捕获: {message}");
        }
        catch { }
    }
}