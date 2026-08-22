using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoulTextWpf.Models;

namespace SoulTextWpf.Services;

public sealed class SceneService
{
    private readonly JsonDataStore _store;
    public SceneService(JsonDataStore store) => _store = store;

    public Task<IReadOnlyList<SoulScene>> GetScenesAsync(CancellationToken token = default) =>
        _store.ReadAsync(root => (IReadOnlyList<SoulScene>)(root.Scenes ?? []).OrderByDescending(scene => scene.UpdatedAt).ToList(), token);

    public Task<SoulScene?> GetSceneAsync(Guid sceneId, CancellationToken token = default) =>
        _store.ReadAsync(root => root.Scenes?.FirstOrDefault(scene => scene.Id == sceneId), token);

    public Task<SceneRuntime> GetRuntimeAsync(Guid sceneId, CancellationToken token = default) =>
        _store.ReadAsync(root =>
        {
            var scene = GetRequired(root, sceneId);
            var first = root.Characters.FirstOrDefault(character => character.Id == scene.CharacterAId) ?? throw new InvalidOperationException("Первый персонаж сцены не найден.");
            var second = root.Characters.FirstOrDefault(character => character.Id == scene.CharacterBId) ?? throw new InvalidOperationException("Второй персонаж сцены не найден.");
            var lore = (root.Lorebooks ?? []).ToDictionary(book => book.Id);
            return new SceneRuntime(scene, first, second, lore);
        }, token);

    public Task<SoulScene> CreateAsync(Guid firstCharacterId, Guid secondCharacterId, string name, string scenario, string location, string timeContext, string mood, string goal, Guid? firstSpeakerId, string turnMode, int delaySeconds, bool contract, string relationshipContext = "", bool advanceSceneAndAvoidRepetition = true, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            if (firstCharacterId == secondCharacterId) throw new InvalidOperationException("Для сцены нужны два разных персонажа.");
            var first = root.Characters.FirstOrDefault(character => character.Id == firstCharacterId) ?? throw new InvalidOperationException("Первый персонаж не найден.");
            var second = root.Characters.FirstOrDefault(character => character.Id == secondCharacterId) ?? throw new InvalidOperationException("Второй персонаж не найден.");
            var now = DateTimeOffset.Now;
            var scene = new SoulScene
            {
                Name = MakeUniqueName(root, string.IsNullOrWhiteSpace(name) ? $"{first.Name} и {second.Name}" : name.Trim()),
                CharacterAId = first.Id,
                CharacterBId = second.Id,
                Scenario = scenario?.Trim() ?? "",
                Location = location?.Trim() ?? "",
                TimeContext = timeContext?.Trim() ?? "",
                Mood = mood?.Trim() ?? "",
                Goal = goal?.Trim() ?? "",
                RelationshipContext = relationshipContext?.Trim() ?? "",
                TurnMode = string.Equals(turnMode, "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "alternate",
                DelaySeconds = Math.Clamp(delaySeconds, 0, 30),
                EnforceSceneContract = contract,
                AdvanceSceneAndAvoidRepetition = advanceSceneAndAvoidRepetition,
                NextCharacterId = firstSpeakerId is not null && (firstSpeakerId == first.Id || firstSpeakerId == second.Id) ? firstSpeakerId : first.Id,
                CreatedAt = now,
                UpdatedAt = now
            };
            root.Scenes ??= [];
            root.Scenes.Add(scene);
            return scene;
        }, "create_scene", token);

