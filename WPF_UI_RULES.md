# WPF UI Rules

## Назначение

Этот документ задаёт правила дальнейшей разработки интерфейса SoulExe.

Он создан после runtime-инцидента, при котором приложение успешно собиралось, но отдельные UI-области выглядели пустыми: sidebar и редактор персонажа не отображали содержимое. Причиной был глобальный кастомный `ScrollViewer` template, который некорректно измерял content в части вложенных WPF-layout сценариев.

Главный принцип:

> Красивый UI не должен достигаться ценой глобальных template-переопределений, которые могут сломать WPF measure/arrange или скрыть content без ошибок компиляции.

---

## 1. Архитектурные границы

Целевая композиция приложения:

```text
MainWindow
└─ AppShellView
   ├─ TitleBarView
   ├─ Sidebar navigation
   ├─ PageHost
   │  ├─ LibraryView
   │  ├─ ChatWorkspaceView
   │  ├─ CharactersView
   │  ├─ GatewayView
   │  ├─ ModelsView
   │  ├─ SettingsView
   │  └─ SetupView
   ├─ StatusView
   └─ Initial setup overlay
```

Правила:

- `MainWindow` остаётся тонким window-host и не получает feature-разметку.
- Бизнес-логика, persistence, API и generation не должны переноситься в XAML/code-behind ради layout.
- `MainViewModel` владеет прикладным состоянием и командами.
- View/code-behind владеет только transient presentation-state: focus, scroll, drawer visibility, breakpoints, animation.
- Новые разделы добавляются через `AppShellView` page mapping и отдельный `View`; не добавлять feature-разметку обратно в `MainWindow`.
- Не создавать два экземпляра одного редактируемого control ради responsive layout. Использовать один экземпляр и менять host/geometry только после runtime-проверки.

---

## 2. Обязательный code-behind для каждого UserControl

### Правило

Каждый `UserControl` с `x:Class` обязан иметь файл code-behind с конструктором:

```csharp
public partial class NavigationView : UserControl
{
    public NavigationView() => InitializeComponent();
}
```

### Почему это критично

Если файл `*.xaml.cs` отсутствует:

```text
проект собирается без ошибок
XAML валиден
StaticResource разрешаются
bindings корректны
InitializeComponent никогда не вызывается
контрол рендерится полностью пустым
```

Компилятор генерирует `InitializeComponent()` в `*.g.cs`, но конструктор должен находиться в пользовательском файле. Без него C# создаёт пустой конструктор по умолчанию, который не загружает XAML-дерево.

### Подтверждённый инцидент

Этот дефект был реальной причиной трёх симптомов одновременно:

```text
NavigationView   → пустой sidebar
CharactersView   → пустой редактор персонажа
StatusView       → нулевая высота статус-бара
```

Все три файла существовали как `.xaml`, но не имели `.xaml.cs`.

### Обязательная проверка

После создания или переноса любого View выполнить сравнение:

```powershell
$xaml = Get-ChildItem -LiteralPath "Views" -Filter *.xaml | ForEach-Object { $_.BaseName }
$cs = Get-ChildItem -LiteralPath "Views" -Filter *.xaml.cs | ForEach-Object { $_.Name -replace '\.xaml\.cs$','' }
Compare-Object -ReferenceObject $xaml -DifferenceObject $cs | Where-Object { $_.SideIndicator -eq '<=' }
```

Результат обязан быть пустым. То же правило применяется к `Controls/`.

---

## 3. Самое важное правило: ControlTemplate

### Запрещено без полного runtime-прогона

Не создавать глобальные implicit templates для layout-critical controls:

```xaml
<!-- Опасно: влияет на каждый ScrollViewer приложения. -->
<Style TargetType="ScrollViewer">
    <Setter Property="Template" Value="..." />
</Style>
```

Особенно опасны global implicit templates для:

```text
ScrollViewer
ContentControl
ItemsPresenter
ListBox
ComboBox
Grid
Border
UserControl
```

