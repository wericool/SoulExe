using SoulExe.ConversationChecks;
using SoulExe.Models;
using SoulExe.Services;
using SoulExe.ViewModels;
using System.Text.Json;

var failures = new List<string>();
void Expect(bool condition, string message)
{
    if (!condition) failures.Add(message);
}

Expect(AppNavigation.NormalizePage("Mobile") == "Options"
    && AppNavigation.OptionsTabForRoute("Mobile") == "mobile",
    "Маршрут Mobile должен открывать Options с выбранной вкладкой mobile.");

var (character, chat) = ConversationFixtures.CreateDirectChat();
Expect(chat.Messages.Count == 2, "Фикстура обычного чата должна содержать две реплики.");
Expect(chat.Messages.Select(message => message.SequenceNumber).SequenceEqual([1, 2]), "Номера сообщений обычного чата должны быть последовательными.");
Expect(chat.Messages.All(message => message.Variants.Any(variant => variant.Id == message.SelectedVariantId)), "Каждое сообщение обычного чата должно иметь текущий вариант.");

var (first, second, scene) = ConversationFixtures.CreateScene();
Expect(scene.Participants.Count(participant => participant.Kind == ConversationParticipantKind.Character) == 2, "Групповой разговор должен хранить обоих персонажей.");
Expect(scene.Messages.Count == 3, "Фикстура сцены должна содержать режиссёрское событие и две реплики.");
Expect(scene.Messages.Count(message => message.Kind == ConversationMessageKind.DirectorEvent) == 1, "Режиссёрское событие не должно потеряться.");
Expect(scene.Messages.Select(message => message.SequenceNumber).SequenceEqual([1, 2, 3]), "Номера сценических сообщений должны быть последовательными.");

var conversations = new[] { chat, scene };
var root = new SoulDataRoot { Characters = [character, first, second], Conversations = conversations.ToList() };
var direct = chat;
var sceneConversation = scene;
Expect(direct.Kind == ConversationKind.Direct && direct.Participants.Count == 2, "Личный разговор должен содержать пользователя и персонажа.");
Expect(direct.Mode == ConversationMode.Personal, "Режим личного разговора должен определяться одним AI-персонажем.");
Expect(direct.Messages.All(message => message.Variants.Count > 0), "Канонический разговор должен хранить варианты ответа.");
Expect(sceneConversation.Kind == ConversationKind.Scene && sceneConversation.Participants.Count == 4, "Сцена должна стать разговором с двумя персонажами, пользователем и режиссёром.");
Expect(sceneConversation.Mode == ConversationMode.Group, "Режим группового разговора должен определяться несколькими AI-персонажами.");
Expect(sceneConversation.Messages.Count(message => message.Kind == ConversationMessageKind.DirectorEvent) == 1, "Групповой разговор должен сохранить режиссёрское событие.");
var historicalScene = ConversationFixtures.CreateScene().Conversation;
for (var sequence = 4; sequence <= 140; sequence++)
    historicalScene.Messages.Add(ConversationFixtures.Message(sequence, historicalScene.Participants[0].Id, SoulMessageAuthorKind.User, "Исторический персонаж", $"историческая-реплика-{sequence:D3}", historicalScene.UpdatedAt.AddMinutes(sequence)));
var sceneWindowStart = ConversationPresentationWindow.LatestStart(historicalScene.Messages.Count);
Expect(historicalScene.Messages.Count - sceneWindowStart == 60 && sceneWindowStart > 0, "Окно групповой ленты должно быть ограничено страницей, а не материализовать всю историю.");
var historicalGroupHit = ConversationMessageSearch.SearchGroup(historicalScene, "историческая-реплика-004").SingleOrDefault();
Expect(historicalGroupHit?.MessageId == historicalScene.Messages.Single(message => message.SequenceNumber == 4).Id, "Поиск группы должен возвращать ID исторической реплики вне текущего окна для выбора и перехода.");
Expect(ConversationPresentationWindow.PreviousStart(sceneWindowStart) < sceneWindowStart
    && ConversationPresentationWindow.StartContaining(historicalScene.Messages.Count, sceneWindowStart, 3) <= 3,
    "Окно истории должно загружать раннюю страницу и раскрывать исторический ID без изменения сохранённой истории.");
Expect(sceneConversation.FindParticipant(sceneConversation.TurnState?.NextParticipantId)?.CharacterId == first.Id, "Следующий участник должен отображаться в общей модели.");

var directThread = new ConversationThreadPresentationViewModel(direct, root.Characters);
var sceneThread = new ConversationThreadPresentationViewModel(sceneConversation, root.Characters);
Expect(directThread.Messages.Count == direct.Messages.Count, "Общее представление не должно терять сообщения обычного чата.");
Expect(directThread.Messages[0].ShowDateSeparator, "Первая реплика общего представления должна открывать дату-разделитель.");
Expect(directThread.Messages.Any(message => message.IsOutgoing), "В обычном чате пользовательская реплика должна быть выровнена как исходящая.");
Expect(sceneThread.Messages.Count == sceneConversation.Messages.Count, "Общее представление не должно терять сообщения сцены.");
Expect(sceneThread.Messages.Count(message => message.IsDirector) == 1, "Режиссёрское событие должно иметь особый тип в общем представлении.");
Expect(sceneThread.Messages.Any(message => message.IsOutgoing), "Первая реплика сцены должна сохранять правило выравнивания участника A.");

