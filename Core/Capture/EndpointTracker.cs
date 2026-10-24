
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace AION_2_Companion.Core.Capture;

public sealed class EndpointTracker
{
    private readonly object _lock = new();

    private readonly Dictionary<string, EndpointStats> _endpoints = new();

    public void Observe(CapturedPacket packet)
    {
        if (!TryParseEndpoint(packet.Source, out var source))
            return;

        if (!TryParseEndpoint(packet.Destination, out var destination))
            return;

        lock (_lock)
        {
            Update(source, packet.Payload.Length, packet.Time);
            Update(destination, packet.Payload.Length, packet.Time);
        }
    }

    public IReadOnlyList<EndpointStats> GetSnapshot()
    {
        lock (_lock)
        {
            return _endpoints.Values
                .OrderByDescending(x => x.PacketCount)
                .Select(x => x.Copy())
                .ToArray();
        }
    }

    private void Update(
        IPEndPoint endpoint,
        int payloadLength,
        DateTimeOffset time)
    {
        var key = endpoint.ToString();

        if (!_endpoints.TryGetValue(key, out var stats))
        {
            stats = new EndpointStats(endpoint);
            _endpoints[key] = stats;
        }

        stats.PacketCount++;
        stats.TotalBytes += payloadLength;
        stats.LastSeen = time;
    }

    private static bool TryParseEndpoint(
        string value,
        out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.None, 0);

        if (!IPEndPoint.TryParse(value, out var parsed) ||
            parsed is null)
            return false;

        // IPv6 kizárása
        if (parsed.Address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        endpoint = parsed;
        return true;
    }
}

public sealed class EndpointStats
{
    public IPEndPoint Endpoint { get; }
    public long PacketCount { get; internal set; }
    public long TotalBytes { get; internal set; }
    public DateTimeOffset LastSeen { get; internal set; }

    public EndpointStats(IPEndPoint endpoint)
    {
        Endpoint = endpoint;
    }

    public EndpointStats Copy()
    {
        return new EndpointStats(Endpoint)
        {
            PacketCount = PacketCount,
            TotalBytes = TotalBytes,
            LastSeen = LastSeen
        };
    }
}
