using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SoulExe.Models;

namespace SoulExe.Services;

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

        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.First(x => x.Id == chatId && x.Mode == ConversationMode.Personal);
            conversation.SummaryText = summary;
            conversation.LastSummarizedSequence = input.Messages.Max(x => x.SequenceNumber);
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "chat_summary", token);
        return SummaryUpdateResult.Completed(summary.Length);
    }

    private static SummaryInput CreateInput(SoulDataRoot root, Guid characterId, Guid chatId, int interval)
    {
        var character = root.Characters.FirstOrDefault(x => x.Id == characterId) ?? throw new InvalidOperationException("Персонаж не найден.");
        var conversation = root.Conversations.FirstOrDefault(x => x.Id == chatId && x.Mode == ConversationMode.Personal) ?? throw new InvalidOperationException("Личный разговор не найден.");
        var pending = conversation.Messages.Where(x => x.SequenceNumber > conversation.LastSummarizedSequence).OrderBy(x => x.SequenceNumber)
            .Select(message => ConversationMessageMapper.ToPersonalMessage(conversation, message)).ToList();
        // Original summary processes one interval-sized chronological chunk and records its highest sequence only after success.
        var messages = pending.Take(interval).ToList();
        return new SummaryInput(character.Name, conversation.SummaryText, conversation.Context.SummaryDirectives, messages, pending.Count);
    }

    private static IReadOnlyList<LlamaMessage> BuildMessages(SummaryInput input)
        => SummaryPromptBuilder.Build(input.ExistingSummary, input.Directives, input.Messages);

    private static string Clean(string text) => text.Trim().Trim('`').Trim();
    private sealed record SummaryInput(string CharacterName, string ExistingSummary, string Directives, IReadOnlyList<SoulMessage> Messages, int PendingMessageCount);
}

public sealed record SummaryUpdateResult(bool Updated, bool Skipped, string Status)
{
    public static SummaryUpdateResult NotNeeded(int messages, int interval) => new(false, true, $"До обновления summary осталось реплик пользователя: {Math.Max(0, interval - messages)}.");
    public static SummaryUpdateResult Failed(string status) => new(false, false, status);
    public static SummaryUpdateResult Completed(int chars) => new(true, false, $"Summary обновлена ({chars} символов).");
}
