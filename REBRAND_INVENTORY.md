# Инвентаризация ребрендинга SoulText → SoulExe

## Можно переименовать механически

- Внутренний .NET namespace `SoulTextWpf` в исходниках, XAML и `RootNamespace` проекта. Выполнено: namespace и файл проекта переименованы в `SoulExe`.
- Внутренние mobile-типы, классы и файлы `SoulTextApi`, `SoulTextSession`, `soultext-api.ts` и связанные импорты. Выполнено: `SoulExeApiClient`, `SoulExeSession`, `soulexe-api.ts` и связанные модули.
- Устаревшие заголовки, комментарии и TODO, если они не являются частью backward compatibility.

Это отдельная механическая правка с полной сборкой desktop и TypeScript-проверкой mobile.

## Оставить как compatibility alias

- Папка и файлы `SoulTextData`, `soultext.json`, `soultext.db` — только для переноса в `SoulExeData` и новые имена.
- HTTP-заголовок `X-SoulText-Session` — принимать наряду с `X-SoulExe-Session` на один переходный релиз.
- `health.service === "SoulText"` — принимать для подключения к старому desktop-серверу.
- Старый ключ secure storage `soultext.mobile.session.v1` — прочитать один раз, перенести в новый ключ и затем очистить. Реализовано в mobile; для нового native package старое защищённое хранилище ОС недоступно, поэтому повторный вход в приложение ожидаем.

## Согласованные изменения распространения

- Пользователь разрешил сменить Android package / iOS bundle ID и Expo slug, понимая, что это создаст новое приложение. Теперь используются `com.app.soulexemobile`, `soulexe-mobile` и deep-link scheme `soulexe`.

## Пользовательские остатки

В основном desktop-интерфейсе уже используется SoulExe. Остаются исторические TODO, комментарии и упоминания в release-описании; их можно заменить после механического namespace-шага, сохранив только явно помеченные compatibility aliases.
