# UI Redesign — Handoff / Current State

> Этот файл предназначен для передачи проекта в новую сессию.
> Прочитай его целиком до любых изменений кода.

---

## 0. Быстрая сводка

| Параметр | Значение |
|---|---|
| Проект | `E:\Games\backup_opencode\Sources\desktop\SoulExe.csproj` |
| Тип | WPF, .NET 8 (`net8.0-windows10.0.19041.0`) |
| Ветка сравнения | `https://github.com/wericool/SoulExe/tree/test/mobile-desktop/desktop` |
| Состояние сборки | успешно, `0` warnings, `0` errors (с `-warnaserror`) |
| Состояние checks | `Conversation fixture checks passed.` |
| Состояние UI | все экраны отображаются, подтверждено пользователем |
| Текущий этап | UI-полировка малыми шагами, по одному экрану |
| Последний выполненный шаг | Шаг 7 — Models / Gateway / Chip / StatusView. ПОЛировка ВСЕХ 7 шагов завершена |
| Ожидается | подтверждено пользователем; новых шагов полировки нет (см. раздел 6.2) |

Обязательные к прочтению документы:

```text
desktop/WPF_UI_RULES.md               -- правила WPF UI, читать ДО правок XAML
desktop/MEMORY_GROWTH_DIAGNOSTICS.md  -- методика проверки роста памяти
desktop/CONVERSATION_THREAD_ANALYSIS.md -- решение по personal/group чату
desktop/UI_REDESIGN_TODO.md           -- этот файл
```

---

## 1. ГЛАВНОЕ: два найденных root cause

Эти две находки объясняют весь инцидент «половина интерфейса не отображается». Не повторяй их.

### 1.1. Отсутствующий code-behind у UserControl

**Симптом:** контрол существует, сборка успешна, XAML валиден, bindings корректны, но контрол рендерится **полностью пустым**.

**Причина:** у `UserControl` с `x:Class` не было файла `*.xaml.cs`. WPF генерирует `InitializeComponent()` в `*.g.cs`, но вызвать его должен конструктор в пользовательском файле. Без него C# создаёт пустой конструктор по умолчанию, и XAML-дерево не загружается.

**Затронутые файлы (исправлено):**

```text
Views/NavigationView.xaml.cs   -- отсутствовал  -> пустой sidebar
Views/CharactersView.xaml.cs   -- отсутствовал  -> пустой редактор персонажа
Views/StatusView.xaml.cs       -- отсутствовал  -> статус-бар высотой 0
```

**Обязательный шаблон:**

```csharp
using System.Windows.Controls;

namespace SoulExe.Views;

public partial class NavigationView : UserControl
{
    public NavigationView() => InitializeComponent();
}
```

**Обязательная проверка после создания/переноса любого View или Control:**

```powershell
$dirs = @("Views","Controls")
foreach ($d in $dirs) {
  $xaml = Get-ChildItem -LiteralPath $d -Filter *.xaml | ForEach-Object { $_.BaseName }
  $cs = Get-ChildItem -LiteralPath $d -Filter *.xaml.cs | ForEach-Object { $_.Name -replace '\.xaml\.cs$','' }
  "== $d MISSING CODEBEHIND =="
  Compare-Object -ReferenceObject $xaml -DifferenceObject $cs | Where-Object { $_.SideIndicator -eq '<=' } | ForEach-Object { $_.InputObject }
}
```

Результат обязан быть пустым.

### 1.2. Глобальный custom template у ScrollViewer

**Симптом:** content внутри вложенных layout не отображается, ошибок компиляции нет.

**Причина:** в `Styles/Controls.xaml` был implicit `<Style TargetType="ScrollViewer">` с полной подменой `ControlTemplate`. Он применялся ко всему приложению: sidebar, редактор персонажа, библиотека, настройки, setup, списки.

**Исправлено:** возвращён штатный platform template. Осталось только безопасное:

```xaml
<Style TargetType="ScrollViewer">
    <Setter Property="Background" Value="Transparent" />
</Style>
```

**Правило:** кастомизировать `ScrollViewer` только через keyed feature-специфичные стили, например существующий `ConversationMessageScroller`.

---

## 2. Что запрещено делать

Список выведен из реальных инцидентов, а не из теории.

