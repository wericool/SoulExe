using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private async Task StartFromSetupAsync()
    {
        await StartModelAsync();
        if (!_llama.IsStartedByApplication) return;
        await FinishInitialSetupAsync();
        CurrentPage = "Chat";
        Status = "Локальная модель запущена. Открыт чат.";
    }
    private async Task FinishInitialSetupAsync()
    {
        try
        {
            await _store.MutateAsync(root => root.Preferences.InitialSetupCompleted = true, "complete_initial_setup");
            IsInitialSetupVisible = false;
            CurrentPage = "Home";
            Status = string.IsNullOrWhiteSpace(ModelPath)
                ? "Начальная настройка закрыта. Движок и модель можно установить позже в Models Hub."
                : "Начальная настройка завершена. Можно выбрать персонажа и начать чат.";
        }
        catch (Exception ex) { HandleError("Не удалось завершить начальную настройку", ex); }
    }
}
