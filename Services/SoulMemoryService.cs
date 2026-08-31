using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Local per-chat Soul Memory pipeline modelled after Soul of Waifu:
/// Router updates persistent indexes, Archivist updates topic memories, and Diary writes private reflections.
/// All state is stored atomically inside SoulExeData/soulexe.json.
/// </summary>
public sealed class SoulMemoryService
{
    private const int DefaultBatchSize = 4;
    private const int MessageOverlap = 2;
    private const int MaxDeltaMessages = 14;
    private const int MaxSnapshots = 5;
    private const int MaxAuditEntries = 60;
    private const int MaxDiaryEntries = 500;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UpdateLocks = new();
    private readonly JsonDataStore _store;

    public SoulMemoryService(JsonDataStore store) => _store = store;

    public async Task<MemoryUpdateResult> UpdateAfterConversationAsync(
        Guid characterId,
        Guid chatId,
        Func<IReadOnlyList<LlamaMessage>, CancellationToken, Task<string>> complete,
        CancellationToken token = default,
        bool force = false,
        int intervalMessages = DefaultBatchSize,
        string preset = "full",
        bool memoryEnabled = true,
        bool summaryEnabled = false,
        int summaryIntervalMessages = 5,
        bool forceSummary = false)
    {
        var mode = SoulMemoryPresetMode.From(preset);
        var interval = Math.Clamp(intervalMessages, 1, 50);
        var summaryInterval = Math.Clamp(summaryIntervalMessages, 1, 100);
        var key = $"{characterId:N}:{chatId:N}";
        var gate = UpdateLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var input = await _store.ReadAsync(root => BuildInput(root, characterId, chatId, summaryInterval), token);
            var memoryDue = memoryEnabled && input.NewMessages.Count > 0 && (force || input.NewMessages.Count >= interval);
            var summaryDue = summaryEnabled && input.SummaryMessages.Count > 0 && (forceSummary || input.PendingSummaryMessages >= summaryInterval);
            if (!memoryDue && !summaryDue)
            {
                AppLog.Write($"COGNITIVE_GATE character={characterId} chat={chatId} memory={input.NewMessages.Count}/{interval} summary={input.PendingSummaryMessages}/{summaryInterval} action=skip");
                return MemoryUpdateResult.NotNeeded(input.NewMessages.Count, interval, input.PendingSummaryMessages, summaryInterval, memoryEnabled, summaryEnabled);
            }

            var updateIndex = memoryDue && mode.UpdatesIndex;
            var updateDiary = memoryDue && mode.UpdatesDiary;
            var planTopics = memoryDue && mode.UpdatesTopics;
            var relevantTopics = planTopics
                ? MemoryTopicSelector.Select(input.Topics, FormatDialogue(input.Delta))
                : [];
            AppLog.Write($"COGNITIVE_PASS_START character={characterId} chat={chatId} mode={mode.Id} memoryDue={memoryDue} summaryDue={summaryDue} index={updateIndex} diary={updateDiary} topics={planTopics}");

            var pass = ParseCognitivePass(await complete(
                SoulMemoryPromptBuilder.BuildCognitivePass(new CognitivePassPromptInput(
                    input.CharacterName,
                    input.InitialUserProfile,
                    input.InitialRelationshipContext,
                    input.CharacterMemory,
                    input.UserProfile,
                    input.ExistingSummary,
                    input.SummaryDirectives,
                    input.LoreContext,
                    relevantTopics,
                    memoryDue ? input.Delta : [],
                    summaryDue ? input.SummaryMessages : [],
                    updateIndex,
                    updateDiary,
                    summaryDue,
                    planTopics), mode), token));

            if (pass.ParseFailed || (summaryDue && pass.Summary.Length < 50))
            {
                if (memoryDue)
                    await AddAuditAsync(characterId, chatId, "cognitive_pass", "parse_failed", "Объединённый проход вернул неполные данные; пакет будет повторён.", input.ThroughSequence, token);
                return MemoryUpdateResult.Failed("Обновление памяти не сохранено: модель вернула неполный ответ. Пакет будет повторён.");
            }

            var topicUpdates = new List<TopicUpdate>();
            if (planTopics && !pass.NoSignificantMemoryChange && pass.TopicPlan.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var batchMessages = SoulMemoryPromptBuilder.BuildArchivistBatch(
                    input.CharacterName,
                    string.IsNullOrWhiteSpace(pass.CharacterMemory) ? input.CharacterMemory : pass.CharacterMemory,
                    pass.TopicPlan,
                    input.Topics,
                    input.Delta);
                topicUpdates = ParseTopicUpdates(await complete(batchMessages, token));
                var returnedKeys = topicUpdates.Select(item => item.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (pass.TopicPlan.Any(plan => !returnedKeys.Contains(plan.Key)))
                    return MemoryUpdateResult.Failed("Тематическая память не сохранена: модель вернула неполный пакет тем. Обновление будет повторено целиком.");
            }

            await ApplyCombinedAsync(characterId, chatId, input, pass, topicUpdates, mode, memoryDue, summaryDue, token);
            var details = new List<string>();
            if (memoryDue)
            {
                if (updateIndex) details.Add(pass.NoSignificantMemoryChange ? "новых важных фактов нет" : "основная память обновлена");
                if (updateDiary) details.Add(pass.DiaryEntry.Length >= 20 ? "дневник дополнен" : "дневник без изменений");
                if (planTopics) details.Add($"тем обновлено: {topicUpdates.Count}");
            }
            if (summaryDue) details.Add("краткая история обновлена");
            var status = $"Когнитивное обновление завершено: {string.Join(", ", details)}.";
            AppLog.Write($"COGNITIVE_PASS_COMPLETE character={characterId} chat={chatId} mode={mode.Id} memory={memoryDue} summary={summaryDue} topics={topicUpdates.Count}");
            return new MemoryUpdateResult(true, false, status);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Write("Soul Memory pipeline failed.", ex);
            return MemoryUpdateResult.Failed("Не удалось обновить Soul Memory. Подробности сохранены в журнале.");
        }
        finally { gate.Release(); UpdateLocks.TryRemove(key, out _); }
    }

