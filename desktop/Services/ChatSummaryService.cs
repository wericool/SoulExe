using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class ChatSummaryService
{
    private const int DefaultSummaryBatchSize = 5;
    private readonly JsonDataStore _store;
    public ChatSummaryService(JsonDataStore store) => _store = store;

    public async Task<SummaryUpdateResult> UpdateAsync(Guid characterId, Guid chatId, Func<IReadOnlyList<LlamaMessage>, CancellationToken, Task<string>> complete, CancellationToken token = default, bool force = false, int intervalMessages = DefaultSummaryBatchSize)
    {
        var interval = Math.Clamp(intervalMessages, 1, 100);
        var input = await _store.ReadAsync(root => CreateInput(root, characterId, chatId, interval), token);
        if (!force && input.PendingMessageCount < interval)
        {
            AppLog.Write($"COGNITIVE_SUMMARY_GATE character={characterId} chat={chatId} pendingDialogueMessages={input.PendingMessageCount} interval={interval} action=skip");
            return SummaryUpdateResult.NotNeeded(input.PendingMessageCount, interval);
        }
        AppLog.Write($"COGNITIVE_SUMMARY_GATE character={characterId} chat={chatId} pendingDialogueMessages={input.PendingMessageCount} chunkMessages={input.Messages.Count} interval={interval} action=run");
        var raw = await complete(BuildMessages(input), token);
        var summary = Clean(raw);
        if (summary.Length < 50) return SummaryUpdateResult.Failed("Модель вернула слишком короткую summary.");

        await _store.MutateAsync(root =>
        {
            var character = root.Characters.First(x => x.Id == characterId);
            var chat = character.Chats.First(x => x.Id == chatId);
            chat.SummaryText = summary;
            chat.LastSummarizedSequence = input.Messages.Max(x => x.SequenceNumber);
            chat.UpdatedAt = DateTimeOffset.Now;
        }, "chat_summary", token);
        return SummaryUpdateResult.Completed(summary.Length);
    }

    private static SummaryInput CreateInput(SoulDataRoot root, Guid characterId, Guid chatId, int interval)
    {
        var character = root.Characters.FirstOrDefault(x => x.Id == characterId) ?? throw new InvalidOperationException("Персонаж не найден.");
        var chat = character.Chats.FirstOrDefault(x => x.Id == chatId) ?? throw new InvalidOperationException("Чат не найден.");
        var pending = chat.Messages.Where(x => x.SequenceNumber > chat.LastSummarizedSequence).OrderBy(x => x.SequenceNumber).ToList();
        // Original summary processes one interval-sized chronological chunk and records its highest sequence only after success.
        var messages = pending.Take(interval).ToList();
        return new SummaryInput(character.Name, chat.SummaryText, chat.SummaryDirectives, messages, pending.Count);
    }

    private static IReadOnlyList<LlamaMessage> BuildMessages(SummaryInput input)
    {
        const string system = """
            You are an expert narrative archivist. Update an ongoing story summary using only confirmed dialogue facts. Return plain text only, without commentary.
            Never continue the story or write new dialogue. Preserve earlier established facts while adding the new developments. Keep the entire result under 500 words.
            Use these exact concise sections:
            [CHARACTER STATES & INVENTORY]
            [RELATIONSHIP DYNAMICS]
            [CURRENT SCENE & ATMOSPHERE]
            [KEY DISCOVERIES & LORE]
            [CHRONOLOGICAL EVENTS]
            Retain physical and emotional state, goals, trust, promises, locations, unresolved hooks, important world facts and causal events. Drop only pure filler. Do not invent facts or repeat details.
            """;
        var turns = string.Join("\n", input.Messages.Select(x => $"{(x.Role == SoulMessageRole.User ? "USER" : "CHARACTER")}: {CurrentContent(x)}"));
        var user = $"Existing summary:\n{input.ExistingSummary}\n\nDirectives:\n{input.Directives}\n\nNew turns:\n{turns}";
        return [new LlamaMessage("system", system), new LlamaMessage("user", user)];
    }

    private static string Clean(string text) => text.Trim().Trim('`').Trim();
    private static string CurrentContent(SoulMessage message) => (message.Variants.FirstOrDefault(x => x.Id == message.CurrentVariantId) ?? message.Variants.FirstOrDefault())?.Content ?? "";
    private sealed record SummaryInput(string CharacterName, string ExistingSummary, string Directives, IReadOnlyList<SoulMessage> Messages, int PendingMessageCount);
}

public sealed record SummaryUpdateResult(bool Updated, bool Skipped, string Status)
{
    public static SummaryUpdateResult NotNeeded(int messages, int interval) => new(false, true, $"До обновления summary осталось реплик пользователя: {Math.Max(0, interval - messages)}.");
    public static SummaryUpdateResult Failed(string status) => new(false, false, status);
    public static SummaryUpdateResult Completed(int chars) => new(true, false, $"Summary обновлена ({chars} символов).");
}