var directCapabilities = ConversationCapabilityPolicy.For(direct);
var sceneCapabilities = ConversationCapabilityPolicy.For(sceneConversation);
Expect(directCapabilities.CanAppendUserMessage && directCapabilities.CanAddDirectorEvent, "Личный разговор должен разрешать пользовательскую реплику и режиссёрское событие.");
Expect(sceneCapabilities.CanAppendUserMessage && sceneCapabilities.CanAddDirectorEvent && sceneCapabilities.CanStart && sceneCapabilities.CanChooseNextParticipant, "Групповой разговор должен публиковать реплики, действия режиссёра, запуск и выбор следующего участника.");

var migrationTestRoot = Path.Combine(Path.GetTempPath(), "SoulExe.ConversationChecks", Guid.NewGuid().ToString("N"));
try
{
    var canonicalRoot = new SoulDataRoot { SchemaVersion = 10, Characters = [character, first, second], Conversations = conversations.ToList() };
    var dataPaths = new DataPaths(migrationTestRoot);
    Directory.CreateDirectory(dataPaths.Root);
    await File.WriteAllTextAsync(Path.Combine(dataPaths.Root, "soulexe.json"), JsonSerializer.Serialize(canonicalRoot));
    var legacyRoot = new
    {
        SchemaVersion = 8, Characters = new[] { character, first, second },
        Chats = new[] { chat }, Scenes = new[] { scene },
        Preferences = new AppPreferences { MobileAccessPassword = "legacy-plaintext" }
    };
    await File.WriteAllTextAsync(Path.Combine(dataPaths.Root, "soulexe.json"), JsonSerializer.Serialize(legacyRoot));
    var v8Store = new JsonDataStore(new DataPaths(migrationTestRoot));
    await v8Store.InitializeAsync();
    var v8Migrated = await v8Store.ReadAsync(value => value);
    Expect(v8Migrated.SchemaVersion == 10 && v8Migrated.Conversations.Count == 2 && v8Migrated.Conversations.Sum(item => item.Messages.Count) == 5, "v8→v10 должна сохранить число чатов/сцен и сообщений.");
    Expect(v8Migrated.Conversations.Select(item => item.Id).OrderBy(item => item).SequenceEqual(new[] { chat.Id, scene.Id }.OrderBy(item => item)), "v8→v10 должна сохранить идентификаторы разговоров.");
    Expect(v8Migrated.Conversations.SelectMany(item => item.Messages).Select(item => item.Id).OrderBy(item => item).SequenceEqual(conversations.SelectMany(item => item.Messages).Select(item => item.Id).OrderBy(item => item)), "v8→v10 должна сохранить идентификаторы сообщений.");
    Expect(string.IsNullOrEmpty(v8Migrated.Preferences.MobileAccessPassword) && Directory.EnumerateFiles(Path.Combine(dataPaths.Root, "backups"), "migration_v8_*.json").Any(), "v8→v10 должна создать постоянный backup и удалить plaintext mobile-пароль из v10.");
    var v10AfterMigration = new JsonDataStore(new DataPaths(migrationTestRoot));
    await v10AfterMigration.InitializeAsync();
    Expect((await v10AfterMigration.ReadAsync(value => value)).SchemaVersion == 10, "После v8→v10 normal startup должен открыть v10.");

    // Restore the canonical fixture for existing persistence checks.
    await File.WriteAllTextAsync(Path.Combine(dataPaths.Root, "soulexe.json"), JsonSerializer.Serialize(canonicalRoot));
    var dataStore = new JsonDataStore(dataPaths);
    await dataStore.InitializeAsync();
    var migrated = await dataStore.ReadAsync(value => value);
    Expect(migrated.SchemaVersion == 10 && migrated.Conversations.Count == 2, "Schema v10 должна читать канонические разговоры.");
    var rejectedMutation = false;
    try
    {
        await dataStore.MutateAsync(value =>
        {
            value.Characters[0].Title = "Несохранённое изменение";
            throw new InvalidOperationException("fixture mutation failure");
        }, "failed_mutation");
    }
    catch (InvalidOperationException) { rejectedMutation = true; }
    var afterRejectedMutation = await dataStore.ReadAsync(value => value);
    Expect(rejectedMutation && afterRejectedMutation.Characters[0].Title == character.Title,
        "Неуспешная мутация не должна оставлять изменённое состояние в памяти для следующего сохранения.");
    await dataStore.MutateAsync(value => value.Characters[0].Title = "Обновлённая карточка", "conversation_sync_test");
    await dataStore.MutateConversationsAsync(values =>
    {
        var conversation = values.Single(item => item.Id == chat.Id);
        conversation.Name = "Переименованный разговор";
        conversation.IsPinned = true;
    }, "conversation_sync_test");
    var synchronized = await dataStore.ReadAsync(value => value);
    Expect(synchronized.Conversations.Single(conversation => conversation.Id == chat.Id).Name == "Переименованный разговор", "Каноническое изменение должно сохраняться независимо от изменения карточки персонажа.");
    Expect(synchronized.Conversations.Single(conversation => conversation.Id == chat.Id).IsPinned, "Каноническая мутация должна немедленно обновлять разговор.");
    Expect(Directory.EnumerateFiles(Path.Combine(dataPaths.Root, "backups"), "soulexe_*conversation_sync_test*.json").Count() == 2, "Два быстрых сохранения должны создавать две разные резервные копии.");
    var reloadedStore = new JsonDataStore(new DataPaths(migrationTestRoot));
    await reloadedStore.InitializeAsync();
    var reloaded = await reloadedStore.ReadAsync(value => value);
    var persistedJson = await File.ReadAllTextAsync(Path.Combine(dataPaths.Root, "soulexe.json"));
    Expect(!persistedJson.Contains("\"Chats\"", StringComparison.Ordinal) && !persistedJson.Contains("\"Scenes\"", StringComparison.Ordinal), "Schema v10 не должна сохранять вторую копию Chats или Scenes.");
    Expect(reloaded.SchemaVersion == 10 && reloaded.Conversations.Single(conversation => conversation.Id == chat.Id).Name == "Переименованный разговор", "Каноническая запись schema v10 должна читаться после перезапуска.");
    var reloadedConversation = reloaded.Conversations.Single(conversation => conversation.Id == chat.Id);
    Expect(reloadedConversation.IsPinned, "Каноническая запись должна восстановить изменения после перезапуска.");
    Expect(reloadedConversation.Context.SummaryDirectives == chat.Context.SummaryDirectives, "Schema v10 должна сохранять директивы summary.");
    Expect(reloadedConversation.Messages.All(message => message.Variants.Any(variant => variant.Id == message.SelectedVariantId)), "Schema v10 должна восстанавливать выбранный вариант каждой реплики.");
}
finally
{
    if (Directory.Exists(migrationTestRoot)) Directory.Delete(migrationTestRoot, recursive: true);
}