```text
1. НЕ создавать implicit (без x:Key) Style с подменой Template для:
   ScrollViewer, ContentControl, ItemsPresenter, ListBox, ComboBox,
   Grid, Border, UserControl.

2. НЕ создавать UserControl без файла *.xaml.cs с конструктором.

3. НЕ переносить один и тот же WPF-элемент между разными host в runtime
   (reparenting) без ручной проверки в EXE. Так был сломан sidebar drawer.

4. НЕ создавать два экземпляра одного редактируемого control ради responsive.

5. НЕ переносить в MainViewModel: ActualWidth, scroll position,
   focus, drawer open state, responsive mode.

6. НЕ менять несколько экранов за один шаг.

7. НЕ считать VirtualMemorySize показателем утечки памяти.

8. НЕ отключать Cognitive Architecture / Soul Memory / Auto Summary
   без измерений по MEMORY_GROWTH_DIAGNOSTICS.md.

9. НЕ возвращать монолитный App.xaml и монолитный MainWindow.xaml.

10. НЕ добавлять literal-цвета, если есть семантический ресурс темы.
```

---

## 3. Текущая архитектура

```text
MainWindow.xaml                  -- тонкий host: WindowChrome + AppShellView
└─ Views/AppShellView.xaml       -- shell: title bar, sidebar, header, page host, status, overlay
   ├─ Views/TitleBarView         -- системные кнопки окна
   ├─ Views/NavigationView       -- sidebar (отдельный компонент, имеет code-behind)
   ├─ ContentControl PageHost    -- lazy page host с кэшем
   │  ├─ Views/LibraryView       -- Home
   │  ├─ Views/ChatWorkspaceView
   │  ├─ Views/GatewayView
   │  ├─ Views/SetupView
   │  ├─ Views/ModelsView
   │  ├─ Views/SettingsView      -- содержит встроенный MobileAccessView
   │  └─ Views/CharactersView    -- НЕ кэшируется, создаётся заново на каждый вход
   ├─ Views/StatusView
   └─ Initial setup overlay      -- отдельный экземпляр SetupView, ZIndex 100
```

### Особенности page host

- Страницы создаются лениво при первом открытии и кэшируются.
- `DataContext` назначается явно перед attach: не полагаться на inherited context после detach/reattach.
- `Characters` route не кэшируется: редактор зависит от свежего `SelectedCharacter`.
- `Mobile` route канонизирован: `NavigateTo("Mobile")` → выбирает вкладку `mobile` → `CurrentPage = "Options"`.

### Дизайн-система

```text
Styles/Themes/Dark.xaml        -- конкретная палитра, legacy brush keys
Styles/Colors.xaml             -- семантические алиасы Brush.*, сам мерджит тему
Styles/Tokens.xaml             -- spacing, radii, heights
Styles/Typography.xaml         -- текстовые стили
Styles/Buttons.xaml            -- кнопки, WindowChromeButton, PseudoTabButton
Styles/Inputs.xaml             -- поля ввода, ComboBox, composer input
Styles/Controls.xaml           -- общие контролы (БЕЗ рискованных global layout templates)
Styles/Cards.xaml              -- поверхности, карточки, Chip, AvatarCircle
Styles/Layout.xaml             -- NavButton и Nav* selected-state стили
Styles/ConversationStyles.xaml -- стили чата, ConversationMessageScroller
```

Порядок merge в `App.xaml` важен и не должен меняться без проверки:

```text
Dark -> Colors -> Tokens -> Typography -> Buttons -> Inputs -> Controls -> Cards -> Layout -> ConversationStyles
```

---

## 4. Что уже выполнено

### Этап 1 — декомпозиция

- [x] `MainWindow.xaml` сокращён с ~3675 строк до тонкого host.
- [x] Созданы отдельные Views: Setup, Library, ChatWorkspace, Characters, Gateway, Models, Settings, MobileAccess.
- [x] UI scroll/search логика чата перенесена в `ChatWorkspaceView.xaml.cs`.
- [x] PasswordBox handler перенесён в `MobileAccessView.xaml.cs`.
- [x] Из проекта исключён из компиляции резервный `MainWindow.original.xaml`.

### Этап 2 — дизайн-система

- [x] Монолитный `App.xaml` разделён на тематические словари.
- [x] Сохранены все legacy resource keys и implicit control styles.
- [x] `App.xaml` содержит только merged dictionaries.

### Этап 3 — Application Shell

