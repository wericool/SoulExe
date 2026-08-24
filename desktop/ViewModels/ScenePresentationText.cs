using SoulExe.Models;

namespace SoulExe.ViewModels;

/// <summary>Short labels for the group-conversation workspace header.</summary>
public static class ScenePresentationText
{
    public static string StartPause(ConversationSnapshot? conversation) =>
        conversation?.TurnState?.Status == SceneStatus.Running ? "Пауза" : "Старт";

    public static string LastMessage(ConversationSnapshot? conversation)
    {
        var message = conversation?.Messages?
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.SequenceNumber)
            .FirstOrDefault();
        return message is null
            ? "Последнее сообщение: пока нет"
            : $"Последнее сообщение · {message.CreatedAt.LocalDateTime:dd.MM.yyyy · HH:mm}";
    }

    public static string Countdown(int seconds) =>
        seconds > 0 ? $"Следующая реплика через {seconds} сек." : string.Empty;

    public static string NextSpeakerName(ConversationSnapshot? conversation, IEnumerable<SoulCharacter> characters)
    {
        var characterId = conversation?.FindParticipant(conversation.TurnState?.NextParticipantId)?.CharacterId;
        if (characterId is null) return "Выберите следующего говорящего";
        return characters.FirstOrDefault(character => character.Id == characterId)?.Name ?? "Персонаж";
    }
}
