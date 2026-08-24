using System.Windows;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private async Task SaveMobileAccessAsync()
    {
        if (string.IsNullOrEmpty(MobileAccessPassword)) return;
        _mobileAccessPasswordHash = MobileAccessPasswordHasher.Hash(MobileAccessPassword);
        _network.InvalidateSessions();
        await _store.MutateAsync(root =>
        {
            root.Preferences.MobileAccessUsername = string.IsNullOrWhiteSpace(MobileAccessUsername) ? "admin" : MobileAccessUsername;
            root.Preferences.MobileAccessPassword = "";
            root.Preferences.MobileAccessPasswordHash = _mobileAccessPasswordHash;
            root.Preferences.LocalWebServerEnabled = StartMobileServerOnLaunch;
        }, "save_mobile_access");
    }
    private async Task StartNetworkOnLaunchAsync()
    {
        if (!StartMobileServerOnLaunch) return;

        try
        {
            if (string.IsNullOrWhiteSpace(MobileAccessUsername) || string.IsNullOrEmpty(_mobileAccessPasswordHash))
            {
                Status = "Автозапуск мобильного сервера пропущен: укажите логин и пароль в настройках «Мобильный».";
                return;
            }

            await _network.StartAsync(MobileServerPort);
            Status = "Мобильный сервер запущен автоматически. Откройте адрес из раздела «Мобильный» на телефоне.";
            OnPropertyChanged(nameof(NetworkRunning));
            OnPropertyChanged(nameof(NetworkAccessUrl));
            OnPropertyChanged(nameof(NetworkAccessToken));
        }
        catch (Exception ex)
        {
            Status = $"Не удалось автоматически запустить мобильный сервер: {ex.Message}";
            OnPropertyChanged(nameof(NetworkRunning));
        }
    }
    private async Task ToggleNetworkAsync()
    {
        try
        {
            IsBusy = true;
            if (_network.IsRunning) { await _network.StopAsync(); Status = "Мобильный веб-клиент остановлен."; }
            else
            {
                if (string.IsNullOrWhiteSpace(MobileAccessUsername) || string.IsNullOrEmpty(MobileAccessPassword)) throw new InvalidOperationException("Укажите новый пароль для мобильного входа.");
                await SaveMobileAccessAsync();
                await _network.StartAsync(MobileServerPort);
                Status = "Мобильный веб-клиент запущен. Откройте адрес на телефоне и войдите по логину и паролю.";
            }
            OnPropertyChanged(nameof(NetworkRunning));
            OnPropertyChanged(nameof(NetworkAccessUrl));
            OnPropertyChanged(nameof(NetworkAccessToken));
        }
        catch (Exception ex) { HandleError("Не удалось изменить состояние веб-клиента", ex); }
        finally { IsBusy = false; }
    }
    private void CopyNetworkAddress()
    {
        try
        {
            Clipboard.SetText(NetworkAccessUrl);
            Status = "Сетевой адрес для телефона скопирован в буфер обмена.";
        }
        catch (Exception ex)
        {
            AppLog.Write("Не удалось скопировать сетевой адрес", ex);
            Status = "Не удалось скопировать адрес. Выделите его в поле и скопируйте вручную.";
        }
    }
    private static string GetLocalIp() => LocalNetworkInfo.GetPreferredIpv4();
}
