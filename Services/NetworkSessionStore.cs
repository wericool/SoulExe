using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SoulExe.Services;

/// <summary>Bounds authenticated browser sessions without retaining request or identity data.</summary>
internal sealed class NetworkSessionStore
{
    private const int MaximumSessions = 256;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);

    public int Count
    {
        get
        {
            Cleanup(DateTimeOffset.UtcNow);
            return _sessions.Count;
        }
    }

    public void Add(string token, DateTimeOffset now)
    {
        Cleanup(now);
        _sessions[token] = now;
        TrimToMaximum();
    }

    public bool TryAuthorize(string token, DateTimeOffset now)
    {
        Cleanup(now);
        while (_sessions.TryGetValue(token, out var previous))
            if (_sessions.TryUpdate(token, now, previous)) return true;
        return false;
    }

    public void Clear() => _sessions.Clear();

    internal void Cleanup(DateTimeOffset now)
    {
        foreach (var session in _sessions)
            if (now - session.Value >= SessionLifetime)
                _sessions.TryRemove(session.Key, out _);
        TrimToMaximum();
    }

    private void TrimToMaximum()
    {
        var excess = _sessions.Count - MaximumSessions;
        if (excess <= 0) return;
        foreach (var session in _sessions.OrderBy(pair => pair.Value).Take(excess))
            _sessions.TryRemove(session.Key, out _);
    }
}
