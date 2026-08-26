# Conversation Thread Analysis

## Цель

Документ фиксирует архитектурное решение перед Этапом 4.2. Цель — убрать дублирование инфраструктуры personal/group conversation thread, **не объединяя механически разные сценарии общения**, не меняя `MainViewModel`, команды, модели, persistence или бизнес-логику.

Проанализированы:

- `Controls/PersonalConversationThreadView.xaml`
- `Controls/PersonalConversationThreadView.xaml.cs`
- `Controls/GroupConversationThreadView.xaml`
- `Controls/GroupConversationThreadView.xaml.cs`
- `Controls/ConversationComposerView.xaml`
- `Views/ChatWorkspaceView.xaml`
- `Views/ChatWorkspaceView.xaml.cs`
- связанные presentation ViewModel.

---

## Текущее состояние

В `ChatWorkspaceView` одновременно создаются два thread-control. Групповой thread располагается поверх personal thread и скрывается через `IsSceneChatActive`:

- `Views/ChatWorkspaceView.xaml:16-22`
- `Controls/GroupConversationThreadView.xaml:7-17`
- `ViewModels/MainViewModel.Properties.cs:243-254`

Оба view наследуют общий `MainViewModel`, но используют разные наборы данных:

| Режим | Сообщения | Поиск | Draft | Выбранный разговор |
|---|---|---|---|---|
| Personal | `Messages` / `ChatMessageViewModel` | `ChatMessageSearchResults` | `Draft` | `SelectedPersonalConversation` |
| Group | `SceneMessages` / `SceneMessageViewModel` | `SceneMessageSearchResults` | `GroupDraft` | `SelectedGroupConversation` |

Ссылки: `ViewModels/MainViewModel.Properties.cs:18-21`, `:147-157`, `:388-436`.

---

## Полностью общие элементы

### 1. Thread shell

Оба thread view строятся по единой схеме:

```text
Border
 └ Grid
    ├ Header (Auto)
    ├ Message viewport (*)
    └ Composer (Auto)
```

Общие характеристики:

- `ChatAppearance.ChatBackgroundColor`;
- граница, скругление и clipping области;
- отдельная header-surface;
- message scroller со стилем `ConversationMessageScroller`;
- общий визуальный контракт typing bubble;
- подключение `ConversationComposerView`.

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:7-14`, `:95-271`.
- Group: `Controls/GroupConversationThreadView.xaml:18-24`, `:128-211`.

**Решение:** shell должен стать частью общего `ConversationThreadView`.

### 2. Header shell

В обоих вариантах header содержит:

- identity block слева;
- title/subtitle;
- conversation title справа;
- поиск;
- меню действий `...`;
- `ContextMenu`, открываемое через placement target.

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:14-48`.
- Group: `Controls/GroupConversationThreadView.xaml:24-81`.

**Решение:** общий header container с отдельными content templates для personal и group.

### 3. Search overlay

Обе панели поиска имеют идентичную композицию:

```text
Overlay border
 ├ Header: title + close
 ├ Query input
 └ Results list
    └ timestamp + author + preview
```

Обе используют тип `ChatMessageSearchResult`.

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:49-94`.
- Group: `Controls/GroupConversationThreadView.xaml:82-127`.
- Общий результат: `ViewModels/MainViewModel.Properties.cs:19-20`.

**Решение:** общий visual template результата и reusable search-overlay shell. Состояние поиска и команды остаются раздельными presentation contracts.

### 4. Базовая геометрия message bubble

Общие свойства:

- margin, padding, corner radius и maximum width из `ChatAppearance`;
- avatar, author, time, текст;
- search highlight;
- визуальное правило исходящих сообщений справа;
- typing indicator.

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:120-145`, `:187-243`.
- Group: `Controls/GroupConversationThreadView.xaml:139-186`.

**Решение:** извлечь в общие стили/визуальные primitives только после стабилизации. Не создавать один универсальный template для разных message VM.

### 5. UI code-behind infrastructure

Практически повторяются:

- `ScrollToEnd()`;
- `ScrollToMessage(Guid)`;
- поиск visual descendant;
- открытие context menu с placement target.

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml.cs:8-42`.
- Group: `Controls/GroupConversationThreadView.xaml.cs:8-42`.
- Внешняя orchestration: `Views/ChatWorkspaceView.xaml.cs:29-82`.

**Решение:** общий viewport behavior/control. Scroll и visual-tree API остаются UI-слоем и не переносятся в `MainViewModel`.

---

## Существенные различия

### Header

| Аспект | Personal | Group |
|---|---|---|
| Identity | один персонаж, avatar и online marker | два overlapping avatar участников |
| Главный title | `SelectedCharacter.Name` | `SceneParticipantNames` |
| Subtitle | `SelectedChatLastMessageLabel` | `SceneLastMessageLabel` |
| Right title | `SelectedChatHeaderTitle` | `SelectedGroupConversation.Name` |
| Дополнительное состояние | нет | countdown badge |

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:16-33`.
- Group: `Controls/GroupConversationThreadView.xaml:27-50`, `:64-79`.

**Решение:** `PersonalConversationHeaderTemplate` и `GroupConversationHeaderTemplate` остаются раздельными.

### Search state и команды

| Аспект | Personal | Group |
|---|---|---|
| Open | `IsChatMessageSearchOpen` | `IsSceneMessageSearchOpen` |
| Query | `ChatMessageSearchQuery` | `SceneMessageSearchQuery` |
| Results | `ChatMessageSearchResults` | `SceneMessageSearchResults` |
| Commands | `Close/SelectChat...` | `Close/SelectScene...` |

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:54-80`.
- Group: `Controls/GroupConversationThreadView.xaml:87-113`.

**Решение:** визуальный overlay общий; presentation data/commands передаются через отдельные personal/group adapters или templates. Не смешивать search state в одну ViewModel без отдельной migration-задачи.

### Personal messages

`ChatMessageViewModel` поддерживает workflow, отсутствующий у групповых реплик:

- date separators;
- `<think>`/thought block;
- inline editing;
- ответные variants;
- continue/edit/delete context menu;
- `VisibleContent` вместо raw content.

Ссылки:

- Template: `Controls/PersonalConversationThreadView.xaml:105-245`.
- VM: `ViewModels/ChatMessageViewModel.cs:8-167`.

**Решение:** отдельный `PersonalMessageTemplate` с `DataType=ChatMessageViewModel`.

### Group messages

`SceneMessageViewModel` имеет отдельную сценическую семантику:

- `IsFirstCharacter` — right/user-style;
- `IsUserParticipant` — centre event style;
- `IsDirector` — centre event без avatar;
- live `Content` streaming.

Ссылки:

- Template: `Controls/GroupConversationThreadView.xaml:131-188`.
- VM: `ViewModels/SceneMessageViewModel.cs:6-78`.

**Решение:** отдельный `GroupSceneMessageTemplate` с `DataType=SceneMessageViewModel`.

### Composer

`ConversationComposerView` уже является общим boundary, но содержит две самостоятельные формы:

| Personal | Group |
|---|---|
| `Draft`, send, continue | `GroupDraft`, start/pause, next turn, group send |
| Enter sends, Shift+Enter newline | отдельный keyboard contract |

Ссылки:

- `Controls/ConversationComposerView.xaml:1-27`.
- `Controls/ConversationComposerView.xaml.cs:11-17`.

**Решение:** personal и group composer templates остаются отдельными. Устранение строкового `Tag="Personal/Group"` — отдельная задача после стабилизации общего thread host.

### Context menus

Общие thread actions: pin, rename, delete. Но параметры и команды разные; personal имеет дополнительное создание чата с персонажем. Message-level actions есть только у personal.

Ссылки:

- Personal: `Controls/PersonalConversationThreadView.xaml:35-45`, `:124-133`.
- Group: `Controls/GroupConversationThreadView.xaml:52-61`.

**Решение:** общие только style/placement behavior. Состав menu остаётся отдельным.

---

## Что станет DataTemplate

| Артефакт | Решение | Причина |
|---|---|---|
| `PersonalMessageTemplate` | отдельный typed DataTemplate | message lifecycle: thoughts, edit, variants, actions |
| `GroupSceneMessageTemplate` | отдельный typed DataTemplate | scene roles: participant/director/first character |
| `PersonalConversationHeaderTemplate` | отдельный template | identity одного персонажа |
| `GroupConversationHeaderTemplate` | отдельный template | два участника и countdown |
| `ChatMessageSearchResultTemplate` | общий visual DataTemplate | одинаковые timestamp/author/preview |

### Критерии выбора DataTemplate

Использовать template, когда элемент:

1. purely visual;
2. имеет стабильный item presentation contract;
3. не владеет scroll/focus/input lifecycle;
4. меняется свойствами item VM, а не другим workflow.

---

## Что остаётся отдельным компонентом

| Компонент | Причина |
|---|---|
| Personal composer content | send/continue и keyboard behavior |
| Group composer content | start/pause/next turn и другой workflow |
| Personal message template | edit/thoughts/variants/context menu |
| Group message template | director/user participant scene rendering |
| Personal/group context menus | разные command parameters и доступные действия |
| `ConversationDetailsPanel` | details sidebar не является частью transcript и имеет разные mode-specific workflows |

---

## Целевая композиция

```text
ChatWorkspaceView
├─ ConversationListView
├─ ConversationThreadHost (один active thread)
│  ├─ ConversationThread shell
│  │  ├─ Header ContentPresenter
│  │  │  ├─ PersonalConversationHeaderTemplate
│  │  │  └─ GroupConversationHeaderTemplate
│  │  ├─ Search overlay
│  │  │  └─ ChatMessageSearchResultTemplate
│  │  ├─ Message viewport
│  │  │  ├─ PersonalMessageTemplate
│  │  │  └─ GroupSceneMessageTemplate
│  │  └─ Composer ContentPresenter
│  │     ├─ personal composer content
│  │     └─ group composer content
└─ ConversationDetailsPanel
```

Один активный host предпочтительнее текущих наложенных `PersonalConversationThreadView` и `GroupConversationThreadView`: inactive visual tree не создаётся, scroll behavior централизован, но business workflows остаются изолированными.

---

## Последовательность безопасной миграции

1. Зафиксировать baseline: personal/group messages, typing, search, menus, variants, thoughts, edit, streaming, resize.
2. Вынести общий search result visual template и scroll/context-menu helper; оба старых thread остаются рабочими.
3. Вынести два typed message templates без изменения bindings/commands.
4. Ввести thin presentation adapters для active personal/group thread; adapters только представляют текущие `MainViewModel` bindings и не содержат бизнес-логики.
5. Заменить два overlay thread-control одним `ContentControl`/thread host, выбирающим typed header/message/composer templates.
6. После проверок удалить legacy thread controls.
7. Отдельным шагом: virtualized `ListBox`, `ScrollIntoView`, coalesced auto-follow и action «Новые сообщения».
8. Последним шагом: typed composer routing вместо строкового `Tag`.

---

## Риски и регрессии

| Область | Риск | Митигирование |
|---|---|---|
| Active thread | оба/ни один thread видимы | один selector/adaptor как источник режима |
| Personal actions | потеря edit/delete/continue | отдельный typed template; проверка command parameters |
| Group roles | director/user participant выглядят как assistant | отдельный scene template с текущими triggers |
| Search navigation | result highlight без scroll | общий UI-only identity adapter и `ScrollIntoView` на этапе виртуализации |
| Streaming | потеря auto-scroll при scene content chunks | сохранить subscription до внедрения общего viewport behavior |
| Context menu | неверный PlacementTarget/DataContext | общий только placement behavior, composition menus отдельно |
| Composer | Enter попадает в group mode | сохранить разные keyboard contracts |
| Resource templates | `AncestorType=Window` bindings перестают разрешаться | проверка после переноса шаблонов в новый visual tree |

---

## Итоговое решение

Объединяется **инфраструктура**, а не доменные сценарии:

- общий thread shell, viewport API, search overlay, search result template и scroll/context-menu behavior;
- два typed message template;
- два header template;
- два composer content template;
- раздельные menu composition.

Не допускается объединять `ChatMessageViewModel` и `SceneMessageViewModel`, переносить UI-scroll в `MainViewModel` или заменять разные personal/group команды одним универсальным command contract в рамках Этапа 4.2.
