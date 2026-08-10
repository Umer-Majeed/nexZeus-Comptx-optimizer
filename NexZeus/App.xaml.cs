using System;
using System.Configuration;
using System.Data;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace NexZeus
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Catch-all so a crash writes to a log file the user can find and
            // send us, instead of the app just vanishing with no explanation.
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            Logger.PruneOldLogs();
            Logger.LogInfo("NexZeus started.");
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Logger.LogException(e.Exception, "UI thread (DispatcherUnhandledException)");

            bool keepRunning = ThemedMessageBox.Show(
                Current?.MainWindow,
                $"NexZeus hit an unexpected error and may be unstable:\n\n{e.Exception.Message}\n\n" +
                $"A detailed log was saved to:\n{Logger.LogFolderPath}\n\n" +
                "Try to keep running?",
                "Unexpected Error",
                ThemedMessageBoxIcon.Warning);

            // Mark as handled either way — if the user says no, we shut down cleanly
            // instead of letting the CLR force-kill the process.
            e.Handled = true;

            if (!keepRunning)
            {
                Shutdown();
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Logger.LogException(ex, "Non-UI thread (AppDomain.UnhandledException)");
            }
            else
            {
                Logger.LogError("Non-UI thread crash with non-Exception payload: " + e.ExceptionObject);
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Logger.LogException(e.Exception, "Unobserved Task Exception");
            e.SetObserved();
        }
    }
}