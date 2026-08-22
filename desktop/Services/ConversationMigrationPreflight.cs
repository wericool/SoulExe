using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

/// <summary>
/// Validates whether a legacy root can be represented as conversations before any schema-changing
/// migration is introduced. It is deliberately pure: callers may run it on a clone or fixture.
/// </summary>
public static class ConversationMigrationPreflight
{
    public static ConversationMigrationPreflightReport Analyze(SoulDataRoot root, ConversationReadService? reader = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        reader ??= new ConversationReadService();
        var issues = new List<string>();
        var directChats = root.Characters.Sum(character => character.Chats?.Count ?? 0);
        var scenes = root.Scenes?.Count ?? 0;
        var expected = directChats + scenes;
        var conversations = reader.ReadAll(root);

        if (conversations.Count != expected)
            issues.Add($"Ожидалось разговоров: {expected}; проекция вернула: {conversations.Count}.");
        if (conversations.Select(conversation => conversation.Id).Distinct().Count() != conversations.Count)
            issues.Add("Идентификаторы разговоров не уникальны.");

        foreach (var conversation in conversations)
        {
            if (conversation.Kind == ConversationKind.Direct && conversation.Participants.Count != 2)
                issues.Add($"Обычный чат {conversation.Id:N} не содержит двух участников.");
            if (conversation.Kind == ConversationKind.Scene && conversation.Participants.Count < 3)
                issues.Add($"Сцена {conversation.Id:N} не содержит обоих персонажей и режиссёра.");
            if (conversation.Messages.Select(message => message.SequenceNumber).Distinct().Count() != conversation.Messages.Count)
                issues.Add($"В разговоре {conversation.Id:N} повторяются номера сообщений.");
            if (conversation.Messages.Any(message => string.IsNullOrWhiteSpace(message.Content)))
                issues.Add($"В разговоре {conversation.Id:N} найдена пустая реплика.");
        }

        return new ConversationMigrationPreflightReport(root.SchemaVersion, expected, conversations.Count, issues);
    }
}

public sealed record ConversationMigrationPreflightReport(int SourceSchemaVersion, int ExpectedConversationCount, int ProjectedConversationCount, IReadOnlyList<string> Issues)
{
    public bool IsSafeToPrepareBackup => Issues.Count == 0;
}
