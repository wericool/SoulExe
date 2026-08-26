using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Temporary prompt-engine input until BuildGroup accepts ConversationSnapshot directly.</summary>
public sealed record GroupConversationRuntime(
    ConversationSnapshot Conversation,
    SoulCharacter First,
    SoulCharacter Second,
    IReadOnlyDictionary<Guid, SoulLorebook> Lorebooks,
    IReadOnlyDictionary<Guid, SoulPersona> Personas);

public sealed record SceneSummaryResult(bool Updated, string Status);
