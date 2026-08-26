using System.Windows;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private async Task<bool> SaveMobileAccessAsync()
    {
        if (!string.IsNullOrEmpty(MobileAccessPassword) && string.IsNullOrWhiteSpace(MobileAccessPassword))
        {
            Status = "Пароль для мобильного входа не может состоять только из пробелов.";
            return false;
        }

        if (string.IsNullOrEmpty(MobileAccessPassword) && string.IsNullOrEmpty(_mobileAccessPasswordHash))
        {
            Status = "Задайте пароль для мобильного входа.";
            return false;
        }

        try
        {
            var passwordChanged = !string.IsNullOrEmpty(MobileAccessPassword) && !MobileAccessPasswordHasher.Verify(MobileAccessPassword, _mobileAccessPasswordHash);
            if (passwordChanged)
            {
                _mobileAccessPasswordHash = MobileAccessPasswordHasher.Hash(MobileAccessPassword);
                _network.InvalidateSessions();
            }

            var username = string.IsNullOrWhiteSpace(MobileAccessUsername) ? "admin" : MobileAccessUsername;
            await _store.MutateAsync(root =>
            {
                root.Preferences.MobileAccessUsername = username;
                root.Preferences.MobileAccessPassword = "";
                root.Preferences.MobileAccessPasswordHash = _mobileAccessPasswordHash;
                root.Preferences.LocalWebServerEnabled = StartMobileServerOnLaunch;
            }, "save_mobile_access");
            MobileAccessUsername = username;
            Status = passwordChanged ? "Пароль мобильного входа изменён; активные сессии завершены." : "Настройки мобильного доступа сохранены.";
            return true;
        }
        catch (Exception ex)
        {
            HandleError("Не удалось сохранить настройки мобильного доступа", ex);
            return false;
        }
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
                if (!await SaveMobileAccessAsync()) return;
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
