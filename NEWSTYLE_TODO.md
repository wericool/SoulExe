# NewStyle — полный редизайн SoulExe (Handoff для новой сессии)

> Этот файл — точка входа для нового ИИ/сессии. Прочитай целиком до любых правок.
> Проект-оригинал не трогать: все изменения только здесь, в `Sources/NewStyle`.

---

## 0. Быстрая сводка

| Параметр | Значение |
|---|---|
| Задача | Полный редизайн внешнего вида приложения «Тёмный премиум» + двуязычность RU/EN |
| Папка редизайна | `E:\Games\backup_opencode\Sources\NewStyle` |
| Оригинал (НЕ ТРОГАТЬ) | `E:\Games\backup_opencode\Sources\desktop` |
| Git | В каждой папке свой репозиторий; история NewStyle продолжается от коммита оригинала |
| GitHub | https://github.com/wericool/SoulExe |
| Ветка текущей версии | `desktop-current` (push из Sources/desktop, master) |
| Ветка редизайна | `new-style` (push из Sources/NewStyle) |
| Тип проекта | WPF, .NET 8 (`net8.0-windows10.0.19041.0`), single-file self-contained |
| Выходные пути | `Sources/OutputNewStyle/*` (НЕ пересекаются с `Sources/Output` оригинала) |
| Состояние | сборка 0 warnings / 0 errors (`-warnaserror`), conversation checks passed |

Логин/коммиттер git локально задаётся флагами `-c user.name="Ericool" -c user.email="ericool@local"`.
Remote уже добавлен: `origin = https://github.com/wericool/SoulExe.git`. Push работает.

---

## 1. Что уже сделано (коммиты в ветке new-style)

### 1.1. Изоляция
- [x] `Sources/desktop`: git init + baseline-коммит `6ee9095`, ветка запушена как `desktop-current`.
- [x] Копия всей папки desktop → `Sources/NewStyle` (без bin/obj/.vs/.git), свой git.
- [x] История NewStyle перебазирована soft-reset'ом на `6ee9095`, поэтому на GitHub между ветками чистая диффа.
- [x] `SoulExe.csproj`: `BaseOutputPath` изменён `..\Output\` → `..\OutputNewStyle\` — сборки двух версий не затирают друг друга.
- [x] Namespace/AssemblyName оставлены `SoulExe` — вся логика (ViewModels, Services, Models) работает без изменений.

### 1.2. Дизайн-система v2 «Dark Premium» (база)
Файл `Styles/Themes/Dark.xaml` переписан полностью. Все ключи сохранены, поэтому все экраны компилируются и сразу получили новую палитру.

Принципы палитры:
```text
Слои (от тёмного к светлому): Window #08090E < Sidebar #0B0D14 < Panel #0F121B
                              < Card #141826 < Elevated #191E2E; Input темнее панели #0C0F17
