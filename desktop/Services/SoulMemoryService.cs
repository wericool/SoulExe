using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Local per-chat Soul Memory pipeline modelled after Soul of Waifu:
/// Router updates persistent indexes, Archivist updates topic memories, and Diary writes private reflections.
/// All state is stored atomically inside SoulTextData/soultext.json.
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
        string preset = "full")
    {
        var mode = SoulMemoryPresetMode.From(preset);
        var interval = Math.Clamp(intervalMessages, 1, 50);
        var gate = UpdateLocks.GetOrAdd($"{characterId:N}:{chatId:N}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var input = await _store.ReadAsync(root => BuildInput(root, characterId, chatId), token);
            if (input.NewMessages.Count == 0)
                return MemoryUpdateResult.NotNeeded(0, interval);

            // Original Soul Memory batches dialogue turns, not only user messages.
            if (!force && input.NewMessages.Count < interval)
            {
                AppLog.Write($"SOUL_MEMORY_GATE character={characterId} chat={chatId} newDialogueMessages={input.NewMessages.Count} interval={interval} action=skip");
                return MemoryUpdateResult.NotNeeded(input.NewMessages.Count, interval);
            }

            AppLog.Write($"SOUL_MEMORY_PIPELINE_START character={characterId} chat={chatId} mode={mode.Id} newDialogueMessages={input.NewMessages.Count} delta={input.Delta.Count} through={input.ThroughSequence}");
            var router = mode.UpdatesIndex
                ? ParseRouter(await complete(BuildRouterMessages(input, mode), token))
                : RouterPayload.Empty;

            if (mode.UpdatesIndex && router.ParseFailed)
            {
                await AddAuditAsync(characterId, chatId, "router", "parse_failed", "Router вернул некорректный JSON; пакет будет повторён при следующем запуске.", input.ThroughSequence, token);
                return MemoryUpdateResult.Failed("Router памяти вернул некорректный JSON. Пакет не помечен обработанным и будет повторён.");
            }

            // A successful Router run always advances the tracker, including no-op batches, as in the original.
            await ApplyRouterAsync(characterId, chatId, input, router, mode, token);

            var topicsUpdated = 0;
            if (mode.UpdatesTopics && !router.NoSignificantChange)
            {
                foreach (var plan in router.TopicPlan.Take(5))
                {
                    token.ThrowIfCancellationRequested();
                    var archived = CleanPlainText(await complete(BuildArchivistMessages(input, router, plan), token));
                    if (archived.Length < 30)
                    {
                        await AddAuditAsync(characterId, chatId, "archivist", "skipped", $"Тема {plan.Key}: ответ слишком короткий.", input.ThroughSequence, token);
                        continue;
                    }
                    await ApplyTopicAsync(characterId, chatId, plan, archived, input.ThroughSequence, token);
                    topicsUpdated++;
                }
            }

            var diaryAdded = false;
            if (mode.UpdatesDiary)
            {
                var diary = CleanPlainText(await complete(BuildDiaryMessages(input), token));
                if (diary.Length >= 20)
                {
                    await ApplyDiaryAsync(characterId, chatId, diary, input.ThroughSequence, token);
                    diaryAdded = true;
                }
                else
                {
                    await AddAuditAsync(characterId, chatId, "diary", "skipped", "Дневниковая запись была пустой или слишком короткой.", input.ThroughSequence, token);
                }
            }

            var status = router.NoSignificantChange
                ? $"{mode.DisplayName}: новых значимых фактов нет; пакет отмечен обработанным."
                : $"{mode.DisplayName}: индекс сохранён; тем обновлено {topicsUpdated}; дневник {(diaryAdded ? "добавлен" : "не изменён")}.";
            AppLog.Write($"SOUL_MEMORY_PIPELINE_COMPLETE character={characterId} chat={chatId} mode={mode.Id} noChange={router.NoSignificantChange} topics={topicsUpdated} diary={diaryAdded}");
            return new MemoryUpdateResult(true, false, status);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Write("Soul Memory pipeline failed.", ex);
            return MemoryUpdateResult.Failed("Не удалось обновить Soul Memory. Подробности сохранены в журнале.");
        }
        finally { gate.Release(); }
    }

    private static MemoryInput BuildInput(SoulDataRoot root, Guid characterId, Guid chatId)
    {
        var character = root.Characters.FirstOrDefault(x => x.Id == characterId) ?? throw new InvalidOperationException("Персонаж не найден.");
        var chat = character.Chats.FirstOrDefault(x => x.Id == chatId) ?? throw new InvalidOperationException("Чат не найден.");
        chat.Memory ??= new SoulMemoryBundle();
        chat.Memory.Topics ??= [];
        chat.Memory.Diary ??= [];
        chat.Memory.Snapshots ??= [];
        chat.Memory.Audit ??= [];
        var all = chat.Messages.OrderBy(x => x.SequenceNumber).ToList();
        var newMessages = all.Where(x => x.SequenceNumber > chat.Memory.LastProcessedSequence).ToList();
        var firstNewIndex = newMessages.Count == 0 ? all.Count : Math.Max(0, all.FindIndex(x => x.SequenceNumber > chat.Memory.LastProcessedSequence) - MessageOverlap);
        var delta = all.Skip(firstNewIndex).TakeLast(MaxDeltaMessages).ToList();
        var through = newMessages.Count == 0 ? chat.Memory.LastProcessedSequence : newMessages.Max(x => x.SequenceNumber);
        return new MemoryInput(character.Name, chat.InitialUserProfile ?? "", chat.InitialRelationshipContext ?? "", chat.Memory.CharacterMemory, chat.Memory.UserProfile, chat.Memory.HealingLog, CloneTopics(chat.Memory.Topics), newMessages, delta, chat.Memory.LastProcessedSequence, through);
    }

    private static IReadOnlyList<LlamaMessage> BuildRouterMessages(MemoryInput input, SoulMemoryPresetMode mode)
    {
        var topicRule = mode.UpdatesTopics
            ? "Return topic_plan with at most five meaningful create or update actions. Each action has action (create/update), key (short stable slug) and summary. Do not create topics for filler."
            : "Do not return topic_plan; topics are disabled in this preset.";
        var system = """
            [SOUL MEMORY — ROUTER]
            You are a local analytical memory engine for a roleplay character. Return ONE valid JSON object and nothing else.
            Work only from confirmed dialogue facts. Never invent memories. Preserve important prior facts unless new dialogue contradicts them.
            The chat starting context is an initial baseline, not a permanent current fact. If later explicit dialogue gives a name, changes the relationship or contradicts that baseline, the newer dialogue must replace it in user_profile; preserve the old situation only as history when relevant.
            Update character_memory with the character's stable self-view, current emotional state, active agenda and unresolved tension.
            Update user_profile with known user facts, preferences, relationship dynamics, promises and shared milestones.
            healing_log records contradictions resolved by newer facts.
            For greetings, filler, repeated content or no meaningful change, return only {"no_significant_change":true}.
            {TOPIC_RULE}
            The JSON schema is:
            {"no_significant_change":false,"character_memory":"compact structured memory","user_profile":"compact structured relationship profile","healing_log":"resolved contradictions or No conflicts detected.","topic_plan":[{"action":"create","key":"short_slug","summary":"why this topic matters"}]}
            Active preset: {PRESET}.
            """
            .Replace("{TOPIC_RULE}", topicRule, StringComparison.Ordinal)
            .Replace("{PRESET}", mode.DisplayName, StringComparison.Ordinal);
        var relevant = string.Join("\n\n", input.Topics.Take(3).Select(topic => $"[{topic.Key}]\n{topic.Content}"));
        var dialogue = FormatDialogue(input.Delta);
        var user = $"""
            CHARACTER: {input.CharacterName}

            CHAT STARTING CONTEXT (initial only; newer dialogue overrides it):
            USER PROFILE: {input.InitialUserProfile}
            RELATIONSHIP: {input.InitialRelationshipContext}

            CURRENT CHARACTER MEMORY:
            {input.CharacterMemory}

            CURRENT USER PROFILE:
            {input.UserProfile}

            RECENT TOPIC MEMORIES:
            {relevant}

            NEW DIALOGUE DELTA:
            {dialogue}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    private static IReadOnlyList<LlamaMessage> BuildArchivistMessages(MemoryInput input, RouterPayload router, TopicPlan plan)
    {
        var existing = input.Topics.FirstOrDefault(topic => string.Equals(topic.Key, plan.Key, StringComparison.OrdinalIgnoreCase))?.Content ?? "";
        var system = """
            [SOUL MEMORY — ARCHIVIST]
            Write or update exactly one long-term topic memory. Return plain text only: no JSON, no markdown wrapper and no conversation.
            Keep it under 300 words. Store only confirmed, durable details: people, places, objects, promises, secrets, major events and their consequences.
            Merge with existing content, replacing facts contradicted by newer dialogue. Do not add filler or invent information.
            """;
        var user = $"""
            CHARACTER: {input.CharacterName}
            TOPIC KEY: {plan.Key}
            ACTION: {plan.Action}
            REASON: {plan.Summary}

            CURRENT CHARACTER MEMORY:
            {router.CharacterMemory}

            EXISTING TOPIC CONTENT:
            {existing}

            RECENT DIALOGUE:
            {FormatDialogue(input.Delta)}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    private static IReadOnlyList<LlamaMessage> BuildDiaryMessages(MemoryInput input)
    {
        var system = $"""
            [SOUL MEMORY — PRIVATE DIARY]
            You are {input.CharacterName}. Write a private reflection on the recent dialogue.
            Return only 4–6 first-person sentences. Focus on emotions, doubts, hopes and evolving feelings toward the user.
            Do not use asterisks, dialogue, headings, markdown or <think> tags. Do not invent events.
            """;
        var user = $"""
            CURRENT CHARACTER MEMORY:
            {input.CharacterMemory}

            RECENT DIALOGUE:
            {FormatDialogue(input.Delta)}
            """;
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
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
        await _store.MutateAsync(root =>
        {
            var memory = GetMemory(root, characterId, chatId);
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
        await _store.MutateAsync(root =>
        {
            var memory = GetMemory(root, characterId, chatId);
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
        await _store.MutateAsync(root =>
        {
            var memory = GetMemory(root, characterId, chatId);
            memory.Diary.Add(new SoulDiaryEntry { Content = Limit(content, 1800), ThroughSequence = throughSequence });
            while (memory.Diary.Count > MaxDiaryEntries) memory.Diary.RemoveAt(0);
            memory.LastDiaryUpdatedAt = DateTimeOffset.Now;
            AddAudit(memory, "diary", "ok", "Добавлена личная рефлексия персонажа.", throughSequence);
            memory.UpdatedAt = DateTimeOffset.Now;
        }, "soul_memory_diary", token);
    }

    private async Task AddAuditAsync(Guid characterId, Guid chatId, string stage, string status, string details, int throughSequence, CancellationToken token) =>
        await _store.MutateAsync(root =>
        {
            var memory = GetMemory(root, characterId, chatId);
            AddAudit(memory, stage, status, details, throughSequence);
            memory.UpdatedAt = DateTimeOffset.Now;
        }, "soul_memory_audit", token);

    private static SoulMemoryBundle GetMemory(SoulDataRoot root, Guid characterId, Guid chatId)
    {
        var chat = root.Characters.First(character => character.Id == characterId).Chats.First(chat => chat.Id == chatId);
        chat.Memory ??= new SoulMemoryBundle();
        chat.Memory.Topics ??= [];
        chat.Memory.Diary ??= [];
        chat.Memory.Snapshots ??= [];
        chat.Memory.Audit ??= [];
        return chat.Memory;
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

    private sealed record MemoryInput(string CharacterName, string InitialUserProfile, string InitialRelationshipContext, string CharacterMemory, string UserProfile, string HealingLog, IReadOnlyList<SoulMemoryTopic> Topics, IReadOnlyList<SoulMessage> NewMessages, IReadOnlyList<SoulMessage> Delta, int LastProcessedSequence, int ThroughSequence);
    private sealed record TopicPlan(string Action, string Key, string Summary);
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
        new("full", "Full", "Router + Archivist + Diary: индекс памяти, профиль пользователя, тематические воспоминания и личный дневник.", true, true, true),
        new("index-diary", "Index + Diary", "Router + Diary: индекс памяти и личный дневник без тематических воспоминаний.", true, false, true),
        new("index", "Index only", "Только Router: индекс памяти и профиль отношений без тем и дневника.", true, false, false),
        new("diary", "Diary only", "Только личные рефлексии персонажа; основной индекс и темы не изменяются.", false, false, true)
    ];

    public static SoulMemoryPresetMode From(string? id) => All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
    public override string ToString() => DisplayName;
}

public sealed record MemoryUpdateResult(bool Updated, bool Skipped, string Status)
{
    public static MemoryUpdateResult NotNeeded(int count, int interval) => new(false, true, $"До обновления Soul Memory осталось реплик диалога: {Math.Max(0, interval - count)}.");
    public static MemoryUpdateResult Failed(string text) => new(false, false, text);
}