var history = new[] { "старое", "актуальное" };
var mobilePasswordHash = MobileAccessPasswordHasher.Hash("новый пароль");
Expect(MobileAccessPasswordHasher.Verify("новый пароль", mobilePasswordHash) && !MobileAccessPasswordHasher.Verify("старый пароль", mobilePasswordHash), "Mobile-вход должен принимать только новый пароль по PBKDF2-хэшу.");
Expect(!MobileAccessPasswordHasher.Verify("   ", mobilePasswordHash), "Пробельный пароль не должен проходить mobile-вход.");
var whitespacePasswordRejected = false;
try { MobileAccessPasswordHasher.Hash("   "); }
catch (ArgumentException) { whitespacePasswordRejected = true; }
Expect(whitespacePasswordRejected, "Новый mobile-пароль из одних пробелов должен отклоняться до сохранения.");
Expect(!MobileAccessPasswordHasher.Verify("новый пароль", "pbkdf2-sha256$0$AA==$AA=="), "Повреждённый хэш mobile-входа должен безопасно отклоняться.");
Expect(!MobileAccessPasswordHasher.Verify("новый пароль", "pbkdf2-sha256$999999999$AA==$AA=="), "Хэш с небезопасным числом итераций должен отклоняться до вычисления PBKDF2.");
Expect(!MobileAccessPasswordHasher.Verify(new string('p', 1025), mobilePasswordHash), "Слишком длинный пароль должен отклоняться до вычисления PBKDF2.");
var sessions = new NetworkSessionStore();
var sessionTime = DateTimeOffset.UtcNow;
sessions.Add("active", sessionTime);
Expect(sessions.TryAuthorize("active", sessionTime.AddHours(11)), "Активная mobile-сессия должна оставаться авторизованной до TTL.");
sessions.Cleanup(sessionTime.AddHours(24));
Expect(!sessions.TryAuthorize("active", sessionTime.AddHours(24)), "Неактивная mobile-сессия должна удаляться по TTL.");
for (var index = 0; index < 300; index++) sessions.Add($"session-{index}", sessionTime.AddMinutes(index));
Expect(sessions.Count == 256 && !sessions.TryAuthorize("session-0", sessionTime.AddHours(6)), "Хранилище mobile-сессий должно удалять самые старые токены при достижении лимита.");
var logRotationRoot = Path.Combine(Path.GetTempPath(), "SoulExe.ConversationChecks", Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(logRotationRoot);
    var currentLog = Path.Combine(logRotationRoot, "SoulExe.log");
    await File.WriteAllTextAsync(currentLog, "old log");
    await File.WriteAllTextAsync(Path.Combine(logRotationRoot, "SoulExe.1.log"), "older log");
    AppLog.RotateIfNeeded(logRotationRoot, 1, 2);
    Expect(File.Exists(Path.Combine(logRotationRoot, "SoulExe.1.log")) && File.Exists(Path.Combine(logRotationRoot, "SoulExe.2.log")) && !File.Exists(currentLog), "Ротация AppLog должна сохранять текущий и предыдущий журналы в ограниченном числе архивов.");
}
finally
{
    if (Directory.Exists(logRotationRoot)) Directory.Delete(logRotationRoot, recursive: true);
}
var sampleWrites = 0;
var sampler = new MemoryDiagnosticsSampler(
    () => new MemoryDiagnosticSnapshot(new ProcessMemoryDiagnostic(1, 2, 3, 4, 5), null, null, 0, 0, 0),
    _ => Interlocked.Increment(ref sampleWrites),
    TimeSpan.FromMilliseconds(20));
