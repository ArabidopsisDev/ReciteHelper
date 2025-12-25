using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace ReciteHelper
{
    public partial class App : Application
    {
        protected void OnStartup(object sender, StartupEventArgs e)
        {
            SetupExceptionHandling();
        }

        private void SetupExceptionHandling()
        {
            this.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            HandleException(e.Exception, "Dispatcher (UI Thread)");

            // Remind users that something went wrong
            var result = MessageBox.Show(
                $"程序发生错误：{e.Exception.Message}\n\n是否继续运行？\n（选择否将退出程序）",
                "应用程序错误",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.No)
            {
                ShutdownGracefully();
            }
        }

        private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            bool isTerminating = e.IsTerminating;

            HandleException(exception, $"AppDomain (Non-UI Thread) - Terminating: {isTerminating}");

            // Remind users that something went wrong
            if (isTerminating)
            {
                MessageBox.Show(
                    $"程序即将关闭：{exception?.Message ?? "未知错误"}",
                    "严重错误",
                    MessageBoxButton.OK,
                    MessageBoxImage.Stop);

                ShutdownGracefully();
            }
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            HandleException(e.Exception, "Unobserved Task");
            e.SetObserved();
        }

        private void HandleException(Exception ex, string source)
        {
            // Generating error logs facilitates subsequent processing
            try
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                string logContent = $@"
                        [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === {source} ===
                        Message: {ex?.Message}
                        Type: {ex?.GetType().FullName}
                        Stack Trace:
                        {ex?.StackTrace}

                        Inner Exception: {(ex?.InnerException != null ? "Yes" : "No")}
                        Inner Message: {ex?.InnerException?.Message}
                        Inner Stack: 
                        {ex?.InnerException?.StackTrace}

                        Source: {ex?.Source}
                        Target Site: {ex?.TargetSite}

                        App Domain: {AppDomain.CurrentDomain.FriendlyName}
                        Thread: {Environment.CurrentManagedThreadId}
                        UI Thread: {System.Threading.Thread.CurrentThread == System.Windows.Threading.Dispatcher.CurrentDispatcher.Thread}
                 ";

                File.AppendAllText(logPath, logContent + new string('-', 80) + "\n\n");
                Console.Error.WriteLine($"Error ({source}): {ex?.Message}");
                WriteToEventLog(ex, source);
                Console.WriteLine("错误日志以保存至 error.log，请发送给开发者寻求帮助", "错误", 
                    MessageBoxButton.OK,MessageBoxImage.Error);
            }
            catch (Exception logEx)
            {
                try
                {
                    string simpleLog = $"[{DateTime.Now}] Failed to log error: {logEx.Message}. Original: {ex?.Message}";
                    File.AppendAllText("error_fallback.log", simpleLog);
                }
                catch { }
            }
        }

        private void WriteToEventLog(Exception ex, string source)
        {
            try
            {
                string eventSource = "ReciteHelper";

                if (!EventLog.SourceExists(eventSource))
                    EventLog.CreateEventSource(eventSource, "Application");

                string eventMessage = $"{source}: {ex?.Message}\nStack: {ex?.StackTrace}";
                EventLog.WriteEntry(eventSource, eventMessage, EventLogEntryType.Error, 1000);
            }
            catch
            {
            }
        }

        private void ShutdownGracefully()
        {
            try
            {
                Task.Delay(1000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Application.Current.Shutdown(1);
                    });
                });
            }
            catch
            {
                Environment.Exit(1);
            }
        }
    }
}