namespace AION_2_Companion.Core.GameData;

using System.Collections.Generic;
using System.Linq;

public sealed class ServerDefinition
{
    public ushort Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
    public string Faction { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public bool? ListedGlobal { get; init; }
}

public sealed class ServersDatabase
{
    private readonly Dictionary<ushort, ServerDefinition> _byId;

    private ServersDatabase(ServerDefinition[] servers)
    {
        Servers = servers;
        _byId = servers
            .GroupBy(server => server.Id)
            .ToDictionary(group => group.Key, group => group.First());
    }

    public IReadOnlyList<ServerDefinition> Servers { get; }

    public ServerDefinition? GetById(ushort id) =>
        _byId.TryGetValue(id, out ServerDefinition? server) ? server : null;

    public static ServersDatabase Load(string path) =>
        new(ForgeDataReader.LoadServers(path));
}