    private static MemoryInput BuildInput(SoulDataRoot root, Guid characterId, Guid chatId, int summaryInterval)
    {
        var character = root.Characters.FirstOrDefault(x => x.Id == characterId) ?? throw new InvalidOperationException("Персонаж не найден.");
        var conversation = root.Conversations.FirstOrDefault(x => x.Id == chatId && x.Mode == ConversationMode.Personal) ?? throw new InvalidOperationException("Личный разговор не найден.");
        var memory = EnsureMemory(conversation);
        var all = conversation.Messages.OrderBy(x => x.SequenceNumber).Select(message => ConversationMessageMapper.ToPersonalMessage(conversation, message)).ToList();
        var newMessages = all.Where(x => x.SequenceNumber > memory.LastProcessedSequence).ToList();
        var firstNewIndex = newMessages.Count == 0 ? all.Count : Math.Max(0, all.FindIndex(x => x.SequenceNumber > memory.LastProcessedSequence) - MessageOverlap);
        var delta = all.Skip(firstNewIndex).TakeLast(MaxDeltaMessages).ToList();
        var through = newMessages.Count == 0 ? memory.LastProcessedSequence : newMessages.Max(x => x.SequenceNumber);
        var pendingSummary = all.Where(x => x.SequenceNumber > conversation.LastSummarizedSequence).ToList();
        var summaryMessages = pendingSummary.Take(summaryInterval).ToList();
        var summaryThrough = summaryMessages.Count == 0 ? conversation.LastSummarizedSequence : summaryMessages.Max(x => x.SequenceNumber);
        var lore = BuildMemoryLore(root, character, delta);
        return new MemoryInput(character.Name, conversation.Context.InitialUserProfile, conversation.Context.InitialRelationshipContext,
            memory.CharacterMemory, memory.UserProfile, memory.HealingLog, CloneTopics(memory.Topics), newMessages, delta,
            memory.LastProcessedSequence, through, conversation.SummaryText, conversation.Context.SummaryDirectives,
            summaryMessages, pendingSummary.Count, summaryThrough, lore);
    }