sampler.Start();
sampler.Start();
await Task.Delay(70);
await sampler.DisposeAsync();
var writesAtDispose = sampleWrites;
await Task.Delay(50);
Expect(writesAtDispose >= 2 && writesAtDispose < 6 && sampleWrites == writesAtDispose && !sampler.IsRunning, "Sampler памяти должен иметь один цикл и полностью останавливаться при DisposeAsync.");
var appearance = new ChatAppearanceSettings
{
    TextColor = "#123456", ActionColor = "#234567", QuoteColor = "#345678", CodeColor = "#456789",
    AssistantBubbleColor = "#56789A", UserBubbleColor = "#6789AB", ChatBackgroundColor = "#789ABC",
    FontSize = 19, BubbleMaxWidth = 840, BubbleCornerRadius = 11,
    FormatActions = false, FormatQuotes = false, FormatBold = false, FormatCode = false
};
var appearanceRoundTrip = JsonSerializer.Deserialize<SoulDataRoot>(JsonSerializer.Serialize(new SoulDataRoot { Preferences = new AppPreferences { ChatAppearance = appearance.Clone() } }))!;
Expect(appearanceRoundTrip.Preferences.ChatAppearance.TextColor == appearance.TextColor
    && appearanceRoundTrip.Preferences.ChatAppearance.ActionColor == appearance.ActionColor
    && appearanceRoundTrip.Preferences.ChatAppearance.QuoteColor == appearance.QuoteColor
    && appearanceRoundTrip.Preferences.ChatAppearance.CodeColor == appearance.CodeColor
    && appearanceRoundTrip.Preferences.ChatAppearance.AssistantBubbleColor == appearance.AssistantBubbleColor
    && appearanceRoundTrip.Preferences.ChatAppearance.UserBubbleColor == appearance.UserBubbleColor
    && appearanceRoundTrip.Preferences.ChatAppearance.ChatBackgroundColor == appearance.ChatBackgroundColor
    && appearanceRoundTrip.Preferences.ChatAppearance.FontSize == appearance.FontSize
    && appearanceRoundTrip.Preferences.ChatAppearance.BubbleMaxWidth == appearance.BubbleMaxWidth
    && appearanceRoundTrip.Preferences.ChatAppearance.BubbleCornerRadius == appearance.BubbleCornerRadius
    && !appearanceRoundTrip.Preferences.ChatAppearance.FormatActions
    && !appearanceRoundTrip.Preferences.ChatAppearance.FormatQuotes
    && !appearanceRoundTrip.Preferences.ChatAppearance.FormatBold
    && !appearanceRoundTrip.Preferences.ChatAppearance.FormatCode,
    "ChatAppearance должен сохранять все цвета, размеры и флаги форматирования.");
var savedLlamaOptions = new LlamaRuntimeOptions { TopK = 73, RepeatPenalty = 1.27 };
var savedPreferences = new AppPreferences();
savedLlamaOptions.WriteToPreferences(savedPreferences);
var reloadedLlamaOptions = new LlamaRuntimeOptions();
reloadedLlamaOptions.ApplyFromPreferences(savedPreferences);
var runtimeSettings = LlamaSettingsFactory.Build(reloadedLlamaOptions, "server", "model", "repository", 8000);
Expect(savedPreferences.TopK == 73 && savedPreferences.RepeatPenalty == 1.27
    && reloadedLlamaOptions.TopK == 73 && reloadedLlamaOptions.RepeatPenalty == 1.27
    && runtimeSettings.TopK == 73 && runtimeSettings.RepeatPenalty == 1.27,
    "TopK и RepeatPenalty должны сохраняться в preferences и передаваться в runtime-настройки llama.cpp.");
var directResponsePolicy = PromptRules.WithDirectResponseMode([new LlamaMessage("user", "Проверка")]);
Expect(directResponsePolicy.Count == 2 && directResponsePolicy[0].role == "system" && directResponsePolicy[0].content.Contains("DIRECT RESPONSE MODE", StringComparison.Ordinal), "Режим прямого ответа должен добавляться prompt policy до пользовательского сообщения.");
var budgetPlan = ContextBudgetPlan.Create(4096, 512, 768, 64);
Expect(budgetPlan.BaseContextTokens + budgetPlan.CharacterTokens + budgetPlan.StateTokens + budgetPlan.LoreTokens + budgetPlan.SummaryTokens + budgetPlan.MemoryTokens + budgetPlan.ReservedHistoryTokens <= budgetPlan.InputBudget, "ContextBudgetPlan не должен распределять больше доступного входного бюджета.");
var selectedHistory = ConversationContextWindow.TakeLatestThatFits(history, 6, value => value);
Expect(selectedHistory.SequenceEqual(["актуальное"]), "Общее окно контекста должно сохранять последнюю подходящую реплику.");
var pagingCursor = ConversationPaging.CreateCursor(sceneConversation);
var parsedPagingCursor = ConversationPaging.ParseCursor(pagingCursor);
Expect(parsedPagingCursor?.UpdatedAt == sceneConversation.UpdatedAt && parsedPagingCursor.Id == sceneConversation.Id, "Курсор единого API должен сохранять время и идентификатор разговора.");
Expect(ConversationPaging.ParseCursor("not-a-cursor") is null, "Повреждённый курсор единого API не должен вызывать ошибку.");
var outOfRangePagingCursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{long.MaxValue}|{Guid.NewGuid()}"));
Expect(ConversationPaging.ParseCursor(outOfRangePagingCursor) is null, "Курсор с недопустимыми ticks единого API не должен вызывать ошибку.");
Expect(ConversationPaging.ReadMessageTake("999") == 100, "Лимит сообщений единого API должен ограничиваться ста последними репликами.");
Expect(ConversationPaging.ReadPageSize("0") == 50, "Некорректный размер страницы должен использовать безопасный стандартный лимит.");
Expect(ConversationTurnPolicy.CanScheduleAutomaticTurn("running", "alternate", 5), "Автоматическая сцена с задержкой от пяти секунд должна планироваться.");
Expect(!ConversationTurnPolicy.CanScheduleAutomaticTurn("paused", "alternate", 10), "Пауза не должна планировать автоматический ход.");
Expect(ConversationTurnPolicy.NextStatusAfterGeneratedTurn("manual") == "running", "Сцена должна продолжаться после сгенерированного хода независимо от прежнего режима.");