Причина: эти controls участвуют в WPF layout engine. Ошибка template может не дать compile error, но привести к:

```text
control существует в visual tree
→ bindings корректны
→ ActualWidth/Height родителя ненулевые
→ content viewport измеряется неверно
→ пользователь видит пустую область
```

### Как делать правильно

Использовать локальные keyed styles для конкретного сценария:

```xaml
<Style x:Key="ConversationMessageScroller"
       TargetType="ScrollViewer">
    <!-- Проверенный template только для ленты сообщений. -->
</Style>
```

Применять явно:

```xaml
<ScrollViewer Style="{StaticResource ConversationMessageScroller}">
    ...
</ScrollViewer>
```

Допустимые специализированные стили:

```text
ConversationMessageScroller
SettingsContentScroller
LibraryContentScroller
SetupContentScroller
GatewayDetailsScroller
```

Правило именования:

```text
<Feature><Purpose>Scroller
```

Новый specialized template сначала применяется к одному screen/control. Он не становится implicit global style, пока не проверен вручную в реальном EXE.

### Штатные platform templates

По умолчанию оставлять WPF platform template для:

```text
ScrollViewer
ContentControl
ItemsPresenter
```

Допустимо задавать безопасные свойства без подмены template:

```xaml
<Style TargetType="ScrollViewer">
    <Setter Property="Background" Value="Transparent" />
</Style>
```

---

## 4. ResourceDictionary правила

Структура ресурсов:

```text
Styles/
  Themes/Dark.xaml       -- concrete palette and theme brushes
  Colors.xaml            -- semantic aliases, merges the theme itself
  Tokens.xaml            -- spacing, radii, control heights
  Typography.xaml        -- text styles
  Buttons.xaml           -- button variants
  Inputs.xaml            -- input controls
  Controls.xaml          -- common controls without risky global layout templates
  Cards.xaml             -- surfaces and cards
  Layout.xaml            -- navigation and shell layout styles
  ConversationStyles.xaml
```

Порядок merged dictionaries в `App.xaml` важен:

```text
Theme
→ Colors
→ Tokens
→ Typography
→ Buttons
→ Inputs
→ Controls
→ Cards
→ Layout
→ Conversation styles
```

Правила:

- Не менять порядок dictionaries без сборки и runtime smoke-test.
- `StaticResource` может быть разрешён только если его dictionary уже загружен.
- Стиль с `BasedOn` должен находиться после базового стиля.
- Не возвращать старый монолитный `App.xaml`.
- При переносе ресурса из одного dictionary в другой сначала найти все потребители через search.
- Старые public style keys сохранять до завершения полной миграции всех XAML-потребителей.
- Новые цвета добавлять сначала в `Themes/Dark.xaml`, затем экспортировать semantic alias в `Colors.xaml`.
- Не добавлять literal colors в feature XAML, если семантика уже существует: accent, danger, surface, border, text, overlay.

### Cross-dictionary StaticResource: жёсткое ограничение

Для значений ресурсов (кисти, геометрии, эффекты) `StaticResource` на ключ из **соседнего** dictionary, смерженного рядом в `App.xaml`, НЕ работает.

```text
Value-ресурс реализуется при первом обращении к ключу.
В этот момент lookup видит только свой dictionary и его собственные MergedDictionaries.
Соседей по App.xaml он не видит.
```

Отложенные `Setter.Value` внутри `Style` — исключение: они разрешаются позже, по цепочке элемента, поэтому стили спокойно ссылаются на кисти темы.

Именно поэтому весь `Styles/Colors.xaml` бросал `XamlParseException` при обращении к любому `Brush.*`, хотя `Styles/Typography.xaml` со ссылками на те же кисти работал.

Правило:

```text
Dictionary, которому нужны значения из другого dictionary,
обязан сам смерджить его через ResourceDictionary.MergedDictionaries.
```