- [x] Создан `AppShellView` с header, navigation, page host, status, overlay.
- [x] Window actions маршрутизируются из `TitleBarView` к окну.
- [x] Все существующие маршруты сохранены.
- [x] Восстановлены потерянные navigation-стили `NavButton`, `NavHome`, `NavChat`, `NavScene`, `NavCharacters`, `NavGateway`, `NavSetup`, `NavModels`, `NavMobile`, `NavOptions` в `Styles/Layout.xaml`.
- [x] Восстановлены `WindowChromeButton` и `WindowCloseButton` в `Styles/Buttons.xaml`.
- [x] Lazy page host с кэшем и явным DataContext.

### Этап 4 — Chat Workspace

- [x] `ConversationDetailsPanel` как presentation host для personal/group details.
- [x] Единый active-thread host `ConversationThreadView`: materializуется только активный режим.
- [x] Исправлен group search: `SelectedSceneMessageSearchResult` теперь устанавливается корректно.
- [x] Убран лимит 30 сообщений в personal и полная materialization в group; введено presentation window `60` + «Загрузить предыдущие сообщения».
- [x] Transcript переведён на virtualized `ListBox` с recycling.
- [x] Auto-follow условный: не сбивает чтение истории, есть кнопка «Новые сообщения».
- [x] Coalescing dispatcher scroll requests, очистка подписок scene messages.
- [x] Responsive chat: wide / medium / narrow с details drawer (локальный state во view).
- [x] Composer: personal `Enter` = отправка, group `Ctrl+Enter` = отправка, подсказки и automation labels.

### Этап 5 — остальные экраны

- [x] Setup: общий scroll region, закреплённый footer, доступ к `SkipInitialSetupCommand`, compact режим.
- [x] Library: единый toolbar, contextual primary action, настоящий empty state персонажей, доступная add-card, CTA в empty states лора и персон.
- [x] Characters: empty state, единый экспорт, группировка Info/Memory/Lore.
- [x] Gateway: toolbar, loading/empty/error states, selected-state карточек, empty details, responsive.
- [x] Models: постоянная карточка runtime со start/stop и launch diagnostics, empty state установленных, error state каталога, responsive.
- [x] Settings: разделы Runtime / Оформление / Мобильный, Mobile Access встроен во вкладку, устранена подмена страницы в shell.

### Этап 6 — полировка малыми шагами

- [x] Шаг 1: sidebar вынесен в отдельный `NavigationView` с обязательным code-behind.
- [x] Шаг 2: полировка sidebar. Подтверждено пользователем: 6 пунктов, активное выделение, читаемый текст, блок МОДЕЛЬ, иконки `Segoe MDL2 Assets` рисуются корректно.
- [x] Шаг 3: header приложения в `Views/AppShellView.xaml`. ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ.
  - Padding приведён к `24,16`, теперь заголовок выровнен с `PageHost` (`Margin="24,16,24,20"`); раньше было `28` против `24`.
  - `MinHeight="88"` — высота header не зависит от длины подзаголовка.
  - `DockPanel` заменён на `Grid` с колонками `*` / `Auto`: колонка заголовка измерима, поэтому длинный `PageSubtitle` обрезается через `TextTrimming` вместо срезания по краю. Проверено на 900 px и на минимальной ширине окна 1080 px.
  - Chip состояния: локальный `ShellHeaderChip` (BasedOn `Chip`) + точка состояния `ShellHeaderStateDot` (`TextDimBrush` → `SuccessBrush` при `IsModelRunning`), `AutomationProperties.Name`, `ToolTip`.
  - Chip больше не растягивается на всю высоту header: добавлен `VerticalAlignment="Center"` (раньше текст пилюли прилипал к верху).
  - Фон header — `PanelBrush`: header читается как chrome приложения, а не как часть прокручиваемого содержимого.
  - Подзаголовок в header получил `TextSecondaryBrush` вместо `TextMutedBrush`: `#6A7288` на `#12151F` даёт ~3.6:1, что ниже AA для 13 px. Общий стиль `PageHeaderSubtitle` не менялся, чтобы не задеть `ModelsView`.
  - `AppNavigation.Title`: `ХАБ` → `Хаб`, `ГРУППОВОЙ РАЗГОВОР` → `Групповой разговор`. `Title`/`Subtitle` потребляются только `PageTitle`/`PageSubtitle`, то есть только header.
