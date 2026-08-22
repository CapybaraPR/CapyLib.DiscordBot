namespace AspectDiscordBot;

internal sealed class BotConfig
{
    [JsonPropertyName("bot_token")]
    public string BotToken { get; set; } = "CHANGE_ME";

    [JsonPropertyName("guild_id")]
    public ulong GuildId { get; set; }

    [JsonPropertyName("status_refresh_seconds")]
    public int StatusRefreshSeconds { get; set; } = 30;

    [JsonPropertyName("presence_rotate_seconds")]
    public int PresenceRotateSeconds { get; set; } = 10;

    [JsonPropertyName("ephemeral_command_responses")]
    public bool EphemeralCommandResponses { get; set; } = true;

    [JsonPropertyName("show_player_names")]
    public bool ShowPlayerNames { get; set; } = true;

    [JsonPropertyName("log_poll_seconds")]
    public int LogPollSeconds { get; set; } = 2;

    [JsonPropertyName("log_batch_size")]
    public int LogBatchSize { get; set; } = 25;

    [JsonPropertyName("send_buffered_logs_on_start")]
    public bool SendBufferedLogsOnStart { get; set; }

    [JsonPropertyName("link_role_sync_seconds")]
    public int LinkRoleSyncSeconds { get; set; } = 60;

    [JsonPropertyName("owner_dm_user_ids")]
    public List<ulong> OwnerDmUserIds { get; set; } = new();

    [JsonPropertyName("health_check_interval_seconds")]
    public int HealthCheckIntervalSeconds { get; set; } = 60;

    [JsonPropertyName("health_alert_failures_threshold")]
    public int HealthAlertFailuresThreshold { get; set; } = 3;

    [JsonPropertyName("assignable_groups")]
    public List<AssignableGroup> AssignableGroups { get; set; } = new();

    [JsonPropertyName("servers")]
    public List<ServerConfig> Servers { get; set; } = new();

    // Legacy single-server fields are migrated to an NR entry when servers is absent.
    [JsonPropertyName("status_channel_id")]
    public ulong StatusChannelId { get; set; }

    [JsonPropertyName("status_message_id")]
    public ulong StatusMessageId { get; set; }

    [JsonPropertyName("audit_channel_id")]
    public ulong AuditChannelId { get; set; }

    [JsonPropertyName("api_base_url")]
    public string ApiBaseUrl { get; set; } = "http://127.0.0.1:8123/";

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = "CHANGE_ME";

    [JsonPropertyName("ra_role_ids")]
    public List<ulong> RaRoleIds { get; set; } = new();

    [JsonPropertyName("management_role_ids")]
    public List<ulong> ManagementRoleIds { get; set; } = new();

    [JsonPropertyName("creator_key_role_ids")]
    public List<ulong> CreatorKeyRoleIds { get; set; } = new();

    [JsonPropertyName("roles_permissions")]
    public List<RolePermissionConfig> RolesPermissions { get; set; } = new();

    [JsonPropertyName("log_channels")]
    public LogChannelsConfig LogChannels { get; set; } = new();

    public static BotConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Конфигурация не найдена: {Path.GetFullPath(path)}", path);

        string json = File.ReadAllText(path, Encoding.UTF8);
        BotConfig? config = JsonSerializer.Deserialize<BotConfig>(json, JsonDefaults.Options);
        if (config == null)
            throw new InvalidDataException("Конфигурация бота пуста или содержит некорректный JSON.");

