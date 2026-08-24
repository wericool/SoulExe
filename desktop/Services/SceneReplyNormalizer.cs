using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>Shared scene post-generation pass used by desktop and network scene turns.</summary>
public static class SceneReplyNormalizer
{
    public static Func<SoulCharacter, string, string> Create(StateVariableService stateVariables) =>
        (speaker, raw) => ResponseFormatter.NormalizeSceneReply(
            raw,
            speaker.Name,
            speaker.UseRoleplayResponseFormatting,
            stateVariables.RemoveStateBlocks);
}
