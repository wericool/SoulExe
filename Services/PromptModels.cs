using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Data contract for the one canonical conversation prompt builder.</summary>
public sealed record PromptBuildRequest(
    SoulCharacter Character,
    ConversationSnapshot Conversation,
    SoulPersona? Persona,
    SoulPromptPreset? Preset,
    IReadOnlyList<SoulLorebook> Lorebooks,
    IReadOnlyList<SoulMemoryTopic> RelevantMemoryTopics,
    string UserMessage,
    int ContextSize,
    int ReservedGenerationTokens,
    bool IncludeSoulMemory = true,
    bool IncludeAutoSummary = true,
    bool ExcludeLastUserMessage = true,
    bool AppendUserMessage = true,
    bool IsContinuation = false);

public sealed record GroupPromptBuildRequest(
    ConversationSnapshot Conversation,
    SoulCharacter First,
    SoulCharacter Second,
    IReadOnlyDictionary<Guid, SoulLorebook> Lorebooks,
    Guid ActiveCharacterId,
    int ContextSize,
    int ReservedGenerationTokens,
    IReadOnlyDictionary<Guid, SoulPersona>? Personas = null);

public sealed record PromptBuildResult(IReadOnlyList<LlamaMessage> Messages, IReadOnlyList<PromptDiagnostic> Diagnostics);
public sealed record PromptDiagnostic(string Category, string Text);
