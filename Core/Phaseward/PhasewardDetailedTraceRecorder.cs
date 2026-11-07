using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Phaseward;

/// <summary>
/// Phaseward research trace V2. Captures ALL non-DPS decoded frames for the boss attempt,
/// and ALL decoded frames (including DPS frames) within [cast-10s, cast+10s].
/// Raw packet bodies are retained ONLY in local compressed archives after a separate opt-in.
/// They are NEVER passed to the v1 community outbox or transmitted over the network.
/// </summary>
public static class PhasewardDetailedTraceRecorder
{
    public const int SchemaVersion = 2;
    public const int CastWindowSeconds = 10;
    public const int MaxFramesPerFight = 100_000;
    public const long MaxPayloadBytesPerFight = 64L * 1024 * 1024;
    private const int MaxRecentFrames = 16_000;
    private const long MaxRecentBytes = 12L * 1024 * 1024;
    private const int MaxArchives = 12;
    private const int MaxCastMarkers = 4096;
    private const int MaxBossHpMarkers = 100_000;

    private sealed record Frame(long Sequence, DateTimeOffset Time, ushort Opcode,
        string Name, bool Known, bool Dps, bool IsCast, int Flags, byte[] Payload);
    private sealed record BossHp(DateTimeOffset Time, uint Current, long Maximum);
    private sealed record Cast(DateTimeOffset Time, uint SkillId);
    private sealed class Encounter
    {
        public int NpcId;
        public uint RuntimeId;
        public DateTimeOffset Started;
        public Dictionary<long, Frame> Kept = new();
        public List<BossHp> Hp = new();
        public List<Cast> Casts = new();
        public long Bytes;
        public long LostFrames;
    }

    private static readonly object Sync = new();
    private static readonly Queue<Frame> Recent = new();
    private static long _recentBytes;
    private static long _sequence;
    private static Encounter? _active;

    public static string DirectoryPath => Path.Combine(
        CompanionSettings.SettingsDirectory, "phaseward-detailed-local");

    public static bool IsDpsOpcode(Opcode opcode) =>
        opcode == OpcodeId.Hit || opcode == OpcodeId.DoT || opcode == OpcodeId.HitNotice;

    /// <summary>Called at the decoder entry point BEFORE UI throttling/dropping unknown opcodes.</summary>
    public static void ObserveFrame(WireFrame frame)
    {
        if (!CompanionSettings.PhasewardDetailedTraceEnabled)
        {
            lock (Sync) { _active = null; Recent.Clear(); _recentBytes = 0; }
            return;
        }
        byte[] payload = frame.Payload.ToArray();
        lock (Sync)
        {
            Frame item = new(++_sequence, frame.Time, frame.Opcode.Value,
                OpcodeCatalog.GetName(frame.Opcode), OpcodeCatalog.IsKnown(frame.Opcode),
                IsDpsOpcode(frame.Opcode), frame.Opcode == OpcodeId.Cast,
                (int)frame.Flags, payload);
            Recent.Enqueue(item);
            _recentBytes += payload.Length;
            // Identify boss-originated casts on the decoder thread, BEFORE UI throttling.
            // The UI callback remains a deduplicated fallback.
            if (_active is { } casting && frame.Opcode == OpcodeId.Cast &&
                PhasewardCastParser.TryParse(payload, out PhasewardCast parsed) &&
                parsed.ActorRuntimeId == casting.RuntimeId)
                MarkCastCore(casting, parsed.SkillId, frame.Time);
            // Keep a bounded rolling buffer for the ten seconds BEFORE a recognized cast.
            while (Recent.Count > 0 &&
                   ((frame.Time - Recent.Peek().Time).TotalSeconds > CastWindowSeconds ||
                    Recent.Count > MaxRecentFrames || _recentBytes > MaxRecentBytes))
            {
                Frame removed = Recent.Dequeue();
                _recentBytes -= removed.Payload.Length;
                // A full packet was evicted from the pre-cast ring: signal incomplete evidence.
                if (_active is not null && removed.Time >= _active.Started &&
                    _active.Casts.Any(c => removed.Time >= c.Time.AddSeconds(-CastWindowSeconds) &&
                                           removed.Time <= c.Time.AddSeconds(CastWindowSeconds)) &&
                    !_active.Kept.ContainsKey(removed.Sequence))
                    _active.LostFrames++;
            }
            if (_active is { } active && frame.Time >= active.Started)
            {
                if (!item.Dps || InCastWindow(active, frame.Time))
                    Keep(active, item);
            }
        }
    }