Границы: тонкие low-contrast hairline (#20263A / strong #343D58), никаких жирных рамок
Акцент: индиго-фиолетовый #6D5AE8 (hover #8A78FF, soft #211E44)
        фирменный градиент AccentGradientBrush: #8B5CF6 -> #5B67F2 (135°)
Текст: primary #F5F6FB / secondary #9CA3B8 / muted #686F86 / dim #484E62
Статусы: success #2FC98C, danger #EF4668 (+hover варианты)
Выделение списков: ListSelectedBrush с акцентным подтоном (#232946)
```
- `Styles/Tokens.xaml`: радиусы подняты Small 10 / Default 14 / Large 18.
- `Styles/Controls.xaml`: галочка чекбокса перекрашена под новый фон (#08090E).
- Новый `CardShadowEffect` добавлен в тему (мягкая чёрная тень для карточек) — можно применять по экранам.

### 1.3. Локализация RU/EN (инфраструктура готова и работает)
Как устроено:
```text
Localization/Strings.ru.xaml и Strings.en.xaml -- скомпилированные ResourceDictionary
    со строковыми ключами (sys:String). Конвенция ключей:
    S.<Раздел>.<Элемент>[.Hint]   -- элементы интерфейса
    page.{route}.title|subtitle   -- заголовки страниц shell header
Services/LocalizationService.cs   -- ЧИСТЫЙ C# без WPF (важно: его компилирует
    ConversationChecks). Хранит таблицу строк, Tr(key, fallback), Normalize,
    событие LanguageChanged.
Services/LocalizationResourceLoader.cs -- WPF-часть: грузит pack://application:,,,/
    Localization/Strings.{lang}.xaml, кормит сервис, подменяет merged dictionary.
ViewModels/MainViewModel.Localization.cs -- partial VM: свойство AppLanguage
    ("ru"/"en"), сохранение в Preferences.Language через _store.MutateAsync
    ("save_language"), обновление PageTitle/PageSubtitle при смене языка.
AppNavigation.cs -- Title/Subtitle теперь LocalizationService.Tr по ключам
    page.{route}.*, русские свитчи остались fallback-ами.
```
Правила для продолжающих:
1. В XAML локализованные строки ТОЛЬКО через `{DynamicResource S.Key}` — тогда смена языка применяется живо, без перезапуска. StaticResource НЕ обновится.
2. AutomationProperties.Name/ToolTip тоже через DynamicResource.
3. C#-тексты (Status и т.п.) — `LocalizationService.Tr("S.Key", "русский fallback")`.
4. Fallback всегда русский и обязателен: до первой загрузки словаря UI не должен показывать ключи.
5. Новые строки добавлять СРАЗУ в оба файла ru/en.
6. Не включать в ConversationChecks ничего, что тянет System.Windows.

Уже переведено: окно (Title), sidebar (все пункты, секции, карточка МОДЕЛЬ, automation names, tooltips), заголовки/подзаголовки всех страниц shell header, статус-сообщения смены языка, карточка «Язык интерфейса» в Настройках.

Переключатель: Настройки → вкладка «Оформление» → первая карточка «Язык интерфейса» (ComboBox Русский/English, SelectedValue={Binding AppLanguage}). Применяется мгновенно, сохраняется в preferences (`AppPreferences.Language` уже существовал в схеме, default "ru").

### 1.4. Проверено
```powershell
cd E:\Games\backup_opencode\Sources\NewStyle
dotnet build SoulExe.csproj -warnaserror          # 0/0
dotnet run --project SoulExe.ConversationChecks\SoulExe.ConversationChecks.csproj
dotnet publish SoulExe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# EXE: E:\Games\backup_opencode\Sources\OutputNewStyle\Release\win-x64\publish\SoulExe.exe
```
Ручная проверка в EXE пользователем: палитра применилась ко всем экранам, переключатель языка меняет sidebar/заголовки/Title окна на лету.

---

## 2. Дорожная карта редизайна (по экрану за шаг)

Обязательный цикл каждого шага (как в оригинале):
```text
1. Один экран (или один компонент).
2. Только локальные keyed стили. ЗАПРЕЩЕНЫ global implicit templates
   (ScrollViewer/ContentControl/ListBox/Grid/Border/UserControl) --
   см. WPF_UI_RULES.md раздел 3, это был реальный инцидент пустого UI.
3. Каждый UserControl обязан иметь .xaml.cs с конструктором InitializeComponent()
   (проверка скриптом из WPF_UI_RULES.md раздел 2).
4. dotnet build -warnaserror -> checks -> publish -> РУЧНАЯ проверка в EXE -> git commit+push.
5. Строки экрана при переделке сразу выносить в Strings.ru/en.xaml (DynamicResource).
```

Порядок фаз (каждая = отдельная сессия/несколько шагов):

- [ ] **Фаза A — Shell**: `Views/AppShellView.xaml`, `Views/NavigationView.xaml`, `Views/TitleBarView.xaml`, `MainWindow.xaml`, `Views/StatusView.xaml`.
  Цель вида: узкий премиальный sidebar с крупными иконками Segoe MDL2, активный пункт — мягкая акцентная подложка + вертикальная полоска-акцент; header прозрачнее, без рамки снизу, заголовок 22px; статус-бар компактнее. Карточку МОДЕЛЬ сделать единой капсулой с прогрессом загрузки.
- [ ] **Фаза B — Library** (`Views/LibraryView.xaml`, ~800 строк): сетка карточек персонажей/лорбуков/персон с hover-elevation (CardShadowEffect), крупные обложки, аккуратные бейджи, единый ритм 8/12/16/24.
- [ ] **Фаза C — Chat workspace**: `Controls/ConversationListView`, `PersonalConversationThreadView`, `GroupConversationThreadView`, `ConversationComposerView`, `ConversationDetailsPanel`, `Views/ChatWorkspaceView`.
  Цель: мессенджер-вид — пузыри с раздельными хвостами/радиусами, группировка подряд идущих сообщений одного автора (если дастся без изменения VM — иначе отложить), composer-капсула, аккуратные drawers.
- [ ] **Фаза D — Characters editor** (`Views/CharactersView.xaml`): секции Info/Memory/Lore, липкий toolbar.
- [ ] **Фаза E — Settings + Mobile** (`Views/SettingsView.xaml`, `Views/MobileAccessView.xaml`): карточки секций с иконками, язык/оформление/runtime.
- [ ] **Фаза F — Models/Gateway** (`Views/ModelsView.xaml`, `Views/GatewayView.xaml`): карточки каталогов, состояния loading/empty/error в новом языке.
- [ ] **Фаза G — Setup overlay** (`Views/SetupView.xaml`): онбординг в стиле premium wizard.
- [ ] **Фаза H — Локализация остатков**: после каждой фазы переводить её строки; в конце пройтись grep по кириллице в Views/Controls и добить VM-тексты (Status, ModelStartStopText, GatewayCategorySubtitle и т.п.) через Tr.

## 3. Локализация — оставшиеся работы (детальный список)

```text
[ ] LibraryView: тулбар (Персонажи/Лор/Персоны, Импорт, сортировка), empty states, диалоги создания/удаления, редактор лорбука, редактор персоны
[ ] ChatWorkspaceView: 4 overlay-диалога, hints composer'а
[ ] ConversationListView/Composer/Thread/Details: все подписи, меню (Pin/Rename/Delete...), typing indicator
[ ] CharactersView, SetupView, ModelsView, GatewayView, SettingsView (кроме языковой карточки), MobileAccessView, StatusView
[ ] ViewModels: Status-строки, ModelStartStopText, SceneStartPauseText, PinMenuText,
    GatewayCategoryTitle/Subtitle, PendingDeletion.Title/Description/Warning,
    CharacterCreation тексты -- все генерируемые в C# строки через LocalizationService.Tr
[ ] Решить: MessageBox-тексты восстановления в App.xaml.cs (можно оставить RU)
```

## 4. Правила безопасности (из оригинального проекта, обязательны)

Полные версии: `WPF_UI_RULES.md`, `UI_REDESIGN_TODO.md`, `MEMORY_GROWTH_DIAGNOSTICS.md` (лежат в этой папке, скопированы с оригинала).

Критичные запреты:
1. НЕ создавать implicit (без x:Key) Style с заменой Template у ScrollViewer, ContentControl, ItemsPresenter, ListBox, ComboBox, Grid, Border, UserControl.
2. НЕ создавать UserControl без *.xaml.cs.
3. НЕ переносить один WPF-элемент между host'ами в runtime без ручной проверки (drawer-инцидент).
4. НЕ менять несколько экранов за один шаг.
5. НЕ переносить ActualWidth/drawer state/scroll/focus в MainViewModel.
6. Не трогать порядок merge в App.xaml без runtime smoke-test.
7. Transcript чата: virtualized ListBox + recycling, presentation window 60, auto-follow — поведение не ломать.
8. Persistence: mutation через snapshot→mutate→persist→restore-on-error (JsonDataStore).

## 5. Команды

```powershell
cd E:\Games\backup_opencode\Sources\NewStyle
dotnet build SoulExe.csproj -warnaserror
dotnet run --project SoulExe.ConversationChecks\SoulExe.ConversationChecks.csproj
dotnet publish SoulExe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
git add -A; git -c user.name="Ericool" -c user.email="ericool@local" commit -m "<сообщение>"; git push origin new-style
```

EXE для проверки: `E:\Games\backup_opencode\Sources\OutputNewStyle\Release\win-x64\publish\SoulExe.exe`
(данные пользователей те же SoulExeData — обе версии читают одно хранилище).

## 6. Контроль качества перед передачей EXE

```text
[ ] build -warnaserror: 0/0
[ ] conversation checks passed
[ ] code-behind есть у всех .xaml в Views/ Controls/
[ ] нет literal-цветов там, где есть семантический brush темы
[ ] новые строки заведены в ОБОИХ Strings.{ru,en}.xaml
[ ] переключение языка в EXE: sidebar/header/текущий экран обновились без рестарта
[ ] publish собран, путь указан, названы экраны для ручной проверки
[ ] git push origin new-style выполнен
```

## 7. Если новая сессия начинает отсюда

1. Прочитай этот файл + `WPF_UI_RULES.md`.
2. Убедись `git log --oneline` и `git status` чистые; синхронизируйся с веткой new-style.
3. Возьми следующую незакрытую фазу из раздела 2 и работай малыми шагами по циклу.
4. После подтверждения пользователем — коммит + push + отметь пункт [x] здесь.