```xaml
<!-- Styles/Colors.xaml -->
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="Themes/Dark.xaml" />
</ResourceDictionary.MergedDictionaries>
<SolidColorBrush x:Key="Brush.Accent" Color="{StaticResource AccentColor}" />
```

Проверять такие алиасы обязательно на скомпилированной сборке, а не только сборкой проекта: ошибка проявляется лишь при первом обращении к ключу, поэтому неиспользуемый сломанный алиас молча живёт в проекте.

```csharp
// probe: Assembly.LoadFrom(SoulExe.dll) -> new SoulExe.App() -> InitializeComponent()
// затем Application.Current.Resources["Brush.Accent"]
```

### Нельзя алиасить Color через содержимое элемента

```xaml
<!-- Неверно: разбирается как литеральная строка и падает при обращении. -->
<Color x:Key="Color.Canvas">{StaticResource WindowColor}</Color>
```

Markup extension работает только в позиции атрибута. Алиас цвета выражается либо кистью (`Color="{StaticResource ...}"`), либо не выражается вовсе.

### CornerRadius не ограничивается половиной высоты

```xaml
<!-- Неверно для широкой невысокой пилюли: Border рисует ЭЛЛИПС. -->
<Setter Property="CornerRadius" Value="999" />
```

`Border` не клампит радиус по габаритам, поэтому `999` на прямоугольнике 140x28 даёт овал. Для пилюли задавать конкретный радиус, равный половине её высоты. Значение `999` допустимо только для квадратного элемента, где результат — корректный круг (`AvatarCircle`).


---

## 5. Layout и responsive design

### Local layout state

Breakpoints принадлежат view, а не `MainViewModel`:

```text
ChatWorkspaceView
ModelsView
GatewayView
SettingsView
MobileAccessView
SetupView
```

Допустимо использовать небольшой code-behind для:

```text
Loaded
Unloaded
SizeChanged
GridLength
Grid.SetRow / Grid.SetColumn / Grid.SetColumnSpan
Visibility
focus restoration
```

Не переносить в ViewModel:

```text
ActualWidth
drawer is open
scroll position
keyboard focus
temporary responsive mode
```

### Один control, один источник состояния

Для responsive layouts нельзя создавать два editable экземпляра одного control:

```text
Плохо:
wide: GroupConversationSettingsView #1
compact: GroupConversationSettingsView #2
```

Это может привести к разным:

```text
TextBox edits
focus state
validation state
scroll position
DataContext lifecycle
```

Предпочтительно:

```text
один control
→ меняется его host или Grid placement
→ bindings и state сохраняются
```

Перед runtime reparenting WPF element между hosts нужно отдельно проверить:

- visual tree не содержит дубликатов;
- element не оказывается в закрытом `ContentControl`;
- element не теряет DataContext;
- focus корректно восстанавливается;
- keyboard navigation не уходит в hidden host.

### Sidebar

Primary desktop sidebar критичен для discoverability.

Правила:

- На поддерживаемых desktop-ширинах sidebar должен быть прямым и постоянным child Shell.
- Не переносить единственный primary navigation control в скрытый drawer без обязательной ручной проверки.
- Не использовать sidebar `ScrollViewer` или custom navigation template, если меню помещается в поддерживаемой высоте.
- Пункты меню должны быть простыми `Button` с явным `ContentPresenter`.
- Любой adaptive drawer для меню сначала реализуется как isolated optional feature, а не как replacement постоянного sidebar.

---

## 6. Lazy views и DataContext lifecycle

`AppShellView` использует lazy page host для основных feature pages.

Правила:

- Перед присоединением cached view к `PageHost` явно назначать актуальный `DataContext`.
- Page, которая зависит от freshly selected domain object, может быть создана заново при navigation. Пример: `CharactersView`.
- Не полагаться только на inherited DataContext после detach/reattach через `ContentControl`.
- Для view с event subscriptions обязательно:
  - подписываться на `Loaded`;
  - отписываться на `Unloaded`;
  - делать `Subscribe` идемпотентным;
  - проверять `IsLoaded` и актуальную VM в отложенных Dispatcher callbacks.