        config.Validate();
        return config;
    }

    private void Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(BotToken) || BotToken.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            errors.Add("bot_token не заполнен");
        if (GuildId == 0)
            errors.Add("guild_id не заполнен");
        if (StatusRefreshSeconds is < 10 or > 300)
            errors.Add("status_refresh_seconds должен быть от 10 до 300");
        if (PresenceRotateSeconds is < 5 or > 300)
            errors.Add("presence_rotate_seconds должен быть от 5 до 300");
        if (LogPollSeconds is < 1 or > 60)
            errors.Add("log_poll_seconds должен быть от 1 до 60");
        if (LogBatchSize is < 1 or > 100)
            errors.Add("log_batch_size должен быть от 1 до 100");
        if (LinkRoleSyncSeconds is < 15 or > 3600)
            errors.Add("link_role_sync_seconds должен быть от 15 до 3600");
        if (HealthCheckIntervalSeconds is < 15 or > 3600)
            errors.Add("health_check_interval_seconds должен быть от 15 до 3600");
        if (HealthAlertFailuresThreshold is < 1 or > 60)
            errors.Add("health_alert_failures_threshold должен быть от 1 до 60");

        OwnerDmUserIds = (OwnerDmUserIds ?? new List<ulong>())
            .Where(id => id != 0)
            .Distinct()
            .ToList();

        Servers ??= new List<ServerConfig>();
        if (Servers.Count == 0)
            Servers.Add(BuildLegacyServer());

        if (Servers.Count > 2)
            errors.Add("эта версия поддерживает не более двух серверов: nr и mrp");

        for (int index = 0; index < Servers.Count; index++)
            Servers[index].NormalizeAndValidate(errors, $"servers[{index}]");

        List<string> duplicateIds = Servers
            .GroupBy(server => server.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateIds.Count > 0)
            errors.Add("повторяющиеся server id: " + string.Join(", ", duplicateIds));

        List<ulong> duplicateMessageIds = Servers
            .Where(server => server.StatusMessageId != 0)
            .GroupBy(server => server.StatusMessageId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicateMessageIds.Count > 0)
            errors.Add("NR и MRP не могут использовать один status_message_id; поставьте 0 для автоматического создания");

        foreach (ServerConfig server in Servers)
        {
            if (server.Id is not ("nr" or "mrp"))
                errors.Add($"server id '{server.Id}' не поддерживается slash-командами; используйте nr или mrp");
        }

        if (errors.Count != 0)
            throw new InvalidDataException("Ошибки конфигурации: " + string.Join("; ", errors) + ".");
    }

    private ServerConfig BuildLegacyServer() => new()
    {
        Id = "nr",
        DisplayName = "NR",
        ApiBaseUrl = ApiBaseUrl,
        ApiKey = ApiKey,
        StatusChannelId = StatusChannelId,
        StatusMessageId = StatusMessageId,
        AuditChannelId = AuditChannelId,
        RaRoleIds = (RaRoleIds ?? new List<ulong>()).ToList(),
        ManagementRoleIds = (ManagementRoleIds ?? new List<ulong>()).ToList(),
        CreatorKeyRoleIds = (CreatorKeyRoleIds ?? new List<ulong>()).ToList(),
        RolesPermissions = (RolesPermissions ?? new List<RolePermissionConfig>()).ToList(),
        LogChannels = LogChannels ?? new LogChannelsConfig()
    };

    internal static string NormalizeBaseUrl(string value) =>
        (value ?? string.Empty).Trim().TrimEnd('/') + "/";
}