var promptCharacter = new SoulCharacter { Name = "Надя", ReplyLanguage = "Русский", SystemPrompt = new string('п', 6000) };
var promptState = new SoulStateVariable { Key = "location" };
promptCharacter.StateVariables.Add(promptState);
var promptHistory = new List<SoulMessage>
{
    PromptMessage(1, SoulMessageRole.User, "Мира", "Мы всё ещё у старого маяка.", SoulMessageAuthorKind.Persona),
    PromptMessage(2, SoulMessageRole.System, "Режиссёр", "Начинается дождь.", SoulMessageAuthorKind.Director)
};
var promptConversation = ConversationFactory.Personal(promptCharacter, "Проверка промпта");
promptConversation.Context.StateValues[promptState.Id] = new string('с', 6000);
promptConversation.SummaryText = new string('с', 6000);
promptConversation.Context.Memory!.CharacterMemory = new string('м', 6000);
promptConversation.Messages = promptHistory.Select(message => CanonicalMessage(message, promptConversation)).ToList();
var promptLore = new SoulLorebook
{
    Name = "Город",
    Entries = [new SoulLoreEntry { Name = "Маяк", TriggerMode = "keyword", Keywords = ["маяк"], Content = "Маяк давно заброшен, но его свет иногда возвращается." }]
};
var prompt = new ConversationPromptEngine().BuildDirect(new PromptBuildRequest(
    promptCharacter, promptConversation, new SoulPersona { Name = "Мира", Description = "Фотограф" }, null,
    [promptLore], [], "Что там видно?", 4096, 512, IncludeSoulMemory: true, IncludeAutoSummary: true,
    ExcludeLastUserMessage: false));
Expect(prompt.Messages.Any(message => message.role == "system" && message.content.Contains("[USER PROFILE]", StringComparison.Ordinal)), "Промпт должен включать выбранную персону как профиль пользователя.");
Expect(prompt.Messages.Any(message => message.role == "user" && message.content.StartsWith("[PERSONA: Мира]", StringComparison.Ordinal)), "История должна сохранять автора-персону отдельной разметкой.");
Expect(prompt.Messages.Any(message => message.role == "system" && message.content.StartsWith("[DIRECTOR EVENT]", StringComparison.Ordinal)), "История должна сохранять режиссёрское событие системным сообщением.");
Expect(prompt.Messages[0].content.Contains("[BACKGROUND KNOWLEDGE: Маяк]", StringComparison.Ordinal), "Лор должен активироваться по недавней реплике, а не только по текущему вводу.");
Expect(prompt.Diagnostics.Any(diagnostic => diagnostic.Category == "lore" && diagnostic.Text.Contains("trigger=direct_recent_and_current", StringComparison.Ordinal)), "Диагностика лора должна указывать тип окна активации без текста реплик.");
Expect(prompt.Diagnostics.Any(diagnostic => diagnostic.Category == "budget"), "Сборщик промпта должен публиковать диагностику token budget.");
Expect(prompt.Diagnostics.Any(diagnostic => diagnostic.Category == "summary"), "Сборщик промпта должен сообщать об обрезании слишком длинной summary.");
Expect(prompt.Diagnostics.Any(diagnostic => diagnostic.Category == "memory"), "Сборщик промпта должен сообщать об обрезании слишком длинной памяти.");
Expect(prompt.Diagnostics.Any(diagnostic => diagnostic.Category == "character"), "Сборщик промпта должен сообщать об обрезании слишком длинной карточки персонажа.");
Expect(prompt.Diagnostics.Any(diagnostic => diagnostic.Category == "state"), "Сборщик промпта должен сообщать об обрезании слишком длинного состояния.");
var promptTrace = PromptTraceFormatter.Format(prompt);
Expect(promptTrace.Contains("source=system", StringComparison.Ordinal) && promptTrace.Contains("cause=trimmed", StringComparison.Ordinal) && !promptTrace.Contains(promptConversation.SummaryText, StringComparison.Ordinal), "Trace промпта должен содержать структурированные метаданные без текста контекста.");
var storedPromptDiagnostic = PromptDiagnosticSnapshotStore.Publish("fixture", prompt);
Expect(storedPromptDiagnostic.Trace == PromptDiagnosticSnapshotStore.Latest()?.Trace && !storedPromptDiagnostic.Trace.Contains("Что там видно?", StringComparison.Ordinal), "Отладочный snapshot должен хранить только безопасную структуру промпта без текста сообщения.");
var boundedBasePrompt = new ConversationPromptEngine().BuildDirect(new PromptBuildRequest(
    promptCharacter, promptConversation, new SoulPersona { Name = "Мира", PromptText = new string('п', 6000) }, new SoulPromptPreset { PromptText = new string('н', 6000) },
    [promptLore], [], "Что там видно?", 4096, 512, IncludeSoulMemory: true, IncludeAutoSummary: true, ExcludeLastUserMessage: false));
