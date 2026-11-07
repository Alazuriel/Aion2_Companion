using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Combat;

/// <summary>
/// #19: Per-encounter player-death transitions. It never guesses player identity
/// and it does not infer resurrection from damage/healing done by a player.
/// One instance belongs to one encounter (paused fights retain their instance).
/// </summary>
internal sealed class PlayerDeathTracker
{
    private sealed class PlayerState
    {
        public required string Name { get; init; }
        public bool BaselineKnown { get; init; }
        public int Deaths { get; set; }
        public bool IsDead { get; set; }
        public DateTimeOffset? LastSignalAt { get; set; }
    }

    private readonly Dictionary<uint, PlayerState> _players = new();
    public bool Started { get; private set; }

    public void Reset()
    {
        _players.Clear();
        Started = false;
    }

    public void Start(IEnumerable<(uint Id, string Name)> presentAtPull, bool baselineKnown)
    {
        if (Started)
            return;

        Started = true;
        // Duplicate runtime IDs (e.g. a stale roster) must not become a trusted 0.
        var identities = new Dictionary<uint, string>();
        var conflicts = new HashSet<uint>();
        foreach (var (id, name) in presentAtPull)
        {
            if (id == 0 || string.IsNullOrWhiteSpace(name))
                continue;
            if (identities.TryGetValue(id, out string? prior) &&
                !string.Equals(prior, name, StringComparison.OrdinalIgnoreCase))
                conflicts.Add(id);
            else
                identities[id] = name;
        }

        foreach (var (id, name) in identities)
            _players[id] = new PlayerState
            {
                Name = name,
                BaselineKnown = baselineKnown && !conflicts.Contains(id),
            };
    }

    private PlayerState? GetOrRegister(uint id, string name)
    {
        if (!Started || id == 0 || string.IsNullOrWhiteSpace(name))
            return null;

        if (_players.TryGetValue(id, out PlayerState? state))
        {
            if (string.Equals(state.Name, name, StringComparison.OrdinalIgnoreCase))
                return state;

            // Runtime ID reused for another party member: never inherit deaths.
            _players.Remove(id);
        }

        state = new PlayerState { Name = name, BaselineKnown = false };
        _players[id] = state;
        return state;
    }

    public void ObserveDeath(uint id, string name, DateTimeOffset time)
    {
        PlayerState? state = GetOrRegister(id, name);
        if (state is null || (state.LastSignalAt.HasValue && time <= state.LastSignalAt.Value))
            return;

        state.LastSignalAt = time;
        if (state.IsDead)
            return; // duplicate Death/HP=0 for one continuous dead state

        state.IsDead = true;
        state.Deaths++;
    }

    public void ObserveHealth(uint id, string name, uint health, DateTimeOffset time)
    {
        if (health == 0)
        {
            ObserveDeath(id, name, time);
            return;
        }

        PlayerState? state = GetOrRegister(id, name);
        if (state is null || (state.LastSignalAt.HasValue && time <= state.LastSignalAt.Value))
            return;

        state.LastSignalAt = time;
        // Only an explicit nonzero HP observation can mark resurrection.
        state.IsDead = false;
    }

    public int? GetCount(uint id, string name)
    {
        PlayerState? state = GetOrRegister(id, name);
        return state?.BaselineKnown == true ? state.Deaths : null;
    }
}