internal sealed class ServerConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("api_base_url")]
    public string ApiBaseUrl { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = "aspect-bot";

    [JsonPropertyName("ssh_private_key_path")]
    public string SshPrivateKeyPath { get; set; } = "aspect_bridge.key";

    [JsonPropertyName("ssh_private_key")]
    public string SshPrivateKey { get; set; } = string.Empty;

    [JsonPropertyName("public_address")]
    public string PublicAddress { get; set; } = string.Empty;

    [JsonPropertyName("status_channel_id")]
    public ulong StatusChannelId { get; set; }

    [JsonPropertyName("status_message_id")]
    public ulong StatusMessageId { get; set; }

    [JsonPropertyName("audit_channel_id")]
    public ulong AuditChannelId { get; set; }

    [JsonPropertyName("required_server_role_ids")]
    public List<ulong> RequiredServerRoleIds { get; set; } = new();

    [JsonPropertyName("ra_role_ids")]
    public List<ulong> RaRoleIds { get; set; } = new();

    [JsonPropertyName("management_role_ids")]
    public List<ulong> ManagementRoleIds { get; set; } = new();

    [JsonPropertyName("creator_key_role_ids")]
    public List<ulong> CreatorKeyRoleIds { get; set; } = new();

    [JsonPropertyName("roles_permissions")]
    public List<RolePermissionConfig> RolesPermissions { get; set; } = new();

    [JsonPropertyName("log_channels")]
    public LogChannelsConfig LogChannels { get; set; } = new();

    public void NormalizeAndValidate(List<string> errors, string scope)
    {
        Id = (Id ?? string.Empty).Trim().ToLowerInvariant();
        DisplayName = (DisplayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(DisplayName))
            DisplayName = Id.ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(Id))
            errors.Add($"{scope}.id не заполнен");
        if (DisplayName.Length > 32)
            errors.Add($"{scope}.display_name длиннее 32 символов");

        ApiBaseUrl = BotConfig.NormalizeBaseUrl(ApiBaseUrl);
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{scope}.api_base_url должен быть абсолютным HTTP/HTTPS URL");
        }

        bool hasSshKey = !string.IsNullOrWhiteSpace(SshPrivateKey) ||
                         (!string.IsNullOrWhiteSpace(SshPrivateKeyPath) && (File.Exists(SshPrivateKeyPath) || File.Exists(Path.Combine(AppContext.BaseDirectory, SshPrivateKeyPath))));

        if (!hasSshKey && (string.IsNullOrWhiteSpace(ApiKey) || ApiKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase) || ApiKey.Length < 32))
        {
            errors.Add($"{scope} требует либо валидный SSH приватный ключ (ssh_private_key_path / ssh_private_key), либо api_key (минимум 32 символа)");
        }

        if (StatusChannelId == 0)
            errors.Add($"{scope}.status_channel_id не заполнен");

        PublicAddress = (PublicAddress ?? string.Empty).Trim();
        RequiredServerRoleIds = NormalizeIds(RequiredServerRoleIds);
        RaRoleIds = NormalizeIds(RaRoleIds);
        ManagementRoleIds = NormalizeIds(ManagementRoleIds);
        CreatorKeyRoleIds = NormalizeIds(CreatorKeyRoleIds);

        RolesPermissions ??= new List<RolePermissionConfig>();
        foreach (RolePermissionConfig role in RolesPermissions)
            role.Normalize();

        if (RolesPermissions.Count == 0)
        {
            if (RaRoleIds.Count > 0)
            {
                RolesPermissions.Add(new RolePermissionConfig
                {
                    Id = "ra",
                    DisplayName = "Администрация (RA)",
                    DiscordRoleIds = RaRoleIds.ToList(),
                    Inherits = new List<string>(),
                    AllowedCommands = new List<string> { "server", "linksteam", "players", "console", "ban", "kick" }
                });
            }

            if (ManagementRoleIds.Count > 0)
            {
                RolesPermissions.Add(new RolePermissionConfig
                {
                    Id = "management",
                    DisplayName = "Руководство",
                    DiscordRoleIds = ManagementRoleIds.ToList(),
                    Inherits = new List<string> { "ra" },
                    AllowedCommands = new List<string> { "listranked" }
                });
            }

            if (CreatorKeyRoleIds.Count > 0)
            {
                RolesPermissions.Add(new RolePermissionConfig
                {
                    Id = "creator",
                    DisplayName = "Ключ Создателя",
                    DiscordRoleIds = CreatorKeyRoleIds.ToList(),
                    Inherits = ManagementRoleIds.Count > 0 ? new List<string> { "management" } : new List<string> { "ra" },
                    AllowedCommands = new List<string> { "*" }
                });
            }
        }

        LogChannels ??= new LogChannelsConfig();
        LogChannels.NormalizeAndValidate(errors, scope + ".log_channels");
    }

    private static List<ulong> NormalizeIds(IEnumerable<ulong>? ids) =>
        (ids ?? Array.Empty<ulong>()).Where(id => id != 0).Distinct().ToList();
}

internal sealed class RolePermissionConfig
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("discord_role_ids")]
    public List<ulong> DiscordRoleIds { get; set; } = new();

    [JsonPropertyName("inherits")]
    public List<string> Inherits { get; set; } = new();

    [JsonPropertyName("allowed_commands")]
    public List<string> AllowedCommands { get; set; } = new();

    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim().ToLowerInvariant();
        DisplayName = (DisplayName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(DisplayName))
            DisplayName = Id;
        DiscordRoleIds = (DiscordRoleIds ?? new List<ulong>()).Where(r => r != 0).Distinct().ToList();
        Inherits = (Inherits ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        AllowedCommands = (AllowedCommands ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant().TrimStart('/'))
            .Distinct()
            .ToList();
    }
}