    public static void Begin(int bossNpcId, uint runtimeId, DateTimeOffset at)
    {
        if (!CompanionSettings.PhasewardDetailedTraceEnabled || bossNpcId <= 0) return;
        lock (Sync)
        {
            _active = new Encounter { NpcId = bossNpcId, RuntimeId = runtimeId, Started = at };
            // The decoder can be ahead of the UI combat recognizer. Reconcile the pre-roll.
            foreach (Frame item in Recent)
                if (item.Time >= at && !item.Dps) Keep(_active, item);
            // Recognition can lag behind decoded frames; recover already-seen casts.
            foreach (Frame item in Recent.ToArray())
                if (item.Time >= at && item.IsCast &&
                    PhasewardCastParser.TryParse(item.Payload, out PhasewardCast cast) &&
                    cast.ActorRuntimeId == runtimeId)
                    MarkCastCore(_active, cast.SkillId, item.Time);
        }
    }

    public static void MarkCast(uint skillId, DateTimeOffset at)
    {
        if (skillId == 0) return;
        lock (Sync)
            if (_active is { } active && at >= active.Started)
                MarkCastCore(active, skillId, at);
    }

    private static void MarkCastCore(Encounter active, uint skillId, DateTimeOffset at)
    {
        if (active.Casts.Any(c => c.Time == at && c.SkillId == skillId)) return;
        if (active.Casts.Count >= MaxCastMarkers) { active.LostFrames++; return; }
        active.Casts.Add(new Cast(at, skillId));
        DateTimeOffset from = at.AddSeconds(-CastWindowSeconds);
        foreach (Frame item in Recent)
            if (item.Time >= active.Started && item.Time >= from && item.Time <= at)
                Keep(active, item);
    }

    public static void MarkBossHp(uint current, long maximum, DateTimeOffset at)
    {
        if (maximum <= 0 || current > maximum) return;
        lock (Sync)
        {
            if (_active is { } active && at >= active.Started)
            {
                if (active.Hp.Count < MaxBossHpMarkers)
                    active.Hp.Add(new BossHp(at, current, maximum));
                else active.LostFrames++;
            }
        }
    }

    public static void Cancel()
    {
        lock (Sync) { _active = null; }
    }

    /// <summary>Called for automatically finalized fights, never manual capture stop.</summary>
    public static void Finish(string outcome)
    {
        Encounter? completed;
        lock (Sync) { completed = _active; _active = null; }
        if (completed is null || completed.Kept.Count == 0) return;
        var frames = completed.Kept.Values.OrderBy(x => x.Sequence).ToArray();
        var casts = completed.Casts.OrderBy(x => x.Time).ToArray();
        var hp = completed.Hp.OrderBy(x => x.Time).ToArray();
        var end = DateTimeOffset.UtcNow;
        int npcId = completed.NpcId;
        DateTimeOffset start = completed.Started;
        long lost = completed.LostFrames;
        string result = outcome is "killed" or "wipe" ? outcome : "unknown";
        // Account ownership is captured at fight finalization, not later on a different login.
        string? accountId = PhasewardTraceV2Sync.ConnectedAccountId;
        // Compress OFF the UI/capture threads. No blocking network work on capture path.
        _ = Task.Run(() => WriteArchive(npcId, start, end, result, lost, frames, casts, hp, accountId));
    }

    private static bool InCastWindow(Encounter encounter, DateTimeOffset at) =>
        encounter.Casts.Any(c => at >= c.Time.AddSeconds(-CastWindowSeconds) &&
                                 at <= c.Time.AddSeconds(CastWindowSeconds));