Особенно это относится к:

```text
ChatWorkspaceView
LibraryView
Views с modal focus restoration
```

---

## 7. Chat rules

### Transcript

- История сообщений использует bounded presentation window, а не полную materialization.
- Лента сообщений использует virtualized `ListBox` с recycling.
- Не оборачивать virtualized list во внешний `ScrollViewer`.
- Не заменять `ListBox` на `ItemsControl + StackPanel` в message transcript.
- Search обязан раскрывать presentation window для historical `MessageId` до scroll.
- Personal и group messages остаются разными typed templates.
- Не объединять `ChatMessageViewModel` и `SceneMessageViewModel` ради упрощения XAML.

### Auto-follow

- Auto-follow хранится в thread view, не в ViewModel.
- Если пользователь не у нижнего края, новые сообщения не двигают viewport.
- Показывать `Новые сообщения`.
- `ScrollToMessage` отключает auto-follow.
- `Load older messages` отключает auto-follow до изменения коллекции.
- Streaming scroll requests должны coalesce-иться.

### Responsive chat

```text
Wide:   [Диалоги] [Лента] [Сведения]
Medium: [Диалоги] [Лента] + details drawer
Narrow: [Лента] + dialogs drawer / details drawer
```

Drawer state принадлежит `ChatWorkspaceView` и не сохраняется в data store.

---

## 8. Forms, dialogs и destructive actions

### Dialog standards

Любой custom overlay dialog обязан иметь:

```xaml
FocusManager.IsFocusScope="True"
KeyboardNavigation.TabNavigation="Cycle"
KeyboardNavigation.ControlTabNavigation="Cycle"
```

И поведение:

```text
open  → сохранить opener focus → сфокусировать meaningful control
Escape → существующая cancel/close command
close → вернуть focus opener, если он видим и enabled
```

Для destructive confirmation initial focus должен быть на:

```text
Отмена
```

### Удаления

Не выполнять необратимое удаление сразу из click/context menu.

Нужен flow:

```text
request deletion
→ immutable snapshot target ID/name
→ confirmation dialog
→ explicit confirm command
→ deletion
```

Не удерживать mutable `ListItemViewModel` в pending deletion state, потому что refresh списка может изменить target между запросом и confirm.

Подтверждение обязательно для:

```text
conversation
scene
message
lorebook
lore entry
character
persona
```

---

## 9. Accessibility

### Keyboard focus

Все custom control templates должны иметь `IsKeyboardFocused` state.

Минимальный focus-visible contract:

```text
AccentBrush border
BorderThickness = 2
или AccentSoftBrush surface
```

Покрывать:

```text
Button
IconButton
Navigation button
Pseudo-tab
TextBox
ComboBox
ListBoxItem
MenuItem
CheckBox
Slider thumb
ToggleButton
```

### Automation names

Каждая icon-only кнопка обязана иметь:

```xaml
AutomationProperties.Name="..."
```

Недопустимо полагаться только на:

```text
☰
...
×
✎
✕
➤
◀
▶
```

Примеры правильных имён:

```text
Открыть список диалогов
Открыть сведения о разговоре
Открыть поиск по сообщениям
Действия с сообщением
Закрыть диалог
Отправить сообщение
Редактировать персонажа
Удалить лорбук
Свернуть окно
Закрыть окно
```

### Pseudo-tabs

Если UI использует `Button` как tab-like selector:

- добавить `AutomationProperties.Name`;
- добавить `AutomationProperties.HelpText`;
- добавить `PositionInSet` и `SizeOfSet`;
- active item получает `AutomationProperties.ItemStatus="Текущая вкладка"`;
- задать предсказуемый `TabIndex`;
- контейнер использует `KeyboardNavigation.DirectionalNavigation="Cycle"`.

Не заменять Button на `TabItem` только ради семантики, если это требует изменения selection state в ViewModel. Делать такую миграцию отдельным шагом.

---

## 10. Loading, empty и error states