internal sealed class LogChannelsConfig
{
    [JsonPropertyName("punishments")]
    public List<LogChannelTarget> Punishments { get; set; } = new();

    [JsonPropertyName("rounds")]
    public List<LogChannelTarget> Rounds { get; set; } = new();

    [JsonPropertyName("server")]
    public List<LogChannelTarget> Server { get; set; } = new();

    [JsonPropertyName("commands")]
    public List<LogChannelTarget> Commands { get; set; } = new();

    [JsonPropertyName("reports")]
    public List<LogChannelTarget> Reports { get; set; } = new();

    public IReadOnlyList<LogChannelTarget> ForCategory(string category) => category.ToLowerInvariant() switch
    {
        "punishments" => Punishments,
        "rounds" => Rounds,
        "server" => Server,
        "commands" => Commands,
        "reports" => Reports,
        _ => Array.Empty<LogChannelTarget>()
    };

    public void NormalizeAndValidate(List<string> errors, string scope)
    {
        Punishments ??= new List<LogChannelTarget>();
        Rounds ??= new List<LogChannelTarget>();
        Server ??= new List<LogChannelTarget>();
        Commands ??= new List<LogChannelTarget>();
        Reports ??= new List<LogChannelTarget>();

        foreach (LogChannelTarget target in Punishments.Concat(Rounds).Concat(Server).Concat(Commands).Concat(Reports))
        {
            target.Mode = (target.Mode ?? "embed").Trim().ToLowerInvariant();
            if (target.Id != 0 && target.Mode is not ("embed" or "text"))
                errors.Add($"{scope} содержит неизвестный mode '{target.Mode}', допустимы embed или text");
        }
    }
}

internal sealed class LogChannelTarget
{
    [JsonPropertyName("id")]
    public ulong Id { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "embed";
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
}

internal sealed class AssignableGroup
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
}

internal sealed class GroupAutocompleteProvider : IAutocompleteProvider
{
    public async Task<IEnumerable<DiscordAutoCompleteChoice>> Provider(AutocompleteContext ctx)
    {
        BotRuntime runtime = BotRuntime.Current;
        var choices = new List<DiscordAutoCompleteChoice>();

        string? serverId = ctx.Options.FirstOrDefault(o => o.Name == "server")?.Value?.ToString();
        var serversToQuery = new List<ServerRuntime>();

        if (!string.IsNullOrWhiteSpace(serverId) && runtime.TryGetServer(serverId, out ServerRuntime? server) && server != null)
        {
            serversToQuery.Add(server);
        }
        else
        {
            serversToQuery.AddRange(runtime.Servers);
        }

        var allGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ServerRuntime s in serversToQuery)
        {
            try
            {
                IReadOnlyList<string> groups = await s.GetGroupsCachedAsync(CancellationToken.None).ConfigureAwait(false);
                foreach (string g in groups)
                    allGroups.Add(g);
            }
            catch
            {
            }
        }

        foreach (AssignableGroup group in runtime.Config.AssignableGroups)
        {
            if (!string.IsNullOrWhiteSpace(group.Name))
                allGroups.Add(group.Name);
        }

        string focusedValue = (ctx.FocusedOption?.Value?.ToString() ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(focusedValue) || "none".Contains(focusedValue, StringComparison.OrdinalIgnoreCase) || "снять".Contains(focusedValue, StringComparison.OrdinalIgnoreCase))
        {
            choices.Add(new DiscordAutoCompleteChoice("Снять группу (none)", "none"));
        }

        foreach (string group in allGroups.OrderBy(g => g))
        {
            if (string.IsNullOrEmpty(focusedValue) || group.Contains(focusedValue, StringComparison.OrdinalIgnoreCase))
            {
                choices.Add(new DiscordAutoCompleteChoice(group, group));
                if (choices.Count >= 25)
                    break;
            }
        }

        return choices;
    }
}