    private static IReadOnlyList<LlamaMessage> BuildRouterMessages(MemoryInput input, SoulMemoryPresetMode mode)
        => SoulMemoryPromptBuilder.BuildRouter(new SoulMemoryRouterPromptInput(input.CharacterName, input.InitialUserProfile, input.InitialRelationshipContext, input.CharacterMemory, input.UserProfile, input.Topics, input.Delta), mode);

    private static IReadOnlyList<LlamaMessage> BuildArchivistMessages(MemoryInput input, RouterPayload router, TopicPlan plan)
    {
        var existing = input.Topics.FirstOrDefault(topic => string.Equals(topic.Key, plan.Key, StringComparison.OrdinalIgnoreCase))?.Content ?? "";
        return SoulMemoryPromptBuilder.BuildArchivist(input.CharacterName, router.CharacterMemory, plan.Key, plan.Action, plan.Summary, existing, input.Delta);
    }

    private static IReadOnlyList<LlamaMessage> BuildDiaryMessages(MemoryInput input)
        => SoulMemoryPromptBuilder.BuildDiary(input.CharacterName, input.CharacterMemory, input.Delta);

    private static CognitivePassPayload ParseCognitivePass(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(raw));
            var root = document.RootElement;
            var plan = new List<CognitiveTopicPlan>();
            if (root.TryGetProperty("topic_plan", out var topicPlan) && topicPlan.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in topicPlan.EnumerateArray().Take(3))
                {
                    var action = TextValue(item, "action").Trim().ToLowerInvariant();
                    var key = NormaliseTopicKey(TextValue(item, "key"));
                    var summary = TextValue(item, "summary").Trim();
                    if ((action is "create" or "update") && key.Length > 0)
                        plan.Add(new CognitiveTopicPlan(action, key, summary));
                }
            }
            return new CognitivePassPayload
            {
                NoSignificantMemoryChange = root.TryGetProperty("no_significant_memory_change", out var noChange) && noChange.ValueKind == JsonValueKind.True,
                CharacterMemory = TextValue(root, "character_memory").Trim(),
                UserProfile = TextValue(root, "user_profile").Trim(),
                HealingLog = TextValue(root, "healing_log").Trim(),
                DiaryEntry = CleanPlainText(TextValue(root, "diary_entry")),
                Summary = TextValue(root, "summary").Trim(),
                TopicPlan = plan
            };
        }
        catch
        {
            return new CognitivePassPayload { ParseFailed = true };
        }
    }

    private static List<TopicUpdate> ParseTopicUpdates(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(raw));
            if (!document.RootElement.TryGetProperty("topic_updates", out var updates) || updates.ValueKind != JsonValueKind.Array)
                return [];
            return updates.EnumerateArray()
                .Select(item => new TopicUpdate(NormaliseTopicKey(TextValue(item, "key")), TextValue(item, "content").Trim()))
                .Where(item => item.Key.Length > 0 && item.Content.Length >= 30)
                .Take(3)
                .ToList();
        }
        catch { return []; }
    }

    private async Task ApplyCombinedAsync(
        Guid characterId,
        Guid chatId,
        MemoryInput input,
        CognitivePassPayload payload,
        IReadOnlyList<TopicUpdate> topicUpdates,
        SoulMemoryPresetMode mode,
        bool memoryDue,
        bool summaryDue,
        CancellationToken token)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.First(value => value.Id == chatId && value.Mode == ConversationMode.Personal);
            var memory = EnsureMemory(conversation);
            if (memoryDue)
            {
                if (mode.UpdatesIndex && !payload.NoSignificantMemoryChange)
                {
                    memory.Snapshots.Add(new SoulMemorySnapshot
                    {
                        ThroughSequence = memory.LastProcessedSequence,
                        CharacterMemory = memory.CharacterMemory,
                        UserProfile = memory.UserProfile,
                        HealingLog = memory.HealingLog,
                        Topics = CloneTopics(memory.Topics)
                    });
                    while (memory.Snapshots.Count > MaxSnapshots) memory.Snapshots.RemoveAt(0);
                    if (payload.CharacterMemory.Length > 0) memory.CharacterMemory = Limit(payload.CharacterMemory, 6000);
                    if (payload.UserProfile.Length > 0) memory.UserProfile = Limit(payload.UserProfile, 5000);
                    if (payload.HealingLog.Length > 0) memory.HealingLog = Limit(payload.HealingLog, 2400);
                    memory.LastRouterUpdatedAt = DateTimeOffset.Now;
                }

                foreach (var update in topicUpdates)
                {
                    var plan = payload.TopicPlan.FirstOrDefault(item => string.Equals(item.Key, update.Key, StringComparison.OrdinalIgnoreCase));
                    if (plan is null) continue;
                    var existing = memory.Topics.FirstOrDefault(topic => string.Equals(topic.Key, update.Key, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                        memory.Topics.Add(new SoulMemoryTopic { Key = update.Key, Content = Limit(update.Content, 6000), SourceSummary = plan.Summary, MentionCount = 1 });
                    else
                    {
                        existing.Content = Limit(update.Content, 6000);
                        existing.SourceSummary = plan.Summary;
                        existing.MentionCount++;
                        existing.UpdatedAt = DateTimeOffset.Now;
                    }
                    AddAudit(memory, "archivist", "ok", $"Тема {update.Key} обновлена в общем пакете.", input.ThroughSequence);
                }

                if (mode.UpdatesDiary && payload.DiaryEntry.Length >= 20)
                {
                    memory.Diary.Add(new SoulDiaryEntry { Content = Limit(payload.DiaryEntry, 1800), ThroughSequence = input.ThroughSequence });
                    while (memory.Diary.Count > MaxDiaryEntries) memory.Diary.RemoveAt(0);
                    memory.LastDiaryUpdatedAt = DateTimeOffset.Now;
                    AddAudit(memory, "diary", "ok", "Добавлена личная рефлексия из общего когнитивного прохода.", input.ThroughSequence);
                }

                memory.LastProcessedSequence = Math.Max(memory.LastProcessedSequence, input.ThroughSequence);
                AddAudit(memory, "cognitive_pass", payload.NoSignificantMemoryChange ? "no_change" : "ok",
                    payload.NoSignificantMemoryChange ? "Новых важных фактов нет; разрешённые вспомогательные части обработаны." : "Разрешённые части памяти обновлены одним проходом.",
                    input.ThroughSequence);
                memory.UpdatedAt = DateTimeOffset.Now;
            }

            if (summaryDue)
            {
                conversation.SummaryText = Limit(payload.Summary, 12000);
                conversation.LastSummarizedSequence = Math.Max(conversation.LastSummarizedSequence, input.SummaryThroughSequence);
            }
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "cognitive_combined_pass", token);
    }

    private static string BuildMemoryLore(SoulDataRoot root, SoulCharacter character, IReadOnlyList<SoulMessage> dialogue)
    {
        var trigger = FormatDialogue(dialogue).ToLowerInvariant();
        var entries = (root.Lorebooks ?? [])
            .Where(book => character.LorebookIds.Contains(book.Id))
            .SelectMany(book => book.Entries ?? [])
            .Where(entry => entry.IsEnabled)
            .Where(entry =>
            {
                var mode = (entry.TriggerMode ?? "always").Trim().ToLowerInvariant();
                if (mode is "always" or "constant") return true;
                var keys = mode == "secondary" ? entry.SecondaryKeywords : entry.Keywords;
                return (keys ?? []).Any(key => !string.IsNullOrWhiteSpace(key) && trigger.Contains(key.Trim().ToLowerInvariant(), StringComparison.Ordinal));
            })
            .OrderBy(entry => entry.InsertionOrder)
            .Take(6)
            .Select(entry => $"[{entry.Name}] {Limit(entry.Content?.Trim() ?? string.Empty, 1200)}")
            .Where(content => content.Length > 3);
        return string.Join("\n", entries);
    }

    private static RouterPayload ParseRouter(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(raw));
            var root = document.RootElement;
            if (root.TryGetProperty("no_significant_change", out var noChange) && noChange.ValueKind == JsonValueKind.True)
                return new RouterPayload { NoSignificantChange = true };
            var plan = new List<TopicPlan>();
            if (root.TryGetProperty("topic_plan", out var topicPlan) && topicPlan.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in topicPlan.EnumerateArray())
                {
                    var action = TextValue(item, "action").Trim().ToLowerInvariant();
                    var key = NormaliseTopicKey(TextValue(item, "key"));
                    var summary = TextValue(item, "summary");
                    if ((action is "create" or "update") && !string.IsNullOrWhiteSpace(key)) plan.Add(new TopicPlan(action, key, summary));
                }
            }
            return new RouterPayload
            {
                CharacterMemory = TextValue(root, "character_memory"),
                UserProfile = TextValue(root, "user_profile"),
                HealingLog = TextValue(root, "healing_log"),
                TopicPlan = plan
            };
        }
        catch { return new RouterPayload { ParseFailed = true }; }
    }

    private async Task ApplyRouterAsync(Guid characterId, Guid chatId, MemoryInput input, RouterPayload payload, SoulMemoryPresetMode mode, CancellationToken token)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            var memory = GetMemory(conversations, chatId);
            if (mode.UpdatesIndex && !payload.NoSignificantChange)
            {
                memory.Snapshots.Add(new SoulMemorySnapshot
                {
                    ThroughSequence = memory.LastProcessedSequence,
                    CharacterMemory = memory.CharacterMemory,
                    UserProfile = memory.UserProfile,
                    HealingLog = memory.HealingLog,
                    Topics = CloneTopics(memory.Topics)
                });
                while (memory.Snapshots.Count > MaxSnapshots) memory.Snapshots.RemoveAt(0);
                if (!string.IsNullOrWhiteSpace(payload.CharacterMemory)) memory.CharacterMemory = payload.CharacterMemory.Trim();
                if (!string.IsNullOrWhiteSpace(payload.UserProfile)) memory.UserProfile = payload.UserProfile.Trim();
                if (!string.IsNullOrWhiteSpace(payload.HealingLog)) memory.HealingLog = payload.HealingLog.Trim();
                memory.LastRouterUpdatedAt = DateTimeOffset.Now;
            }
            memory.LastProcessedSequence = Math.Max(memory.LastProcessedSequence, input.ThroughSequence);
            AddAudit(memory, "router", payload.NoSignificantChange ? "no_change" : "ok", payload.NoSignificantChange ? "Новых значимых фактов нет." : $"Индекс обновлён; запланировано тем: {payload.TopicPlan.Count}.", input.ThroughSequence);
            memory.UpdatedAt = DateTimeOffset.Now;
        }, "soul_memory_router", token);
    }

    private async Task ApplyTopicAsync(Guid characterId, Guid chatId, TopicPlan plan, string content, int throughSequence, CancellationToken token)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            var memory = GetMemory(conversations, chatId);
            var existing = memory.Topics.FirstOrDefault(topic => string.Equals(topic.Key, plan.Key, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                memory.Topics.Add(new SoulMemoryTopic { Key = plan.Key, Content = Limit(content, 6000), SourceSummary = plan.Summary, MentionCount = 1 });
            }
            else
            {
                existing.Content = Limit(content, 6000);
                existing.SourceSummary = plan.Summary;
                existing.MentionCount++;
                existing.UpdatedAt = DateTimeOffset.Now;
            }
            AddAudit(memory, "archivist", "ok", $"Тема {plan.Key} {plan.Action}.", throughSequence);
            memory.UpdatedAt = DateTimeOffset.Now;
        }, "soul_memory_archivist", token);
    }

    private async Task ApplyDiaryAsync(Guid characterId, Guid chatId, string content, int throughSequence, CancellationToken token)
    {
        await _store.MutateConversationsAsync(conversations =>
        {
            var memory = GetMemory(conversations, chatId);
            memory.Diary.Add(new SoulDiaryEntry { Content = Limit(content, 1800), ThroughSequence = throughSequence });
            while (memory.Diary.Count > MaxDiaryEntries) memory.Diary.RemoveAt(0);
            memory.LastDiaryUpdatedAt = DateTimeOffset.Now;
            AddAudit(memory, "diary", "ok", "Добавлена личная рефлексия персонажа.", throughSequence);
            memory.UpdatedAt = DateTimeOffset.Now;
        }, "soul_memory_diary", token);
    }

    private async Task AddAuditAsync(Guid characterId, Guid chatId, string stage, string status, string details, int throughSequence, CancellationToken token) =>
        await _store.MutateConversationsAsync(conversations =>
        {
            var memory = GetMemory(conversations, chatId);
            AddAudit(memory, stage, status, details, throughSequence);
            memory.UpdatedAt = DateTimeOffset.Now;
        }, "soul_memory_audit", token);

    private static SoulMemoryBundle GetMemory(List<ConversationSnapshot> conversations, Guid chatId)
    {
        var conversation = conversations.First(value => value.Id == chatId && value.Mode == ConversationMode.Personal);
        return EnsureMemory(conversation);
    }

    private static SoulMemoryBundle EnsureMemory(ConversationSnapshot conversation)
    {
        var memory = conversation.Context.Memory ??= new SoulMemoryBundle();
        memory.Topics ??= [];
        memory.Diary ??= [];
        memory.Snapshots ??= [];
        memory.Audit ??= [];
        return memory;
    }

    private static void AddAudit(SoulMemoryBundle memory, string stage, string status, string details, int sequence)
    {
        memory.Audit.Add(new SoulMemoryAuditEntry { Stage = stage, Status = status, Details = details, ThroughSequence = sequence });
        while (memory.Audit.Count > MaxAuditEntries) memory.Audit.RemoveAt(0);
    }

    private static string FormatDialogue(IEnumerable<SoulMessage> messages) => string.Join("\n", messages.Select(message => $"{(message.Role == SoulMessageRole.User ? "USER" : message.Role == SoulMessageRole.Assistant ? "CHARACTER" : "SYSTEM")}: {CurrentContent(message)}"));
    private static string CurrentContent(SoulMessage message) => (message.Variants.FirstOrDefault(variant => variant.Id == message.CurrentVariantId) ?? message.Variants.FirstOrDefault())?.Content ?? "";

    private static string TextValue(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
    }

    private static string ExtractJson(string raw)
    {
        var text = (raw ?? "").Replace("<think>", "", StringComparison.OrdinalIgnoreCase).Replace("</think>", "", StringComparison.OrdinalIgnoreCase).Trim();
        text = Regex.Replace(text, "^```(?:json)?\\s*|```$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
        var first = text.IndexOf('{');
        var last = text.LastIndexOf('}');
        return first >= 0 && last > first ? text[first..(last + 1)] : text;
    }

    private static string CleanPlainText(string text) => string.Join(" ", (text ?? "").Replace("<think>", "", StringComparison.OrdinalIgnoreCase).Replace("</think>", "", StringComparison.OrdinalIgnoreCase).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    private static string Limit(string text, int max)
    {
        if (text.Length <= max) return text;
        if (max <= 0) return "";

        var wordBoundary = text.LastIndexOf(' ', max);
        var endIndex = wordBoundary > 0 ? wordBoundary : max;
        return text[..endIndex].TrimEnd();
    }
    private static string NormaliseTopicKey(string value)
    {
        var key = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), "[^a-zа-яё0-9_-]+", "_");
        key = key.Trim('_');
        return string.IsNullOrWhiteSpace(key) ? "" : key[..Math.Min(key.Length, 64)];
    }
    private static List<SoulMemoryTopic> CloneTopics(IEnumerable<SoulMemoryTopic> topics) => topics.Select(topic => new SoulMemoryTopic { Id = topic.Id, Key = topic.Key, Content = topic.Content, SourceSummary = topic.SourceSummary, MentionCount = topic.MentionCount, CreatedAt = topic.CreatedAt, UpdatedAt = topic.UpdatedAt, LastRetrievedAt = topic.LastRetrievedAt }).ToList();

    private sealed record MemoryInput(
        string CharacterName,
        string InitialUserProfile,
        string InitialRelationshipContext,
        string CharacterMemory,
        string UserProfile,
        string HealingLog,
        IReadOnlyList<SoulMemoryTopic> Topics,
        IReadOnlyList<SoulMessage> NewMessages,
        IReadOnlyList<SoulMessage> Delta,
        int LastProcessedSequence,
        int ThroughSequence,
        string ExistingSummary,
        string SummaryDirectives,
        IReadOnlyList<SoulMessage> SummaryMessages,
        int PendingSummaryMessages,
        int SummaryThroughSequence,
        string LoreContext);
    private sealed record TopicPlan(string Action, string Key, string Summary);
    private sealed record TopicUpdate(string Key, string Content);
    private sealed class CognitivePassPayload
    {
        public bool ParseFailed { get; init; }
        public bool NoSignificantMemoryChange { get; init; }
        public string CharacterMemory { get; init; } = "";
        public string UserProfile { get; init; } = "";
        public string HealingLog { get; init; } = "";
        public string DiaryEntry { get; init; } = "";
        public string Summary { get; init; } = "";
        public List<CognitiveTopicPlan> TopicPlan { get; init; } = [];
    }
    private sealed class RouterPayload
    {
        public static RouterPayload Empty { get; } = new();
        public bool NoSignificantChange { get; init; }
        public bool ParseFailed { get; init; }
        public string CharacterMemory { get; init; } = "";
        public string UserProfile { get; init; } = "";
        public string HealingLog { get; init; } = "";
        public List<TopicPlan> TopicPlan { get; init; } = [];
    }
}

