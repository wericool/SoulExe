using System.ComponentModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoulExe.Services;
using SoulExe.ViewModels;

namespace SoulExe;

public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public string BuildInfoDisplay => $"build {SoulExe.BuildInfo.BuildNumber} · {SoulExe.BuildInfo.BuildTime}";

    public MainWindow()
    {
        InitializeComponent();
        ApplyExecutableIconSafely();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void ApplyExecutableIconSafely()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/SoulExeWindowIcon.png", UriKind.Absolute));
            if (resource?.Stream is null) return;

            using (resource.Stream)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = resource.Stream;
                bitmap.EndInit();
                bitmap.Freeze();
                Icon = bitmap;
            }
        }
        catch (Exception exception)
        {
            AppLog.Write("Не удалось назначить иконку окна SoulExe", exception);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel = await MainViewModel.CreateAsync();
            DataContext = _viewModel;
        }
        catch (Exception exception)
        {
            AppLog.Write("Failed to initialise the main window.", exception);
            MessageBox.Show(
                $"Не удалось загрузить локальные данные SoulExe.\n\n{exception.Message}\n\nЖурнал: {AppLog.LogFilePath}",
                "SoulExe — ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel is not null)
        {
            await _viewModel.DisposeAsync();
            _viewModel = null;
            DataContext = null;
        }
    }

    // Сохранённый «нормальный» размер: ручная установка Width/Height в Maximized
    // затирает RestoreBounds, и после «свернуть из полноэкранного» окно остаётся огромным.
    private Rect _normalBounds = Rect.Empty;

    private void MinimizeWindowButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void ToggleMaximizeWindowButton_OnClick(object sender, RoutedEventArgs e) => ToggleWindowState();

    private void CloseWindowButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ToggleWindowState()
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            RestoreNormalBounds();
            return;
        }

        CaptureNormalBounds();
        WindowState = WindowState.Maximized;
    }

    private void CaptureNormalBounds()
    {
        // Пока окно в Normal — запоминаем актуальные границы (и для системного maximize).
        if (WindowState != WindowState.Normal) return;
        if (double.IsNaN(Left) || double.IsNaN(Top) || Width <= 0 || Height <= 0) return;
        _normalBounds = new Rect(Left, Top, Width, Height);
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (WindowState == WindowState.Normal) CaptureNormalBounds();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (WindowState == WindowState.Normal) CaptureNormalBounds();
    }

    private void RestoreNormalBounds()
    {
        ClearValue(MaxWidthProperty);
        ClearValue(MaxHeightProperty);
        if (_normalBounds.IsEmpty) return;

        // Принудительно возвращаем размер до развёртывания.
        Left = _normalBounds.X;
        Top = _normalBounds.Y;
        Width = Math.Max(MinWidth, _normalBounds.Width);
        Height = Math.Max(MinHeight, _normalBounds.Height);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Maximized)
        {
            // На всякий случай, если развернули системно (не только нашей кнопкой).
            if (_normalBounds.IsEmpty && RestoreBounds.Width > 0 && RestoreBounds.Height > 0)
                _normalBounds = RestoreBounds;
            return;
        }

        if (WindowState == WindowState.Normal)
        {
            ClearValue(MaxWidthProperty);
            ClearValue(MaxHeightProperty);
            // После Maximized Width/Height часто остаются равными экрану — возвращаем сохранённые.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(RestoreNormalBounds));
        }
    }

}
