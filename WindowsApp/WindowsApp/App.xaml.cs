using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace WindowsApp;

public partial class App : Application
{
    private static readonly string LogFile = @"D:\AK10X\app_error.log";

    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--snapshot"))
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                try
                {
                    var mw = Current.Windows.OfType<MainWindow>().FirstOrDefault() ?? (MainWindow as MainWindow);
                    if (mw != null)
                    {
                        string destDir = @"C:\Users\AK\.gemini\antigravity-ide\brain\603eb494-c65c-4022-b9e5-e29e07d4613c";
                        for (int i = 0; i < e.Args.Length - 1; i++)
                        {
                            if (e.Args[i] == "--output-dir")
                            {
                                destDir = e.Args[i + 1];
                                break;
                            }
                        }
                        mw.ExportViewSnapshots(destDir);
                        File.WriteAllText(@"D:\AK10X\snapshot.log", $"Snapshot export completed at {DateTime.Now} to {destDir}");
                    }
                    else
                    {
                        File.WriteAllText(@"D:\AK10X\snapshot.log", $"MainWindow was null at {DateTime.Now}. Windows count: {Current.Windows.Count}");
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(@"D:\AK10X\snapshot.log", $"Error during snapshot: {ex}");
                }
                Shutdown(0);
            };
            timer.Start();
        }
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        e.Handled = true; // Prevent app crash!
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("CurrentDomain_UnhandledException", ex);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogException("TaskScheduler_UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string source, Exception ex)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{source}] {ex.GetType().FullName}: {ex.Message}");
            sb.AppendLine(ex.StackTrace);
            var inner = ex.InnerException;
            int depth = 1;
            while (inner != null)
            {
                sb.AppendLine($"  Inner #{depth}: {inner.GetType().FullName}: {inner.Message}");
                sb.AppendLine($"  Stack: {inner.StackTrace}");
                inner = inner.InnerException;
                depth++;
            }
            sb.AppendLine("----------------------------------------");
            File.AppendAllText(LogFile, sb.ToString());
        }
        catch { }
    }
}
