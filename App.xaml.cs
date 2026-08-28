using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SoulExe.Services;

namespace SoulExe;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppLog.Write("SoulExe startup.");

        try
        {
            await AppServices.InitializeAsync();
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception exception)
        {
            AppLog.Write("Startup data recovery required.", exception);
            var choice = MessageBox.Show(
                $"SoulExe не смог безопасно открыть или перенести локальные данные. Исходный файл не был удалён.\n\n{exception.Message}\n\nДа — сохранить исходные данные и выйти.\nНет — создать новый пустой магазин (исходный файл будет сохранён в backups).\n\nЖурнал: {AppLog.LogFilePath}",
                "SoulExe — восстановление данных", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.No)
            {
                try
                {
                    await AppServices.DataStore.CreateNewStoreAfterRecoveryAsync();
                    var mainWindow = new MainWindow();
                    MainWindow = mainWindow;
                    mainWindow.Show();
                    return;
                }
                catch (Exception recoveryException)
                {
                    AppLog.Write("Recovery new-store creation failed.", recoveryException);
                    MessageBox.Show($"Не удалось создать новый магазин. Исходные данные сохранены.\n\n{recoveryException.Message}", "SoulExe — восстановление данных", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            Shutdown(-1);
        }
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            $"Приложение перехватило ошибку и останется открытым.\n\n{e.Exception.Message}\n\nЖурнал: {AppLog.LogFilePath}",
            "SoulExe — ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            AppLog.Write("Unhandled application exception.", exception);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Write("Unobserved background task exception.", e.Exception);
        e.SetObserved();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // WPF does not wait for an async OnExit handler. Block here so the
            // llama-server child process and local mobile server are actually
            // stopped before Windows tears the application down.
            if (MainWindow?.DataContext is ViewModels.MainViewModel viewModel)
                viewModel.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            AppLog.Write("Shutdown cleanup failed.", exception);
        }

        AppLog.Write("SoulExe exit.");
        base.OnExit(e);
    }
}
