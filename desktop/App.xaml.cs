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
            AppLog.Write("Fatal startup initialisation error.", exception);
            MessageBox.Show(
                $"Не удалось подготовить локальное хранилище SoulExe.\n\n{exception.Message}\n\nЖурнал: {AppLog.LogFilePath}",
                "SoulExe — ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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

    protected override async void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is ViewModels.MainViewModel viewModel)
            await viewModel.DisposeAsync();

        AppLog.Write("SoulExe exit.");
        base.OnExit(e);
    }
}