    public Task UpdateAsync(SoulScene draft, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var scene = GetRequired(root, draft.Id);
            scene.Name = string.IsNullOrWhiteSpace(draft.Name) ? scene.Name : draft.Name.Trim();
            scene.Scenario = draft.Scenario?.Trim() ?? "";
            scene.Location = draft.Location?.Trim() ?? "";
            scene.TimeContext = draft.TimeContext?.Trim() ?? "";
            scene.Mood = draft.Mood?.Trim() ?? "";
            scene.Goal = draft.Goal?.Trim() ?? "";
            scene.RelationshipContext = draft.RelationshipContext?.Trim() ?? "";
            scene.EnforceSceneContract = draft.EnforceSceneContract;
            scene.AdvanceSceneAndAvoidRepetition = draft.AdvanceSceneAndAvoidRepetition;
            scene.TurnMode = string.Equals(draft.TurnMode, "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "alternate";
            scene.DelaySeconds = Math.Clamp(draft.DelaySeconds, 0, 30);
            var now = DateTimeOffset.Now;
            scene.NextTurnAt = scene.Status == "running" && scene.TurnMode == "alternate" && scene.DelaySeconds >= 5
                ? now.AddSeconds(scene.DelaySeconds)
                : null;
            scene.UpdatedAt = now;
        }, "update_scene", token);

    public Task SetPinnedAsync(Guid sceneId, bool pinned, CancellationToken token = default) =>
        _store.MutateAsync(root =>
        {
            var scene = GetRequired(root, sceneId);
            scene.IsPinned = pinned;
        }, pinned ? "pin_scene" : "unpin_scene", token);

    public Task SetStatusAsync(Guid sceneId, string status, Guid? nextCharacterId = null, CancellationToken token = default, bool scheduleNextTurn = true) =>
        _store.MutateAsync(root =>
        {
            var scene = GetRequired(root, sceneId);
            var now = DateTimeOffset.Now;
            scene.Status = status is "running" or "finished" ? status : "paused";
            if (nextCharacterId is not null && (nextCharacterId == scene.CharacterAId || nextCharacterId == scene.CharacterBId)) scene.NextCharacterId = nextCharacterId;
            scene.NextTurnAt = scene.Status == "running" && scheduleNextTurn && scene.TurnMode == "alternate" && scene.DelaySeconds >= 5
                ? now.AddSeconds(Math.Clamp(scene.DelaySeconds, 5, 30))
                : null;
            scene.UpdatedAt = now;
        }, "set_scene_status", token);

    public Task DeleteAsync(Guid sceneId, CancellationToken token = default) =>
        _store.MutateAsync(root => root.Scenes?.RemoveAll(scene => scene.Id == sceneId), "delete_scene", token);

    public Task<SoulSceneMessage> AddCharacterMessageAsync(Guid sceneId, Guid speakerId, string content, CancellationToken token = default) =>
        AddMessageAsync(sceneId, SoulSceneMessageKind.Character, speakerId, null, content, token);

    public Task<SoulSceneMessage> AddDirectorMessageAsync(Guid sceneId, string content, CancellationToken token = default) =>
        AddMessageAsync(sceneId, SoulSceneMessageKind.Director, null, "Режиссёр", content, token);

    private Task<SoulSceneMessage> AddMessageAsync(Guid sceneId, SoulSceneMessageKind kind, Guid? speakerId, string? speakerName, string content, CancellationToken token) =>
        _store.MutateAsync(root =>
        {
            var scene = GetRequired(root, sceneId);
            var name = speakerName ?? root.Characters.FirstOrDefault(character => character.Id == speakerId)?.Name ?? "Персонаж";
            var now = DateTimeOffset.Now;
            var message = new SoulSceneMessage
            {
                Kind = kind,
                SpeakerCharacterId = speakerId,
                SpeakerName = name,
                Content = content.Trim(),
                SequenceNumber = scene.Messages.Count == 0 ? 1 : scene.Messages.Max(item => item.SequenceNumber) + 1,
                CreatedAt = now
            };
            scene.Messages.Add(message);
            scene.UpdatedAt = now;
            return message;
        }, kind == SoulSceneMessageKind.Director ? "add_scene_director_message" : "add_scene_character_message", token);

    public Task<SceneSummaryResult> UpdateSummaryAsync(Guid sceneId, Func<IReadOnlyList<LlamaMessage>, CancellationToken, Task<string>> complete, bool force = false, int intervalMessages = 6, CancellationToken token = default) =>
        UpdateSummaryCoreAsync(sceneId, complete, force, Math.Clamp(intervalMessages, 2, 20), token);