Expect(boundedBasePrompt.Diagnostics.Any(diagnostic => diagnostic.Category == "base_context"), "Preset и профиль персоны должны обрезаться в отдельном защищённом бюджете.");
Expect(boundedBasePrompt.Messages.Any(message => message.content.Contains("[DIRECTOR EVENT]", StringComparison.Ordinal)), "Ограничение базового контекста не должно вытеснять событие режиссёра из истории.");
const string proactiveFixtureDirective = "PROACTIVE FIXTURE DIRECTIVE";
var proactivePrompt = new ConversationPromptEngine().BuildDirect(new PromptBuildRequest(
    promptCharacter, promptConversation, null, null, [promptLore], [], string.Empty, 4096, 512,
    ExcludeLastUserMessage: false, AppendUserMessage: false, IsContinuation: true, HiddenDirective: proactiveFixtureDirective));
Expect(proactivePrompt.Messages.Count(message => message.content == proactiveFixtureDirective) == 1, "Инициативная команда должна передаваться модели ровно один раз.");
Expect(!proactivePrompt.Messages.Any(message => message.content == PromptRules.ContinuationDirectorCommand), "Инициативная команда не должна одновременно добавлять обычную команду Continue.");
Expect(!promptConversation.Messages.Any(message => message.Content.Contains(proactiveFixtureDirective, StringComparison.Ordinal)), "Скрытая инициативная команда не должна попадать в сохранённую историю.");

var (promptFirst, promptSecond, promptScene) = ConversationFixtures.CreateScene();
promptFirst.SystemPrompt = new string('г', 6000);
var sceneLore = new SoulLorebook
{
    Name = "Горный лор",
    Entries = [new SoulLoreEntry { Name = "Сигнальный огонь", TriggerMode = "keyword", Keywords = ["огонь"], Content = "Сигнальные огни в этих горах обычно ведут к безопасному убежищу." }]
};
promptFirst.LorebookIds.Add(sceneLore.Id);
var personaMessage = ConversationFixtures.Message(4, promptScene.Participants.Single(participant => participant.Kind == ConversationParticipantKind.User).Id, SoulMessageAuthorKind.Persona, "Мира", "Я тоже вижу огонь внизу.", promptScene.UpdatedAt.AddMinutes(1));
personaMessage.AuthorPersonaId = Guid.NewGuid();
promptScene.Messages.Add(personaMessage);
var promptGroupConversation = promptScene;
var scenePrompt = new ConversationPromptEngine().BuildGroup(new GroupPromptBuildRequest(
    promptGroupConversation, promptFirst, promptSecond,
    new Dictionary<Guid, SoulLorebook> { [sceneLore.Id] = sceneLore }, promptFirst.Id, 4096, 512));
Expect(scenePrompt.Messages[0].content.Contains("[BACKGROUND KNOWLEDGE: Сигнальный огонь]", StringComparison.Ordinal), "Групповой разговор должен активировать лор по недавней истории.");
Expect(scenePrompt.Messages.Any(message => message.role == "system" && message.content.StartsWith("[DIRECTOR EVENT]", StringComparison.Ordinal)), "Групповой промпт должен сохранять режиссёрское событие системным.");
Expect(scenePrompt.Messages.Any(message => message.role == "user" && message.content.StartsWith("[PERSONA SPEECH: Мира]", StringComparison.Ordinal)), "Групповой промпт должен сохранять реплику пользовательской персоны.");
var directorMessageIndex = Enumerable.Range(0, scenePrompt.Messages.Count)
    .First(index => scenePrompt.Messages[index].role == "system" && scenePrompt.Messages[index].content.StartsWith("[DIRECTOR EVENT]", StringComparison.Ordinal));
Expect(directorMessageIndex > 1 && directorMessageIndex < scenePrompt.Messages.Count - 1, "Событие режиссёра в середине истории должно сохранять позицию между репликами и финальной инструкцией хода.");
Expect(scenePrompt.Diagnostics.Any(diagnostic => diagnostic.Category == "budget"), "Групповой промпт должен публиковать диагностику token budget.");
Expect(scenePrompt.Diagnostics.Any(diagnostic => diagnostic.Category == "character"), "Групповой промпт должен сообщать об обрезании слишком длинной карточки.");

