using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SoulExe.Models;

namespace SoulExe.Services;

public sealed class StateVariableService
{
    private static readonly Regex StateBlock = new("<state_update>(.*?)</state_update>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private readonly JsonDataStore _store;
    public StateVariableService(JsonDataStore store) => _store = store;

    public string RemoveStateBlocks(string text) => StateBlock.Replace(text, "").Trim();

    public async Task<IReadOnlyDictionary<string, string>> ApplyFromResponseAsync(Guid characterId, Guid chatId, string response, CancellationToken token = default)
    {
        var matches = StateBlock.Matches(response);
        if (matches.Count == 0) return new Dictionary<string, string>();
        var applied = new Dictionary<string, string>();

        var character = await _store.ReadAsync(root => root.Characters.FirstOrDefault(x => x.Id == characterId), token)
            ?? throw new InvalidOperationException("Персонаж не найден.");
        await _store.MutateConversationsAsync(conversations =>
        {
            var conversation = conversations.FirstOrDefault(x => x.Id == chatId && x.Mode == ConversationMode.Personal)
                ?? throw new InvalidOperationException("Личный разговор не найден.");
            foreach (Match match in matches)
            {
                try
                {
                    using var document = JsonDocument.Parse(match.Groups[1].Value);
                    if (document.RootElement.ValueKind != JsonValueKind.Object) continue;
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        var variable = character.StateVariables.FirstOrDefault(x => string.Equals(x.Key, property.Name, StringComparison.Ordinal));
                        if (variable is null) continue;
                        var normalized = Normalize(variable, property.Value);
                        if (normalized is null) continue;
                        conversation.Context.StateValues[variable.Id] = normalized;
                        applied[variable.Key] = normalized;
                    }
                }
                catch (JsonException)
                {
                    // The chat reply stays valid even if the model produced a malformed state block.
                }
            }
            conversation.UpdatedAt = DateTimeOffset.Now;
        }, "apply_state_update", token);
        return applied;
    }

    private static string? Normalize(SoulStateVariable variable, JsonElement value)
    {
        try
        {
            return variable.VariableType switch
            {
                "int" when value.TryGetInt32(out var integer) => JsonSerializer.Serialize(integer),
                "bool" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => JsonSerializer.Serialize(value.GetBoolean()),
                "string" when value.ValueKind == JsonValueKind.String => JsonSerializer.Serialize(value.GetString() ?? ""),
                "list" when value.ValueKind == JsonValueKind.Array => value.GetRawText(),
                "enum" when value.ValueKind == JsonValueKind.String && IsAllowedEnumValue(variable, value.GetString()) => JsonSerializer.Serialize(value.GetString()),
                _ => null
            };
        }
        catch { return null; }
    }

    private static bool IsAllowedEnumValue(SoulStateVariable variable, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            using var rules = JsonDocument.Parse(variable.ValidationJson);
            if (!rules.RootElement.TryGetProperty("allowed", out var allowed) || allowed.ValueKind != JsonValueKind.Array) return true;
            return allowed.EnumerateArray().Any(x => string.Equals(x.GetString(), value, StringComparison.Ordinal));
        }
        catch { return true; }
    }
}