internal sealed class OnlinePlayerAutocompleteProvider : IAutocompleteProvider
{
    public async Task<IEnumerable<DiscordAutoCompleteChoice>> Provider(AutocompleteContext ctx)
    {
        BotRuntime runtime = BotRuntime.Current;
        var choices = new List<DiscordAutoCompleteChoice>();

        string? serverId = ctx.Options.FirstOrDefault(o => o.Name == "server")?.Value?.ToString();
        var serversToQuery = new List<ServerRuntime>();

        if (!string.IsNullOrWhiteSpace(serverId) && runtime.TryGetServer(serverId, out ServerRuntime? server) && server != null)
        {
            serversToQuery.Add(server);
        }
        else
        {
            serversToQuery.AddRange(runtime.Servers);
        }

        var players = new List<ApiPlayer>();
        foreach (ServerRuntime s in serversToQuery)
        {
            try
            {
                PlayersResponse resp = await s.Api.GetPlayersAsync(CancellationToken.None).ConfigureAwait(false);
                if (resp.Players != null)
                {
                    foreach (ApiPlayer p in resp.Players)
                        players.Add(p);
                }
            }
            catch
            {
            }
        }

        string focusedValue = (ctx.FocusedOption?.Value?.ToString() ?? string.Empty).Trim();

        IEnumerable<ApiPlayer> filtered = players;
        if (!string.IsNullOrWhiteSpace(focusedValue))
        {
            filtered = filtered.Where(p =>
                p.Id.ToString().Contains(focusedValue, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(p.DisplayName) && p.DisplayName.Contains(focusedValue, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.UserId) && p.UserId.Contains(focusedValue, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (ApiPlayer p in filtered.Take(25))
        {
            string rankTag = p.HasRank ? $" [{p.DisplayRank}]" : "";
            string label = DiscordPresentation.Limit($"#{p.Id}{rankTag} {p.DisplayName} — {p.Role}", 95);
            choices.Add(new DiscordAutoCompleteChoice(label, p.Id.ToString()));
        }

        if (choices.Count == 0 && players.Count == 0)
        {
            choices.Add(new DiscordAutoCompleteChoice("На сервере нет игроков онлайн", "0"));
        }

        return choices;
    }
}

internal sealed class StaffMemberAutocompleteProvider : IAutocompleteProvider
{
    public async Task<IEnumerable<DiscordAutoCompleteChoice>> Provider(AutocompleteContext ctx)
    {
        BotRuntime runtime = BotRuntime.Current;
        var choices = new List<DiscordAutoCompleteChoice>();

        string? serverId = ctx.Options.FirstOrDefault(o => o.Name == "server")?.Value?.ToString();
        var serversToQuery = new List<ServerRuntime>();

        if (!string.IsNullOrWhiteSpace(serverId) && runtime.TryGetServer(serverId, out ServerRuntime? server) && server != null)
        {
            serversToQuery.Add(server);
        }
        else
        {
            serversToQuery.AddRange(runtime.Servers);
        }

        var staffList = new List<StaffMemberDto>();
        foreach (ServerRuntime s in serversToQuery)
        {
            try
            {
                StaffListResponse resp = await s.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
                if (resp.Staff != null)
                {
                    foreach (StaffMemberDto st in resp.Staff)
                    {
                        if (!staffList.Any(x => x.Id.Equals(st.Id, StringComparison.OrdinalIgnoreCase)))
                            staffList.Add(st);
                    }
                }
            }
            catch
            {
            }
        }

        string focusedValue = (ctx.FocusedOption?.Value?.ToString() ?? string.Empty).Trim();

        IEnumerable<StaffMemberDto> filtered = staffList;
        if (!string.IsNullOrWhiteSpace(focusedValue))
        {
            filtered = filtered.Where(s =>
                s.Id.Contains(focusedValue, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(s.Nickname) && s.Nickname.Contains(focusedValue, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.Group) && s.Group.Contains(focusedValue, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.DiscordUserName) && s.DiscordUserName.Contains(focusedValue, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (StaffMemberDto s in filtered.Take(25))
        {
            string name = !string.IsNullOrWhiteSpace(s.Nickname) ? s.Nickname : (!string.IsNullOrWhiteSpace(s.DiscordUserName) ? s.DiscordUserName : s.Id);
            string label = DiscordPresentation.Limit($"[{s.Group}] {name} ({s.Id})", 95);
            choices.Add(new DiscordAutoCompleteChoice(label, s.Id));
        }

        if (choices.Count == 0 && staffList.Count == 0)
        {
            choices.Add(new DiscordAutoCompleteChoice("В реестре стаффа пока нет записей", "none"));
        }

        return choices;
    }
}