var summaryPrompt = SummaryPromptBuilder.Build("Надя уже предложила кофе.", "Сохраняй факты о доверии.", promptHistory);
Expect(summaryPrompt.Count == 2 && summaryPrompt[0].role == "system" && summaryPrompt[1].role == "user", "Summary должна состоять из системной инструкции и данных для обновления.");
Expect(summaryPrompt[0].content.Contains("[CHRONOLOGICAL EVENTS]", StringComparison.Ordinal), "Summary должна требовать стабильную структуру результата.");
Expect(summaryPrompt[1].content.Contains("Existing summary:\nНадя уже предложила кофе.", StringComparison.Ordinal), "Summary должна получать предыдущую сводку.");
Expect(summaryPrompt[1].content.Contains("USER: Мы всё ещё у старого маяка.", StringComparison.Ordinal), "Summary должна использовать текущие варианты сообщений, а не устаревший текст.");

var diaryPrompt = SoulMemoryPromptBuilder.BuildDiary("Надя", "Надя дорожит честностью в разговоре.", promptHistory);
Expect(diaryPrompt.Count == 2 && diaryPrompt[0].content.Contains("[SOUL MEMORY — PRIVATE DIARY]", StringComparison.Ordinal), "Дневник памяти должен иметь отдельную строгую системную инструкцию.");
Expect(diaryPrompt[0].content.Contains("<think> tags", StringComparison.Ordinal), "Дневник памяти должен запрещать служебные рассуждения.");
Expect(diaryPrompt[1].content.Contains("CURRENT CHARACTER MEMORY:\nНадя дорожит честностью", StringComparison.Ordinal), "Дневник памяти должен получать текущую память персонажа.");
Expect(diaryPrompt[1].content.Contains("USER: Мы всё ещё у старого маяка.", StringComparison.Ordinal), "Дневник памяти должен получать актуальные варианты реплик.");

var routerPrompt = SoulMemoryPromptBuilder.BuildRouter(new SoulMemoryRouterPromptInput("Надя", "Гость с книгой", "Знакомство", "Надя волнуется", "Пользователь любит кофе", [new SoulMemoryTopic { Key = "cafe", Content = "Они встретились в кафе." }], promptHistory), SoulMemoryPresetMode.From("full"));
Expect(routerPrompt.Count == 2 && routerPrompt[0].content.Contains("[SOUL MEMORY — ROUTER]", StringComparison.Ordinal), "Router памяти должен иметь отдельную JSON-инструкцию.");
Expect(routerPrompt[0].content.Contains("topic_plan", StringComparison.Ordinal), "Полный режим памяти должен разрешать планирование тематической памяти.");
Expect(routerPrompt[1].content.Contains("[cafe]\nОни встретились в кафе.", StringComparison.Ordinal), "Router памяти должен получать ограниченный список текущих тем.");
Expect(routerPrompt[1].content.Contains("NEW DIALOGUE DELTA:", StringComparison.Ordinal), "Router памяти должен получать свежую дельту диалога.");

var archivistPrompt = SoulMemoryPromptBuilder.BuildArchivist("Надя", "Надя волнуется", "cafe", "update", "Появился новый факт", "Они встретились в кафе.", promptHistory);
Expect(archivistPrompt.Count == 2 && archivistPrompt[0].content.Contains("[SOUL MEMORY — ARCHIVIST]", StringComparison.Ordinal), "Archivist памяти должен иметь отдельную строгую инструкцию.");
Expect(archivistPrompt[1].content.Contains("TOPIC KEY: cafe\nACTION: update\nREASON: Появился новый факт", StringComparison.Ordinal), "Archivist памяти должен получать план обновления темы.");
Expect(archivistPrompt[1].content.Contains("EXISTING TOPIC CONTENT:\nОни встретились в кафе.", StringComparison.Ordinal), "Archivist памяти должен получать существующее содержимое темы.");
Expect(archivistPrompt[1].content.Contains("USER: Мы всё ещё у старого маяка.", StringComparison.Ordinal), "Archivist памяти должен получать свежий диалог.");

var indexOnlyPass = SoulMemoryPromptBuilder.BuildCognitivePass(new CognitivePassPromptInput(
    "Надя", "Гость", "Знакомство", "Надя волнуется", "Гость любит кофе", "Старая summary", "Сохраняй доверие", "Лор маяка",
    [new SoulMemoryTopic { Key = "cafe", Content = "Встреча в кафе" }], promptHistory, [],
    UpdateIndex: true, UpdateDiary: false, UpdateSummary: false, PlanTopics: false), SoulMemoryPresetMode.From("index"));
Expect(indexOnlyPass[0].content.Contains("allowed to update only: character_memory, user_profile and healing_log", StringComparison.Ordinal), "Index only должен разрешать только индекс и профиль отношений.");
Expect(!indexOnlyPass[1].content.Contains("RELEVANT TOPIC MEMORIES", StringComparison.Ordinal) && !indexOnlyPass[1].content.Contains("CURRENT STORY SUMMARY", StringComparison.Ordinal), "Index only не должен передавать темы или Summary, когда они не обновляются.");
Expect(!indexOnlyPass[1].content.Contains("\"diary_entry\"", StringComparison.Ordinal) && !indexOnlyPass[1].content.Contains("\"topic_plan\"", StringComparison.Ordinal), "JSON-схема Index only не должна запрашивать отключённые части памяти.");