- [x] Шаг 4: Библиотека в `Views/LibraryView.xaml`. Изменён только этот экран (+ аддитивные токены темы).
  - Единый ритм сетки: гаттер карточек лорбуков `14` → `16` (как у персонажей и персон); отступы тулбара приведены к шагу 8/12.
  - Вкладка «Персонажи» получила заголовок секции «Карточки персонажей» + подзаголовок — паритет с «Загруженные лоры» и «Персоны пользователя».
  - Единый мета-бейдж `LibraryMetaChip` (локальный keyed стиль, радиус `9`, не `999`): счётчик «Записей: N» на карточке лорбука (был `TextMutedBrush` 10 px без фона) и бейдж «Персона пользователя» на карточке персоны.
  - Literal `#D6DAEA` (подписи поверх scrim) заменён новым семантическим токеном: `CardScrimTextColor`/`CardScrimTextBrush` в `Themes/Dark.xaml` + алиас `Brush.CardScrimText` в `Colors.xaml`. Значение цвета не менялось, только способ ссылки.
  - Дедупликация: scrim-градиент карточек и оверлей hover-действий вынесены в локальные keyed ресурсы `LibraryCardScrimGradient` и `LibraryCardActionsOverlayBrush` в `UserControl.Resources` (значения не менялись).
  - Карточка персоны: добавлен `IsKeyboardFocusWithin` триггер оверлея действий — паритет с карточкой персонажа (клавиатурная навигация теперь раскрывает действия).
  - НЕ тронуто: shell, sidebar, header, `StatusView`, overlay-диалоги библиотеки, логика вкладок, empty-state триггеры (`HomeCards.Count == 1` корректен: VM всегда добавляет add-card последним элементом).
- [x] Шаг 5: Chat Workspace визуал. Изменены только `Controls/PersonalConversationThreadView.xaml`, `Controls/GroupConversationThreadView.xaml`, `Controls/ConversationListView.xaml`.
  - Разделитель дат в личном чате: `CornerRadius="999"` → `"11"` (тот же класс дефекта «эллипс вместо пилюли», что и `Chip` на Шаге 3).
  - Empty state ленты: при `Messages.Count == 0` / `SceneMessages.Count == 0` показывается центрированная подсказка (локальные стили-триггеры, без изменений VM). Исчезает с первым сообщением.
  - Список диалогов: заголовок «Диалоги» `Margin 18,18,18,14` → `16,16,16,12`, выровнен с строкой поиска по сетке 16.
  - НЕ тронуто: virtualized transcript, auto-follow, presentation window, подписки, responsive breakpoints, композер, поиск, overlay-диалоги чата.
- [x] Шаг 5a (функциональный фикс по feedback пользователя): сообщения пользователя не выравнивались вправо. ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ.
  - Причина: `ListBoxItem` в transcript имеет дефолтный `HorizontalContentAlignment=Left`; ContentPresenter упаковывал корневой `StackPanel` шаблона по ширине пузыря, поэтому триггер `IsUser → HorizontalAlignment=Right` не имел визуального эффекта.
  - Фикс: `<Setter Property="HorizontalContentAlignment" Value="Stretch" />` в ItemContainerStyle обоих тредов (`PersonalConversationThreadView`, `GroupConversationThreadView`). Теперь работают все выравнивания: user/IsFirstCharacter вправо, director/user-participant по центру.
  - Проверить в EXE: свои сообщения справа, персонажа слева; в групповом -- первый персонаж справа, второй слева, режиссёрские/пользовательские реплики по центру.

### Дефекты дизайн-системы, найденные и исправленные на Шаге 3

- [x] **`Styles/Colors.xaml` не работал целиком.** `<Color x:Key="Color.Canvas">{StaticResource WindowColor}</Color>` парсится как литеральная строка, поэтому любое обращение к `Color.*` и `Brush.*` бросало `XamlParseException`. Приложение не падало только потому, что ни один экран ещё не использовал семантические ключи. Проверено на скомпилированной сборке через pack-URI словари.
  - Промежуточный слой `Color.*` удалён: алиас Color в WPF выразить нельзя без дублирования палитры.
  - `Brush.*` заданы через `Color="{StaticResource <ThemeColor>}"` в позиции атрибута.
  - `Colors.xaml` сам мерджит `Themes/Dark.xaml`: значение кисти реализуется при первом обращении к ключу и видит только свой словарь и его merged-словари, но не соседей из `App.xaml`. Отложенные `Setter.Value` соседей видят, поэтому стили работали, а кисти — нет.
