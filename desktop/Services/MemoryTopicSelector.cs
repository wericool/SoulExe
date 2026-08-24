using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SoulExe.Models;

namespace SoulExe.Services;

/// <summary>
/// Local RAG-style selector for per-chat topic memories.
/// Small topic sets are passed intact, matching the original fallback. Larger sets are ranked by query/key/content overlap and recency.
/// This has no cloud dependency and stays entirely inside SoulExeData.
/// </summary>
public static class MemoryTopicSelector
{
    private const int PassAllThreshold = 4;
    private const int DefaultMaximum = 3;

    public static IReadOnlyList<SoulMemoryTopic> Select(IReadOnlyList<SoulMemoryTopic>? topics, string? query, int maximum = DefaultMaximum)
    {
        var available = (topics ?? []).Where(topic => !string.IsNullOrWhiteSpace(topic.Content)).ToList();
        if (available.Count <= PassAllThreshold)
            return available.OrderByDescending(topic => topic.UpdatedAt).ToList();

        var queryTerms = Terms(query).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return available
            .Select(topic => new
            {
                Topic = topic,
                Score = Score(topic, queryTerms)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Topic.UpdatedAt)
            .Take(Math.Clamp(maximum, 1, 8))
            .Select(item => item.Topic)
            .ToList();
    }

    private static double Score(SoulMemoryTopic topic, IReadOnlySet<string> queryTerms)
    {
        if (queryTerms.Count == 0) return 0;
        var keyTerms = Terms(topic.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var contentTerms = Terms(topic.Content).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keyMatches = queryTerms.Count(term => keyTerms.Contains(term));
        var contentMatches = queryTerms.Count(term => contentTerms.Contains(term));
        return keyMatches * 4d + contentMatches + Math.Min(topic.MentionCount, 10) * .05d;
    }

    private static IEnumerable<string> Terms(string? text) => Regex.Matches(text ?? "", "[\\p{L}\\p{Nd}_-]{3,}")
        .Select(match => match.Value.Trim().ToLowerInvariant())
        .Where(value => value.Length >= 3);
}
