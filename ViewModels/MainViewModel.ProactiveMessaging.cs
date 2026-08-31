using System.Globalization;
using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed partial class MainViewModel
{
    private readonly CancellationTokenSource _proactiveLoopCts = new();
    private Task? _proactiveLoopTask;

    private void StartProactiveMessaging() =>
        _proactiveLoopTask ??= Task.Run(() => RunProactiveLoopAsync(_proactiveLoopCts.Token));

    private async Task RunProactiveLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
        while (!token.IsCancellationRequested)
        {
            try { await ProcessOneDueProactiveConversationAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { AppLog.Write("Proactive messaging cycle failed", ex); }
            try { await timer.WaitForNextTickAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessOneDueProactiveConversationAsync(CancellationToken token)
    {
        var now = DateTimeOffset.Now;
        var candidates = await _store.ReadAsync(root =>
        {
            var characters = (root.Characters ?? []).Where(value => value.ProactiveMessagesEnabled).ToDictionary(value => value.Id);
            return (root.Conversations ?? [])
                .Where(value => value.Mode == ConversationMode.Personal && value.Messages.Count > 0)
                .Select(value => new
                {
                    Conversation = value,
                    CharacterId = value.Participants.Where(p => p.Kind == ConversationParticipantKind.Character).Select(p => p.CharacterId).FirstOrDefault()
                })
                .Where(value => value.CharacterId is not null && characters.ContainsKey(value.CharacterId.Value))
                .Select(value => (value.Conversation.Id, Character: characters[value.CharacterId!.Value], Last: value.Conversation.Messages.OrderBy(m => m.SequenceNumber).Last()))
                .ToList();
        }, token).ConfigureAwait(false);

        foreach (var candidate in candidates)
        {
            var due = await PrepareProactiveScheduleAsync(candidate.Id, candidate.Last.Id, candidate.Character, now, token).ConfigureAwait(false);
            if (!due) continue;
            await GenerateProactiveMessageAsync(candidate.Id, candidate.Character, token).ConfigureAwait(false);
            return; // Keep local-model work serial and predictable.
        }
    }

    private async Task<bool> PrepareProactiveScheduleAsync(Guid conversationId, Guid latestMessageId, SoulCharacter character, DateTimeOffset now, CancellationToken token)
    {
        return await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.First(value => value.Id == conversationId);
            conversation.Context ??= new ConversationContextSnapshot();
            conversation.Context.Proactive ??= new ProactiveConversationState();
            var state = conversation.Context.Proactive;
            var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!string.Equals(state.DailyCountDate, today, StringComparison.Ordinal))
            {
                state.DailyCountDate = today;
                state.SentToday = 0;
                if (state.NextAttemptAt is null) state.NextAttemptAt = now + MessagingTiming.NextProactiveDelay();
            }

            if (state.ScheduledAfterMessageId != latestMessageId)
            {
                state.ScheduledAfterMessageId = latestMessageId;
                state.NextAttemptAt = now + MessagingTiming.NextProactiveDelay();
                return false;
            }

            if (state.SentToday >= 3)
            {
                state.NextAttemptAt = null;
                return false;
            }
            if (state.NextAttemptAt is null)
            {
                state.NextAttemptAt = now + MessagingTiming.NextProactiveDelay();
                return false;
            }
            if (state.NextAttemptAt > now) return false;
            if (TryMovePastQuietHours(character, now, out var afterQuiet))
            {
                state.NextAttemptAt = afterQuiet;
                return false;
            }

            // Claim the due item. A failure receives a short retry instead of
            // firing on every scheduler tick.
            state.NextAttemptAt = now.AddMinutes(5);
            return true;
        }, "prepare_proactive_message", token).ConfigureAwait(false);
    }

    private async Task GenerateProactiveMessageAsync(Guid conversationId, SoulCharacter character, CancellationToken token)
    {
        const string directive = "A meaningful amount of real time has passed since the last message. Without mentioning timers, systems, prompts, or this instruction, initiate one natural in-character message to the user now. Base it strictly on the established relationship, recent conversation, memory, current context, and your own personality. Do not assume the user performed any new action while absent.";
        var settings = await BuildLlamaSettingsAsync().ConfigureAwait(false);
        var generationId = "proactive_" + Guid.NewGuid().ToString("N")[..10];
        var result = await _conversationTurnRunner.RunPersonalTurnAsync(
            character.Id,
            conversationId,
            string.Empty,
            isContinuation: true,
            settings.ContextSize,
            settings.MaxTokens,
            (messages, cancellation) => GenerateWithPromptPolicyAsync(settings, messages, cancellation, generationId),
            async (raw, cancellation) => await DirectChatResponseFinalizer.FinalizeAsync(
                _stateVariables, character.Id, conversationId, raw, character.UseRoleplayResponseFormatting, cancellation),
            persistAssistant: true,
            hiddenDirective: directive,
            token: token).ConfigureAwait(false);
        if (result.Status != DirectTurnExecutionStatus.Completed || result.SavedMessage is null) return;

        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.First(value => value.Id == conversationId);
            var state = conversation.Context.Proactive ??= new ProactiveConversationState();
            var now = DateTimeOffset.Now;
            var today = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (state.DailyCountDate != today) { state.DailyCountDate = today; state.SentToday = 0; }
            state.SentToday = Math.Min(3, state.SentToday + 1);
            var latest = conversation.Messages.OrderBy(message => message.SequenceNumber).Last();
            state.ScheduledAfterMessageId = latest.Id;
            state.NextAttemptAt = state.SentToday < 3 ? now + MessagingTiming.NextProactiveDelay() : null;
            return true;
        }, "complete_proactive_message", token).ConfigureAwait(false);

        await RefreshDesktopAfterNetworkMutationAsync().ConfigureAwait(false);
        _ = ScheduleCognitiveMaintenanceAfterReplyAsync(character.Id, conversationId);
    }

    private static bool TryMovePastQuietHours(SoulCharacter character, DateTimeOffset now, out DateTimeOffset afterQuiet)
    {
        afterQuiet = default;
        if (!character.ProactiveQuietHoursEnabled) return false;
        if (!TimeOnly.TryParseExact(character.ProactiveQuietHoursStart, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !TimeOnly.TryParseExact(character.ProactiveQuietHoursEnd, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || start == end)
            return false;
        var local = TimeOnly.FromDateTime(now.LocalDateTime);
        var crossesMidnight = start > end;
        var quiet = crossesMidnight ? local >= start || local < end : local >= start && local < end;
        if (!quiet) return false;
        var endDate = now.Date;
        if (crossesMidnight && local >= start) endDate = endDate.AddDays(1);
        afterQuiet = new DateTimeOffset(endDate + end.ToTimeSpan(), now.Offset).AddMinutes(Random.Shared.Next(1, 16));
        return true;
    }
}