- [x] **`CornerRadius="999"` даёт эллипс, а не пилюлю.** `Border` не ограничивает радиус половиной высоты: короткий широкий прямоугольник превращается в овал. В header радиус зафиксирован (`14`). Стиль `Chip` остался с `999` — см. раздел 10.

### Функциональные исправления, найденные по ходу

- [x] Startup recovery schema v8 → v10: валидированный permanent backup, явный legacy-парсинг, in-memory валидация, temp round-trip, атомарная замена, recovery-диалог без потери данных.
- [x] `ChatAppearance` сохраняется полностью через `SaveChatAppearanceCommand`.
- [x] Исправлена семантика подписи MMAP.
- [x] Mobile: логин/автозапуск сохраняются без повторного ввода пароля; пробельный пароль отклоняется; сессии инвалидируются при смене пароля.
- [x] `TopK` и `RepeatPenalty` сохраняются и доходят до runtime llama.cpp.
- [x] `JsonDataStore` откатывает in-memory root при ошибке записи.
- [x] Устранён двойной `DisposeAsync` у `MainViewModel`.
- [x] Initial setup снова показывается новым пользователям.
- [x] Подтверждения удаления: разговор, сцена, сообщение, лорбук, запись лора; snapshot ID вместо mutable VM.
- [x] Удаление неактивной сцены больше не останавливает таймер активной.
- [x] `ConversationPaging` безопасно отклоняет некорректный cursor.
- [x] Accessibility: focus-visible состояния, automation names, pseudo-tab семантика, modal focus trap / Escape / focus restoration.
- [x] Memory observability: `MEMORY_SNAPSHOT` каждые 60 c, TTL/cap для mobile сессий, ротация `SoulExe.log`.

---

## 5. Текущий рабочий процесс: малые шаги

Согласованный с пользователем режим работы.

```text
Шаг 1  [x] sidebar вынесен в отдельный NavigationView
Шаг 2  [x] полировка sidebar -- ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ
Шаг 3  [x] header приложения (в AppShellView) -- ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ
Шаг 4  [x] Библиотека (LibraryView) -- ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ
Шаг 5  [x] Chat Workspace визуал -- ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ
Шаг 5a [x] фикс выравнивания сообщений (user вправо) -- ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ
Шаг 6  [x] Настройки -- ОЖИДАЕТ ПОДТВЕРЖДЕНИЯ ПОЛЬЗОВАТЕЛЕМ
Шаг 7  [x] Models / Gateway / Chip / Status -- ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ. ПОЛИРОВКА ЗАВЕРШЕНА
```

### Обязательный цикл каждого шага

```text
1. Меняем ОДИН экран или ОДИН компонент.
2. Только локальные keyed стили. Никаких global implicit templates.
3. Проверяем наличие code-behind у всех .xaml.
4. dotnet build SoulExe.csproj --no-restore -warnaserror
5. dotnet run --project SoulExe.ConversationChecks\SoulExe.ConversationChecks.csproj --no-restore
6. dotnet publish (Release, self-contained, single file)
7. Пользователь вручную проверяет ИМЕННО этот экран в EXE.
8. Только после подтверждения переходим к следующему шагу.
```

Команды:

