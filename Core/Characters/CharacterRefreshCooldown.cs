using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Characters;

public sealed class CharacterRefreshCooldown
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _until = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _now;

    public CharacterRefreshCooldown(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.UtcNow);

    public bool TryBegin(string key, out IDisposable? lease, out TimeSpan remaining)
    {
        lock (_gate)
        {
            DateTimeOffset now = _now();
            bool running = _running.Contains(key);
            bool coolingDown = _until.TryGetValue(key, out DateTimeOffset until) && until > now;
            if (running || coolingDown)
            {
                lease = null;
                remaining = running ? TimeSpan.FromSeconds(1) : until - now;
                return false;
            }
            _running.Add(key);
            lease = new Lease(this, key);
            remaining = TimeSpan.Zero;
            return true;
        }
    }

    public void StartCooldown(string key, TimeSpan? duration = null)
    {
        TimeSpan effective = duration is { } value && value > TimeSpan.FromSeconds(10)
            ? value : TimeSpan.FromSeconds(10);
        lock (_gate) _until[key] = _now() + effective;
    }

    public TimeSpan Remaining(string key)
    {
        lock (_gate)
            return _until.TryGetValue(key, out DateTimeOffset until) && until > _now() ? until - _now() : TimeSpan.Zero;
    }

    private void End(string key) { lock (_gate) _running.Remove(key); }

    private sealed class Lease(CharacterRefreshCooldown owner, string key) : IDisposable
    {
        private bool _disposed;
        public void Dispose() { if (!_disposed) { _disposed = true; owner.End(key); } }
    }
}