    private static void Keep(Encounter encounter, Frame item)
    {
        if (encounter.Kept.ContainsKey(item.Sequence)) return;
        if (encounter.Kept.Count >= MaxFramesPerFight ||
            encounter.Bytes + item.Payload.Length > MaxPayloadBytesPerFight)
        { encounter.LostFrames++; return; }
        encounter.Kept.Add(item.Sequence, item);
        encounter.Bytes += item.Payload.Length;
    }

    private static void WriteArchive(int npcId, DateTimeOffset start, DateTimeOffset end,
        string outcome, long lost, Frame[] frames, Cast[] casts, BossHp[] hp, string? accountId)
    {
        string? temp = null;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string id = Guid.NewGuid().ToString("N");
            string path = Path.Combine(DirectoryPath,
                $"phaseward-{npcId}-{start.ToUniversalTime():yyyyMMdd-HHmmss}-{id}.jsonl.gz");
            temp = path + ".tmp";
            using (FileStream file = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (GZipStream gz = new(file, CompressionLevel.Fastest, leaveOpen: false))
            using (StreamWriter writer = new(gz, new UTF8Encoding(false)))
            {
                Write(writer, new {
                    record_type = "header", schema_version = SchemaVersion,
                    boss_npc_id = npcId, started_at = start.ToUniversalTime().ToString("O"),
                    finished_at = end.ToUniversalTime().ToString("O"), outcome,
                    baseline = "all_non_dps_frames", cast_window_before_ms = 10_000,
                    cast_window_after_ms = 10_000, window_includes_dps = true,
                    frames_saved = frames.Length, cast_count = casts.Length,
                    boss_hp_count = hp.Length, lost_frames = lost,
                    // Decoder/encounter attribution is not independently verified.
                    capture_complete = false, truncated = lost > 0,
                    raw_payloads = "LOCAL_ONLY_NEVER_V1_UPLOAD",
                    raw_payloads_may_contain_personal_data = true,
                    endpoint_and_player_name_fields_omitted = true
                });
                foreach (Cast cast in casts)
                    Write(writer, new { record_type = "cast", t_ms = Ms(start, cast.Time), skill_id = cast.SkillId });
                foreach (BossHp item in hp)
                    Write(writer, new { record_type = "boss_hp", t_ms = Ms(start, item.Time),
                        current = item.Current, maximum = item.Maximum,
                        hp_pct = Math.Round(item.Current * 100.0 / item.Maximum, 3) });
                foreach (Frame item in frames)
                {
                    bool window = casts.Any(c => item.Time >= c.Time.AddSeconds(-CastWindowSeconds) &&
                                                       item.Time <= c.Time.AddSeconds(CastWindowSeconds));
                    string scope = window ? (item.Dps ? "cast_window" : "baseline_and_cast_window") : "baseline";
                    Write(writer, new { record_type = "frame", seq = item.Sequence,
                        t_ms = Ms(start, item.Time), opcode = item.Opcode,
                        opcode_name = item.Name, known_opcode = item.Known, flags = item.Flags,
                        scope, payload_length = item.Payload.Length,
                        payload_b64 = Convert.ToBase64String(item.Payload) });
                }
            }
            File.Move(temp, path);
            temp = null;
            // Dedicated immutable account-bound research queue, only after separate V2 opt-in.
            // No old local archive is silently backfilled.
            PhasewardTraceV2Sync.TryQueueCompletedArchive(path, accountId);
            // Bounded local retention; does not touch NSL or the v1 community outbox.
            var files = new DirectoryInfo(DirectoryPath)
                .GetFiles("phaseward-*.jsonl.gz").OrderByDescending(f => f.CreationTimeUtc).ToArray();
            foreach (FileInfo old in files.Skip(MaxArchives))
                try { old.Delete(); } catch (IOException) { }
        }
        catch (Exception) { /* Research logging may not disrupt gameplay. */ }
        finally
        {
            if (temp is not null) try { File.Delete(temp); } catch (IOException) { }
        }
    }

    private static long Ms(DateTimeOffset started, DateTimeOffset at) =>
        (long)Math.Round((at - started).TotalMilliseconds);
    private static void Write(StreamWriter writer, object value) =>
        writer.WriteLine(JsonSerializer.Serialize(value));
}
