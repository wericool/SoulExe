using System.Windows.Input;
using System.Windows;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public interface IRaiseCanExecute
{
    void RaiseCanExecuteChanged();
}

public sealed class RelayCommand : ICommand, IRaiseCanExecute
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand : ICommand, IRaiseCanExecute
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _busy;
    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_busy && (_canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _busy = true;
        RaiseCanExecuteChanged();
        try { await _execute(parameter); }
        catch (OperationCanceledException)
        {
            // Отмена пользователем — штатный исход, без аварийного сообщения.
        }
        catch (Exception ex)
        {
            AppLog.Write("Неперехваченная ошибка асинхронной команды", ex);
            MessageBox.Show(
                "Не удалось выполнить действие. Приложение продолжает работать. Подробности записаны в журнал SoulExe.",
                "SoulExe — действие не выполнено",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally { _busy = false; RaiseCanExecuteChanged(); }
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
