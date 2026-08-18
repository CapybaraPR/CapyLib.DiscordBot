namespace AspectDiscordBot;

internal sealed class BotRuntime : IDisposable
{
    private static BotRuntime? _current;
    private readonly Dictionary<string, ServerRuntime> _serversById;

    private BotRuntime(BotConfig config, BotStateStore state)
    {
        Config = config;
        State = state;
        Servers = config.Servers.Select(server => new ServerRuntime(server)).ToList();
        _serversById = Servers.ToDictionary(server => server.Config.Id, StringComparer.OrdinalIgnoreCase);
    }

    public BotConfig Config { get; }
    public BotStateStore State { get; }
    public IReadOnlyList<ServerRuntime> Servers { get; }

    public static BotRuntime Current => _current ??
        throw new InvalidOperationException("BotRuntime ещё не инициализирован.");

    public static BotRuntime Initialize(BotConfig config, BotStateStore state)
    {
        if (_current != null)
            throw new InvalidOperationException("BotRuntime уже инициализирован.");

        _current = new BotRuntime(config, state);
        return _current;
    }

    public bool TryGetServer(string id, out ServerRuntime? server) =>
        _serversById.TryGetValue((id ?? string.Empty).Trim(), out server);

    public ServerRuntime GetServer(string id) => TryGetServer(id, out ServerRuntime? server) && server != null
        ? server
        : throw new InvalidOperationException($"Сервер '{id}' не настроен в bot-config.json.");

    public void Dispose()
    {
        foreach (ServerRuntime server in Servers)
            server.Dispose();
    }
}

internal sealed class ServerRuntime : IDisposable
{
    private List<string> _cachedGroups = new();
    private DateTime _groupsCacheExpires = DateTime.MinValue;
    private readonly SemaphoreSlim _groupsLock = new(1, 1);

    public ServerRuntime(ServerConfig config)
    {
        Config = config;
        Api = new BridgeApiClient(config);
    }

    public ServerConfig Config { get; }
    public BridgeApiClient Api { get; }

    public async Task<IReadOnlyList<string>> GetGroupsCachedAsync(CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow < _groupsCacheExpires && _cachedGroups.Count > 0)
            return _cachedGroups;

        await _groupsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (DateTime.UtcNow < _groupsCacheExpires && _cachedGroups.Count > 0)
                return _cachedGroups;

            try
            {
                GroupsResponse response = await Api.GetGroupsAsync(cancellationToken).ConfigureAwait(false);
                _cachedGroups = response.Groups ?? new List<string>();
                _groupsCacheExpires = DateTime.UtcNow.AddSeconds(30);
            }
            catch
            {
            }

            return _cachedGroups;
        }
        finally
        {
            _groupsLock.Release();
        }
    }

    public void Dispose()
    {
        _groupsLock.Dispose();
        Api.Dispose();
    }
}

internal enum BotAccessLevel
{
    Guest = 0,
    Ra = 1,
    Management = 2,
    Creator = 3
}

internal static class AccessResolver
{
    public static BotAccessLevel GetAccess(DiscordMember? member, ServerConfig config)
    {
        if (member == null)
            return BotAccessLevel.Guest;

        HashSet<ulong> roleIds = member.Roles.Select(role => role.Id).ToHashSet();
        if (!config.RequiredServerRoleIds.All(roleIds.Contains))
            return BotAccessLevel.Guest;
        if (config.CreatorKeyRoleIds.Any(roleIds.Contains))
            return BotAccessLevel.Creator;
        if (config.ManagementRoleIds.Any(roleIds.Contains))
            return BotAccessLevel.Management;
        if (config.RaRoleIds.Any(roleIds.Contains))
            return BotAccessLevel.Ra;
        return BotAccessLevel.Guest;
    }

    public static string ApiName(this BotAccessLevel access) => access switch
    {
        BotAccessLevel.Creator => "creator",
        BotAccessLevel.Management => "management",
        BotAccessLevel.Ra => "ra",
        _ => "guest"
    };

    public static string DisplayName(this BotAccessLevel access) => access switch
    {
        BotAccessLevel.Creator => "Ключ Создателя",
        BotAccessLevel.Management => "Руководство",
        BotAccessLevel.Ra => "RA",
        _ => "Гость"
    };
}