```powershell
cd E:\Games\backup_opencode\Sources\desktop
dotnet build SoulExe.csproj --no-restore -warnaserror
dotnet run --project SoulExe.ConversationChecks\SoulExe.ConversationChecks.csproj --no-restore
dotnet publish SoulExe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Итоговый EXE:

```text
E:\Games\backup_opencode\Sources\Output\Release\win-x64\publish\SoulExe.exe
```

---

## 6. СЛЕДУЮЩЕЕ ДЕЙСТВИЕ для новой сессии

### 6.1. Полировка завершена -- Шаги 1-7 ПОДТВЕРЖДЕНЫ ПОЛЬЗОВАТЕЛЕМ В EXE

Новых обязательных шагов нет. Проверенный релиз:

```text
E:\Games\backup_opencode\Sources\Output\Release\win-x64\publish\SoulExe.exe
```

Перед передачей пользователям прогнать контроль качества из раздела 9.

### 6.2. Возможные направления дальше (опционально, не запланировано)

Из отложенного (раздел 8, п. 5): adaptive navigation drawer как отдельный host,
унификация personal/group thread-инфраструктуры по
`CONVERSATION_THREAD_ANALYSIS.md`, замена оставшихся literal-цветов токенами,
виртуализация карточных каталогов Library/Gateway (WrapPanel не виртуализуется).

### 6.3. Закрытые Шаги 3-7 (справка)

- Шаг 3 — header приложения: изменены `Views/AppShellView.xaml`, `ViewModels/AppNavigation.cs`, `Styles/Colors.xaml`. ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ В EXE.
- Шаг 4 — Библиотека: изменены `Views/LibraryView.xaml` (+ аддитивно токен `CardScrimText` в теме). ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ В EXE.
- Шаг 5/5a — Chat Workspace визуал + выравнивание сообщений: изменены `Controls/PersonalConversationThreadView.xaml`, `Controls/GroupConversationThreadView.xaml`, `Controls/ConversationListView.xaml`. ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ В EXE.
  - Ключевой фикс 5a: `HorizontalContentAlignment=Stretch` в ItemContainerStyle transcript — без него триггеры выравнивания (`IsUser` вправо, director по центру) не имели эффекта, все пузыри прилипали влево.
- Шаг 6 — Настройки: изменены `Views/SettingsView.xaml`, `ViewModels/AppNavigation.cs`. ПОДТВЕРЖДЕНО ПОЛЬЗОВАТЕЛЕМ В EXE.
- Шаг 7 — Models / Gateway / Chip / Status:
  - §10.1 закрыт: `Chip` в `Styles/Cards.xaml` радиус `999` → `9`; потребители проверены (`ShellHeaderChip` имеет локальный радиус 14, Models tabs переопределён на 12, StatusView использует дефолт).
  - §10.2 закрыт: у `ModelsView` удалён внутренний дубль заголовка; `PageHeaderTitle` уменьшен 26 → 22 (единственный потребитель теперь shell header).
  - §10.3 закрыт: в `StatusView` основная строка показывает `Status`, `ModelState` демотирован вправо (приглушённый); дублирование с header устранено по предложению из §10.3.
  - `GatewayView`: подзаголовок тулбара `TextMutedBrush` → `TextSecondaryBrush` (тот же класс AA-контраста, что на Шаге 3).

---

## 7. Открытый вопрос: возможная утечка памяти

Пользователь сообщил о росте памяти при загруженной модели в простое. Модель на момент передачи не была установлена, измерения не выполнены.

Порядок действий описан в:

```text
desktop/MEMORY_GROWTH_DIAGNOSTICS.md
```

Кратко:

1. Запустить EXE, загрузить модель, не писать сообщений 10–15 минут, закрыть.
2. Открыть `SoulExeData/logs/SoulExe.log`.
3. Найти строки `MEMORY_SNAPSHOT`.
4. Сопоставить с `GEN`, `COGNITIVE_`, `SOUL_MEMORY_`, `SCENE` записями.

Интерпретация:

```text
managedHeap SoulExe стабилен + llamaWs/llamaPrivate растут и выходят на плато
   -> нормальная память модели (GGUF, KV cache, compute buffers)

managedHeap и privateBytes SoulExe растут монотонно,
cognitivePending=0, cognitiveRunning=0, networkSessions=0
   -> кандидат на managed/WPF утечку, нужен heap dump сравнение

между snapshot есть COGNITIVE_/SOUL_MEMORY_/GEN/SCENE
   -> приложение НЕ в простое, это фоновый inference по расписанию 60-300 c