Нельзя показывать network/API error как empty result.

Правильная модель состояния:

```text
Loading
Success with items
Success empty
Error
```

Для Gateway и Models:

- error должен быть виден в контексте каталога;
- error должен давать retry action;
- empty state показывается только после успешного запроса без результатов;
- глобальный footer `Status` не является единственным носителем ошибки.

При очистке collection перед async запросом нужно хранить отдельное error state, чтобы UI не интерпретировал временно пустую коллекцию как успешную пустую выдачу.

---

## 11. Persistence и startup safety

### Data store

Любая mutation должна быть transactional на уровне in-memory root:

```text
snapshot
→ mutate
→ persist
→ success

on error:
→ restore snapshot
```

Нельзя оставлять изменённый `_root` в памяти после провалившегося save: следующая успешная операция может иначе записать неподтверждённые данные.

### Schema migration

Для migration:

```text
validated permanent backup
→ explicit legacy parsing
→ in-memory migration
→ semantic validation
→ temp write
→ temp reread/validation
→ atomic replacement
```

Нельзя:

```text
deserialize legacy schema directly into current root
→ silently discard unknown fields
→ persist truncated data
```

### Dispose lifecycle

`MainViewModel.DisposeAsync` выполняется ровно один раз.

После normal window closing очищать window `DataContext`, чтобы `App.OnExit` не освобождал тот же VM повторно.

---

## 12. Проверка перед расширением UI

Для каждого non-trivial UI изменения:

1. Проанализировать текущий XAML, templates, bindings и commands.
2. Найти все consumers затрагиваемого style/resource.
3. Внести минимальное изменение.
4. Выполнить:

```powershell
dotnet build SoulExe.csproj --no-restore -warnaserror
dotnet run --project SoulExe.ConversationChecks\SoulExe.ConversationChecks.csproj --no-restore
git diff --check
```

5. Собрать Release:

```powershell
dotnet publish SoulExe.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

6. Для changes templates/layout обязательно открыть affected screen в реальном EXE.

### Mandatory manual smoke tests для template/layout changes

Проверить минимум:

```text
Sidebar navigation visible
Library visible
Character editor visible
Chat personal and group thread visible
Settings / Mobile visible
Models / Gateway visible
Initial setup overlay visible on clean profile
```

Для affected screen проверить:

```text
normal window
maximized window
minimum supported window size
125% DPI
150% DPI
keyboard Tab/Shift+Tab
Escape for dialog/drawer
```

---

## 13. Release checklist

Перед передачей EXE:

- [ ] Debug build проходит с `-warnaserror`.
- [ ] Conversation checks проходят.
- [ ] `git diff --check` не показывает whitespace errors.
- [ ] Release self-contained single-file EXE создан.
- [ ] Проверен путь Release EXE.
- [ ] Нет временных visual debug markers.
- [ ] Нет временного runtime logging/probe code.
- [ ] Нет `TODO`, `FIXME`, `HACK` в затронутых production files без отдельного согласованного документа.
- [ ] Проверены sidebar и character editor в реальном EXE после любых changes `ScrollViewer`, `ContentControl`, page host или global templates.

---

## 14. Incident record

### Симптом

```text
Приложение собирается.
Sidebar выглядит пустым.
Редактор персонажа выглядит пустым.
```

### Причина

Глобальный custom `ScrollViewer` template некорректно работал в некоторых вложенных layouts.

### Почему компиляция не выявила проблему

XAML был синтаксически корректен, а WPF layout error проявлялся только в runtime measure/arrange. `StaticResource`, bindings и commands могли быть полностью валидны.

### Исправление

```text
Удалён global ScrollViewer ControlTemplate.
Возвращён штатный WPF ScrollViewer template.
Primary sidebar восстановлен как direct Shell layout.
```

### Долгосрочное правило

```text
Кастомизировать ScrollViewer только через keyed feature-specific styles.
Никогда не заменять ScrollViewer template глобально без полного manual runtime matrix.
```