    private async Task<SceneSummaryResult> UpdateSummaryCoreAsync(Guid sceneId, Func<IReadOnlyList<LlamaMessage>, CancellationToken, Task<string>> complete, bool force, int interval, CancellationToken token)
    {
        var input = await _store.ReadAsync(root =>
        {
            var scene = GetRequired(root, sceneId);
            var pending = scene.Messages.Where(message => message.SequenceNumber > scene.LastSummarizedSequence).OrderBy(message => message.SequenceNumber).ToList();
            return new SceneSummaryInput(scene.Name, scene.Scenario, scene.RelationshipContext, CompactSummary(scene.SummaryText), pending.Take(interval).ToList(), pending.Count);
        }, token);
        if (!force && input.PendingCount < interval) return new SceneSummaryResult(false, "Summary сцены пока не требует обновления.");
        if (input.Messages.Count == 0) return new SceneSummaryResult(false, "В сцене нет новых реплик для Summary.");
        const string system = """
            You maintain a compact shared summary for a two-character roleplay scene. Return plain text only.
            Preserve confirmed facts from the scene; never invent dialogue, motives or events. Use exact headings:
            [SCENE STATE]
            [RELATIONSHIP DYNAMICS]
            [CURRENT SITUATION]
            [KEY EVENTS]
            Use exactly the 4 headings above. Under each heading write at most 2 short bullet points.
            Maximum 180 words and 1,200 characters total. Prefer preserving only the current state and facts required for the next few turns.
            Replace facts only when newer messages directly contradict them.
            """;
        var dialogue = string.Join("\n", input.Messages.Select(message => $"{message.SpeakerName}: {message.Content}"));
        var user = $"SCENE: {input.Name}\nSCENARIO: {input.Scenario}\nSHARED RELATIONSHIP: {input.RelationshipContext}\n\nEXISTING COMPACT SUMMARY:\n{input.ExistingSummary}\n\nNEW EVENTS:\n{dialogue}";
        var summary = CompactSummary((await complete([new LlamaMessage("system", system), new LlamaMessage("user", user)], token)).Trim());
        if (summary.Length < 20) return new SceneSummaryResult(false, "Summary сцены не обновлён: ответ модели слишком короткий.");
        await _store.MutateAsync(root =>
        {
            var scene = GetRequired(root, sceneId);
            scene.SummaryText = summary;
            scene.LastSummarizedSequence = input.Messages.Max(message => message.SequenceNumber);
            scene.UpdatedAt = DateTimeOffset.Now;
        }, "update_scene_summary", token);
        return new SceneSummaryResult(true, "Summary сцены обновлён.");
    }

    private static string CompactSummary(string? text)
    {
        var clean = (text ?? "").Trim();
        const int maxCharacters = 1200;
        if (clean.Length <= maxCharacters) return clean;
        var cut = clean.LastIndexOf(' ', maxCharacters - 1);
        if (cut < maxCharacters / 2) cut = maxCharacters;
        return clean[..cut].TrimEnd() + "…";
    }

    private static SoulScene GetRequired(SoulDataRoot root, Guid sceneId) => root.Scenes?.FirstOrDefault(scene => scene.Id == sceneId) ?? throw new InvalidOperationException("Сцена не найдена.");
    private static string MakeUniqueName(SoulDataRoot root, string candidate)
    {
        var name = candidate; var index = 2;
        while ((root.Scenes ?? []).Any(scene => string.Equals(scene.Name, name, StringComparison.CurrentCultureIgnoreCase))) name = $"{candidate} {index++}";
        return name;
    }
}

public sealed record SceneRuntime(SoulScene Scene, SoulCharacter First, SoulCharacter Second, IReadOnlyDictionary<Guid, SoulLorebook> Lorebooks);
public sealed record SceneSummaryResult(bool Updated, string Status);
internal sealed record SceneSummaryInput(string Name, string Scenario, string RelationshipContext, string ExistingSummary, IReadOnlyList<SoulSceneMessage> Messages, int PendingCount);