```

Наиболее вероятная гипотеза: не пассивная утечка, а отложенный фоновый LLM-workload Cognitive Architecture, из-за которого llama.cpp расширяет и удерживает кэши.

---

## 8. Заметки от предыдущей сессии

Несколько выводов, которые сэкономят время.

1. **Полный rewrite не требовался.** Пользователь предлагал создать `NEW_VERSION` и переписать программу с нуля. Настоящей причиной «пропавшего интерфейса» были три отсутствующих файла code-behind по 6 строк. Архитектура рефакторинга оказалась рабочей. Прежде чем предлагать переписывание, ищи конкретный технический root cause.

2. **Сборка не является доказательством работоспособности UI.** В WPF есть класс ошибок, которые видны только в запущенном EXE: missing `InitializeComponent`, неверный `ControlTemplate`, нулевой viewport, `Visibility` триггеры, reparenting. Всегда требуй ручную проверку конкретного экрана.

3. **UI Automation не заменяет визуальную проверку.** Простые `TextBlock` не обязаны появляться как отдельные automation elements. Отсутствие элемента в automation tree не означает, что он скрыт. Для доказательства отрисовки использовался `RenderTargetBitmap` с анализом пикселей.

4. **Изменения по одному экрану реально работают.** Все крупные регрессии возникли, когда менялось несколько слоёв одновременно: shell, drawer, templates, page host. Малые шаги с проверкой EXE после каждого — правильный режим для этого проекта.

5. **Что стоит сделать в будущем, но не сейчас:**
   - вернуть adaptive navigation drawer, но как отдельный опциональный host, а не через reparenting единственного `NavigationView`;
   - унифицировать инфраструктуру personal/group thread по плану из `CONVERSATION_THREAD_ANALYSIS.md`, оставив разными message/header/composer templates;
   - постепенно заменить оставшиеся literal-цвета семантическими токенами;
   - рассмотреть виртуализацию карточных каталогов Library и Gateway; штатный `WrapPanel` не виртуализируется.

6. **Локальные измерения ротации логов.** `SoulExe.log` ограничен `5 MiB` + 4 архива. Если понадобится длительная диагностика памяти, учитывай, что старые снапшоты могут уехать в архивные файлы `SoulExe.1.log` ... `SoulExe.4.log`.

7. **Offscreen-рендер экрана без запуска приложения.** Рабочий рецепт, которым проверен header на Шаге 3. Позволяет увидеть реальную отрисовку до ручной проверки в EXE, но НЕ заменяет её.

   ```text
   1. Отдельный net8.0-windows WPF Exe ВНЕ репозитория (temp-каталог).
   2. Assembly.LoadFrom(Output\Debug\win-x64\SoulExe.dll).
   3. Activator.CreateInstance типа SoulExe.App + вызов InitializeComponent()
      через reflection. Так Application.Resources идентичен production.
      OnStartup не выполняется, потому что Run() не вызывается.
   4. Прочитать нужный *.xaml как текст, убрать x:Class и обработчики событий,
      заменить <views:*> на <Border>, загрузить через XamlReader.Load.
   5. Подставить простой stub-DataContext с нужными свойствами.
   6. Measure/Arrange/UpdateLayout в Border фиксированного размера,
      затем RenderTargetBitmap -> PngBitmapEncoder.
   7. CroppedBitmap + TransformedBitmap для зума: именно так был виден
      эллипс вместо пилюли.
   ```

   Важные ограничения рецепта:

   ```text
   - Loose XAML словари грузить через file:// URI нельзя: sibling StaticResource
     в Setter.Value не отложится и упадёт. Нужен именно скомпилированный App.
   - Проверяются layout, ресурсы, триггеры, обрезка текста, геометрия.
   - НЕ проверяются: реальные шрифты окна (TextFormattingMode="Display"
     задан на MainWindow), DPI, фокус, клавиатура, поведение команд.
   ```

---

## 9. Контроль качества перед передачей EXE пользователю

```text
[ ] Debug build с -warnaserror: 0 warnings, 0 errors
[ ] Conversation checks: passed
[ ] git diff --check: без whitespace ошибок
[ ] Нет отсутствующих code-behind в Views/ и Controls/
[ ] Нет временных debug-маркеров и runtime probe кода
[ ] Release self-contained single-file EXE собран
[ ] Указан точный путь к EXE
[ ] Названы конкретные экраны для ручной проверки
```

---

## 10. Известные дефекты -- ВСЕ ЗАКРЫТЫ на Шаге 7

Найдены на Шаге 3, отложены по правилу «один экран за шаг», исправлены в Шаге 7 (см. раздел 6.3).

### 10.1. [ЗАКРЫТО] `Chip` рисуется эллипсом

`Styles/Cards.xaml`: радиус `999` → `9`. Потребители проверены: `ShellHeaderChip` — локальный радиус 14; контейнер вкладок ModelsView — локальный 12; StatusView — дефолт 9. `AvatarCircle` не менялся (квадратный, корректный круг).

### 10.2. [ЗАКРЫТО] `ModelsView` дублирует заголовок страницы

Внутренний `PageHeaderTitle`/`PageHeaderSubtitle` удалён из ModelsView. `PageHeaderTitle` уменьшен 26 → 22 px: единственный потребитель теперь shell header.

### 10.3. [ЗАКРЫТО] `ModelState` показан в трёх местах одновременно

`StatusView`: основная строка переведена на `Status`, `ModelState` демотирован вправо приглушённым цветом. Header-пилюля и карточка МОДЕЛЬ остались быстрыми индикаторами состояния.

