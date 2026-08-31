# Установка и первый запуск

## Вариант 1. Готовая Windows-сборка

1. Перейдите в [Releases](https://github.com/wericool/SoulExe/releases).
2. Скачайте `SoulExe-Windows-x64-*.zip` и распакуйте его в отдельную папку, где у вас есть права записи.
3. Запустите `SoulExe.exe`. При первом старте рядом будет создан каталог `SoulExeData`.
4. В разделе параметров настройте путь к `llama-server.exe` и GGUF-файлу модели или следуйте встроенному установочному сценарию.

Не запускайте EXE непосредственно из ZIP: распакуйте архив полностью, чтобы приложению было куда сохранять данные и аватары.

## Подключение телефона

1. Подключите телефон и ПК к одной Wi‑Fi/LAN-сети.
2. На ПК откройте **Параметры → Мобильный**.
3. Задайте логин и пароль, включите сервер либо опцию **«Автоматически включать сервер при запуске SoulExe»**.
4. Посмотрите показанный LAN-адрес, обычно вида `http://192.168.x.x:8000/`.
5. Установите APK из архива `soulexe-mobile-*.zip` либо откройте встроенный веб-клиент в браузере телефона.
6. В Mobile выберите поиск SoulExe в Wi‑Fi или укажите адрес ПК вручную, затем войдите с созданными данными.

Если соединение не устанавливается, убедитесь, что Windows Firewall разрешает SoulExe для частной сети, а телефон не подключён к гостевому Wi‑Fi с изоляцией клиентов.

## Сборка Windows из исходников

Требования: Windows, .NET 8 SDK и Windows SDK.

```powershell
git clone https://github.com/wericool/SoulExe.git
cd SoulExe
dotnet build SoulExe.csproj -c Release
```

Для самостоятельной self-contained публикации x64:

```powershell
dotnet publish SoulExe.csproj -c Release -r win-x64 --self-contained true
```

## Запуск Mobile из исходников

Требования: Node.js, pnpm и Android/Expo окружение.

```bash
git clone https://github.com/wericool/SoulExe.git
cd SoulExe/mobile
pnpm install
pnpm check
pnpm test
pnpm android
```

Текущий Mobile-клиент включает нативный Android-проект и собственный foreground service. Для release APK после `pnpm install` выполните:

```powershell
cd android
.\gradlew.bat assembleRelease
```

Перед публичной публикацией скопируйте `android/keystore.properties.example` в `android/keystore.properties` и заполните путь к закрытому ключу и пароли. Реальный ключ и этот локальный файл нельзя добавлять в Git.

## Проверка файлов релиза

В описании каждого GitHub Release указаны SHA-256 архивов. В PowerShell проверить загруженный файл можно так:

```powershell
Get-FileHash .\SoulExe-Windows-x64-v2.0.0.zip -Algorithm SHA256
```

Сравните полученную строку с контрольной суммой в релизе. Если суммы не совпадают, удалите файл и скачайте его повторно.
