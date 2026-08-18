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
    SeniorManagement = 3,
    Creator = 4
}

internal static class AccessResolver
{
    public static bool CanExecute(DiscordMember? member, ServerConfig config, string commandName)
    {
        if (member == null || string.IsNullOrWhiteSpace(commandName))
            return false;

        HashSet<ulong> userRoleIds = member.Roles.Select(role => role.Id).ToHashSet();
        if (config.RequiredServerRoleIds.Count > 0 && !config.RequiredServerRoleIds.All(userRoleIds.Contains))
            return false;

        string normalizedCommand = commandName.Trim().ToLowerInvariant().TrimStart('/');
        HashSet<string> effectiveCommands = GetEffectiveCommands(member, config);

        return effectiveCommands.Contains("*") || 
               effectiveCommands.Contains("all") || 
               effectiveCommands.Contains(normalizedCommand);
    }

    public static HashSet<string> GetEffectiveCommands(DiscordMember? member, ServerConfig config)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (member == null)
            return result;

        HashSet<ulong> userRoleIds = member.Roles.Select(role => role.Id).ToHashSet();
        if (config.RequiredServerRoleIds.Count > 0 && !config.RequiredServerRoleIds.All(userRoleIds.Contains))
            return result;

        var rolesById = (config.RolesPermissions ?? new List<RolePermissionConfig>())
            .ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

        // Find all roles directly assigned to user
        var matchedRoleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RolePermissionConfig role in config.RolesPermissions ?? new List<RolePermissionConfig>())
        {
            if (role.DiscordRoleIds.Any(userRoleIds.Contains))
                matchedRoleIds.Add(role.Id);
        }

        // Recursively resolve inheritance
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(matchedRoleIds);

        while (queue.Count > 0)
        {
            string currentId = queue.Dequeue();
            if (!visited.Add(currentId))
                continue;

            if (!rolesById.TryGetValue(currentId, out RolePermissionConfig? roleConfig) || roleConfig == null)
                continue;

            foreach (string cmd in roleConfig.AllowedCommands)
                result.Add(cmd);

            foreach (string inheritId in roleConfig.Inherits)
            {
                if (!visited.Contains(inheritId))
                    queue.Enqueue(inheritId);
            }
        }

        return result;
    }

    public static string GetHighestRoleDisplayName(DiscordMember? member, ServerConfig config)
    {
        if (member == null)
            return "Гость";

        HashSet<ulong> userRoleIds = member.Roles.Select(role => role.Id).ToHashSet();
        if (config.RequiredServerRoleIds.Count > 0 && !config.RequiredServerRoleIds.All(userRoleIds.Contains))
            return "Нет роли сервера";

        var matched = (config.RolesPermissions ?? new List<RolePermissionConfig>())
            .Where(r => r.DiscordRoleIds.Any(userRoleIds.Contains))
            .ToList();

        if (matched.Count == 0)
            return "Гость";

        return matched.Last().DisplayName;
    }

    public static BotAccessLevel GetAccess(DiscordMember? member, ServerConfig config)
    {
        if (member == null)
            return BotAccessLevel.Guest;

        HashSet<string> effective = GetEffectiveCommands(member, config);
        if (effective.Contains("*") || effective.Contains("all") || effective.Contains("setgroup"))
            return BotAccessLevel.Creator;
        if (effective.Contains("listranked"))
            return BotAccessLevel.Management;
        if (effective.Count > 0)
            return BotAccessLevel.Ra;

        return BotAccessLevel.Guest;
    }

    public static string ApiName(this BotAccessLevel access) => access switch
    {
        BotAccessLevel.Creator => "creator",
        BotAccessLevel.SeniorManagement => "senior_management",
        BotAccessLevel.Management => "management",
        BotAccessLevel.Ra => "ra",
        _ => "guest"
    };

    public static string DisplayName(this BotAccessLevel access) => access switch
    {
        BotAccessLevel.Creator => "Ключ Создателя",
        BotAccessLevel.SeniorManagement => "Высшее Руководство",
        BotAccessLevel.Management => "Руководство",
        BotAccessLevel.Ra => "RA",
        _ => "Гость"
    };
}
