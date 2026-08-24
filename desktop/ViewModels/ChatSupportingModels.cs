using SoulExe.Models;
using SoulExe.Services;

namespace SoulExe.ViewModels;

public sealed record ComposerAuthorOption(string Key, string DisplayName, SoulMessageAuthorKind Kind, Guid? PersonaId, string AvatarPath)
{
    public static ComposerAuthorOption User { get; } = new("user", "Вы", SoulMessageAuthorKind.User, null, "");
    public static ComposerAuthorOption Director { get; } = new("director", "Режиссёр", SoulMessageAuthorKind.Director, null, "");
}

internal sealed record ModelDownloadRequest(string RepositoryId, ModelHubFile File, string? RecommendationName, bool IsInitialSetup, bool IsRecommended);

public sealed record GatewayCategoryOption(string Id, string Title, string Description);

public sealed class StateVariableContextItem
{
    public StateVariableContextItem(string displayName, string key, string valueJson, string variableType)
    {
        DisplayName = displayName;
        Key = key;
        ValueJson = valueJson;
        VariableType = variableType;
    }

    public string DisplayName { get; }
    public string Key { get; }
    public string ValueJson { get; }
    public string VariableType { get; }
}