var fullCombinedPass = SoulMemoryPromptBuilder.BuildCognitivePass(new CognitivePassPromptInput(
    "Надя", "Гость", "Знакомство", "Надя волнуется", "Гость любит кофе", "Старая summary", "Сохраняй доверие", "Лор маяка",
    [new SoulMemoryTopic { Key = "cafe", Content = "Встреча в кафе" }], promptHistory, promptHistory,
    UpdateIndex: true, UpdateDiary: true, UpdateSummary: true, PlanTopics: true), SoulMemoryPresetMode.From("full"));
Expect(fullCombinedPass[1].content.Contains("CURRENT STORY SUMMARY", StringComparison.Ordinal) && fullCombinedPass[1].content.Contains("RELEVANT TOPIC MEMORIES", StringComparison.Ordinal), "Full должен передавать Summary и релевантные темы единому проходу.");
Expect(fullCombinedPass[1].content.Contains("\"diary_entry\"", StringComparison.Ordinal) && fullCombinedPass[1].content.Contains("\"topic_plan\"", StringComparison.Ordinal), "Full должен запросить дневник и план тем в том же JSON.");

var batchArchivist = SoulMemoryPromptBuilder.BuildArchivistBatch("Надя", "Надя волнуется",
    [new CognitiveTopicPlan("update", "cafe", "Новый факт"), new CognitiveTopicPlan("create", "lighthouse", "Новая локация")],
    [new SoulMemoryTopic { Key = "cafe", Content = "Старая встреча" }], promptHistory);
Expect(batchArchivist[0].content.Contains("BATCH ARCHIVIST", StringComparison.Ordinal) && batchArchivist[1].content.Contains("cafe", StringComparison.Ordinal) && batchArchivist[1].content.Contains("lighthouse", StringComparison.Ordinal), "Все тематические действия должны объединяться в один пакет Archivist.");
Expect(SoulMemoryPromptBuilder.MaximumGenerationPasses(true, true, true, true) == 2, "Полная память вместе с Summary должна требовать максимум два запроса.");
Expect(SoulMemoryPromptBuilder.MaximumGenerationPasses(true, false, false, false) == 1, "Профиль основных фактов должен требовать ровно один запрос.");
Expect(SoulMemoryPromptBuilder.MaximumGenerationPasses(false, false, true, false) == 1, "Отдельное обновление Summary должно требовать один запрос.");

for (var index = 0; index < 500; index++)
{
    var realisticDelay = MessagingTiming.RealisticReplyDelay(new string('а', index * 8), new Random(index + 17));
    Expect(realisticDelay >= TimeSpan.FromSeconds(3) && realisticDelay <= TimeSpan.FromSeconds(120), "Реалистичная задержка должна оставаться в диапазоне 3–120 секунд.");
    var proactiveDelay = MessagingTiming.NextProactiveDelay(new Random(index + 31));
    Expect(proactiveDelay >= TimeSpan.FromMinutes(20) && proactiveDelay <= TimeSpan.FromHours(5), "Инициативная задержка должна оставаться в диапазоне 20 минут–5 часов.");
}
for (var seed = 0; seed < 100; seed++)
    Expect(
        MessagingTiming.RealisticReplyDelay(new string('а', 20), new Random(seed)) <= TimeSpan.FromSeconds(40),
        "Короткая реплика до 20 символов не должна получать длинную задержку.");
Expect(
    MessagingTiming.RealisticReplyDelay(new string('а', 3000), new Random(42)) > MessagingTiming.RealisticReplyDelay("да", new Random(42)),
    "Длинное сообщение должно давать большую реалистичную задержку при одинаковом случайном компоненте.");

if (failures.Count > 0)
{
    Console.Error.WriteLine("Conversation fixture checks failed:");
    foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
    return 1;
}

Console.WriteLine("Conversation fixture checks passed.");
return 0;

static SoulMessage PromptMessage(int sequence, SoulMessageRole role, string author, string content, SoulMessageAuthorKind authorKind)
{
    var variant = new SoulMessageVariant { Content = content };
    return new SoulMessage
    {
        SequenceNumber = sequence,
        Role = role,
        AuthorKind = authorKind,
        AuthorName = author,
        CurrentVariantId = variant.Id,
        Variants = [variant]
    };
}

static ConversationMessageSnapshot CanonicalMessage(SoulMessage message, ConversationSnapshot conversation)
{
    var content = message.Variants.First(variant => variant.Id == message.CurrentVariantId).Content;
    var participant = message.AuthorKind == SoulMessageAuthorKind.Director
        ? conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.Director)
        : conversation.Participants.FirstOrDefault(value => value.Kind == ConversationParticipantKind.User);
    var variant = new ConversationMessageVariantSnapshot(message.CurrentVariantId, "Основной", content, message.CreatedAt);
    return new ConversationMessageSnapshot
    {
        Id = message.Id, SequenceNumber = message.SequenceNumber,
        Kind = message.AuthorKind == SoulMessageAuthorKind.Director ? ConversationMessageKind.DirectorEvent : ConversationMessageKind.Message,
        AuthorParticipantId = participant?.Id, AuthorKind = message.AuthorKind, AuthorPersonaId = message.AuthorPersonaId,
        AuthorName = message.AuthorName, Content = content, CreatedAt = message.CreatedAt,
        SelectedVariantId = variant.Id, Variants = [variant]
    };
}