public sealed record SoulMemoryPresetMode(string Id, string DisplayName, string Description, bool UpdatesIndex, bool UpdatesTopics, bool UpdatesDiary)
{
    public static IReadOnlyList<SoulMemoryPresetMode> All { get; } =
    [
        new("full", "Полная память", "Запоминает основные факты и отношения, ведёт личный дневник и отдельные воспоминания о важных людях, местах и событиях. Самый подробный вариант.", true, true, true),
        new("index-diary", "Факты и дневник", "Запоминает главное о персонаже, пользователе и отношениях и сохраняет личные впечатления. Отдельные тематические воспоминания не создаются.", true, false, true),
        new("index", "Только основные факты", "Обновляет факты о персонаже, пользователе и отношениях. Самый быстрый вариант без дневника и отдельных тем.", true, false, false),
        new("diary", "Только дневник", "Сохраняет личные впечатления персонажа от разговора. Основные факты, отношения и тематические воспоминания не изменяются.", false, false, true)
    ];

    public static SoulMemoryPresetMode From(string? id) => All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    public override string ToString() => DisplayName;
}

public sealed record MemoryUpdateResult(bool Updated, bool Skipped, string Status)
{
    public static MemoryUpdateResult NotNeeded(int count, int interval) => new(false, true, $"До обновления Soul Memory осталось реплик диалога: {Math.Max(0, interval - count)}.");
    public static MemoryUpdateResult NotNeeded(int memoryCount, int memoryInterval, int summaryCount, int summaryInterval, bool memoryEnabled, bool summaryEnabled)
    {
        var parts = new List<string>();
        if (memoryEnabled) parts.Add($"до памяти: {Math.Max(0, memoryInterval - memoryCount)}");
        if (summaryEnabled) parts.Add($"до краткой истории: {Math.Max(0, summaryInterval - summaryCount)}");
        return new(false, true, parts.Count == 0 ? "Автоматическое обновление отключено." : $"Обновление пока не требуется ({string.Join(", ", parts)} реплик).");
    }
    public static MemoryUpdateResult Failed(string text) => new(false, false, text);
}
