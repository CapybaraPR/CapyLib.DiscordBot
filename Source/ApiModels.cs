namespace AspectDiscordBot;

internal sealed class HealthResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

internal sealed class ServerStatus
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("online")]
    public int Online { get; set; }

    [JsonPropertyName("maximum")]
    public int Maximum { get; set; }

    [JsonPropertyName("address")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = string.Empty;

    [JsonPropertyName("round_state")]
    public string RoundState { get; set; } = string.Empty;

    [JsonPropertyName("round_time")]
    public string RoundTime { get; set; } = string.Empty;

    [JsonPropertyName("tps")]
    public double Tps { get; set; }

    [JsonPropertyName("utc")]
    public string Utc { get; set; } = string.Empty;

    [JsonPropertyName("server")]
    public NestedServerStatus? Server { get; set; }

    public int GetOnline() => Online > 0 ? Online : (Server?.PlayersCount ?? Online);
    public int GetMaximum() => Maximum > 0 ? Maximum : (Server?.MaxPlayers ?? (Maximum > 0 ? Maximum : 25));
}

internal sealed class NestedServerStatus
{
    [JsonPropertyName("players_count")]
    public int PlayersCount { get; set; }

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("server_name")]
    public string? ServerName { get; set; }

    [JsonPropertyName("public_address")]
    public string? PublicAddress { get; set; }

    [JsonPropertyName("tps")]
    public double Tps { get; set; }
}

internal sealed class PlayersResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("online")]
    public int Online { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("maximum")]
    public int Maximum { get; set; }

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("players")]
    public List<ApiPlayer> Players { get; set; } = new();

    public int GetOnline() => Online > 0 ? Online : (Count > 0 ? Count : Players.Count);
    public int GetMaximum() => Maximum > 0 ? Maximum : (MaxPlayers > 0 ? MaxPlayers : 20);
}

internal sealed class GroupsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("groups")]
    public List<string> Groups { get; set; } = new();
}

internal sealed class ApiPlayer
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }

    [JsonPropertyName("rank")]
    public string? Rank { get; set; }

    [JsonPropertyName("rank_color")]
    public string? RankColor { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Name) ? Name : (!string.IsNullOrWhiteSpace(Nickname) ? Nickname : "Unknown");
    public string DisplayRank => !string.IsNullOrWhiteSpace(Rank) ? Rank : (!string.IsNullOrWhiteSpace(Group) ? Group : string.Empty);
    public bool HasRank => !string.IsNullOrWhiteSpace(DisplayRank) && !DisplayRank.Equals("none", StringComparison.OrdinalIgnoreCase);
}

internal sealed class CommandRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("access")]
    public string Access { get; set; } = string.Empty;

    [JsonPropertyName("actor_id")]
    public ulong ActorId { get; set; }

    [JsonPropertyName("actor_name")]
    public string ActorName { get; set; } = string.Empty;
}

internal sealed class CommandResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("output")]
    public string Output { get; set; } = string.Empty;

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("executed_as")]
    public string ExecutedAs { get; set; } = string.Empty;
}

internal sealed class ErrorResponse
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = string.Empty;
}

internal sealed class LogEventBatchResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("events")]
    public List<BridgeLogEvent> Events { get; set; } = new();

    [JsonPropertyName("next_after_id")]
    public long NextAfterId { get; set; }

    [JsonPropertyName("latest_id")]
    public long LatestId { get; set; }

    [JsonPropertyName("oldest_id")]
    public long OldestId { get; set; }

    [JsonPropertyName("has_gap")]
    public bool HasGap { get; set; }
}

internal sealed class BridgeLogEvent
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "info";

    [JsonPropertyName("utc")]
    public string Utc { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public List<BridgeLogField> Fields { get; set; } = new();
}

internal sealed class BridgeLogField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("inline")]
    public bool Inline { get; set; } = true;
}

internal sealed class LinkCodeRequest
{
    [JsonPropertyName("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    [JsonPropertyName("discord_user_name")]
    public string DiscordUserName { get; set; } = string.Empty;

    [JsonPropertyName("discord_role_ids")]
    public List<ulong> DiscordRoleIds { get; set; } = new();
}

internal sealed class LinkCodeResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("expires_utc")]
    public string ExpiresUtc { get; set; } = string.Empty;

    [JsonPropertyName("expires_in_seconds")]
    public int ExpiresInSeconds { get; set; }
}

internal sealed class LinkedAccountsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("accounts")]
    public List<LinkedDiscordAccount> Accounts { get; set; } = new();
}

internal sealed class LinkedDiscordAccount
{
    [JsonPropertyName("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    [JsonPropertyName("discord_user_name")]
    public string DiscordUserName { get; set; } = string.Empty;

    [JsonPropertyName("linked_utc")]
    public string LinkedUtc { get; set; } = string.Empty;
}

internal sealed class LinkRoleSyncRequest
{
    [JsonPropertyName("members")]
    public List<DiscordMemberRoleSnapshot> Members { get; set; } = new();
}

internal sealed class DiscordMemberRoleSnapshot
{
    [JsonPropertyName("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    [JsonPropertyName("discord_user_name")]
    public string DiscordUserName { get; set; } = string.Empty;

    [JsonPropertyName("discord_role_ids")]
    public List<ulong> DiscordRoleIds { get; set; } = new();
}

internal sealed class LinkRoleSyncResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    [JsonPropertyName("received")]
    public int Received { get; set; }
}

internal sealed class StaffMemberDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = string.Empty;

    [JsonPropertyName("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    [JsonPropertyName("discord_user_name")]
    public string DiscordUserName { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("server_scope")]
    public string ServerScope { get; set; } = "all";

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("assigned_by_discord_id")]
    public ulong AssignedByDiscordId { get; set; }

    [JsonPropertyName("assigned_by_discord_name")]
    public string AssignedByDiscordName { get; set; } = string.Empty;

    [JsonPropertyName("assigned_at_utc")]
    public DateTime AssignedAtUtc { get; set; }

    [JsonPropertyName("total_playtime_seconds")]
    public long TotalPlaytimeSeconds { get; set; }

    [JsonPropertyName("weekly_playtime_seconds")]
    public long WeeklyPlaytimeSeconds { get; set; }

    [JsonPropertyName("duty_playtime_seconds")]
    public long DutyPlaytimeSeconds { get; set; }

    [JsonPropertyName("bans_count")]
    public int BansCount { get; set; }

    [JsonPropertyName("mutes_count")]
    public int MutesCount { get; set; }

    [JsonPropertyName("kicks_count")]
    public int KicksCount { get; set; }

    [JsonPropertyName("first_seen_utc")]
    public DateTime FirstSeenUtc { get; set; }

    [JsonPropertyName("last_seen_utc")]
    public DateTime LastSeenUtc { get; set; }

    [JsonPropertyName("history")]
    public List<StaffHistoryEntryDto> History { get; set; } = new();
}

internal sealed class StaffHistoryEntryDto
{
    [JsonPropertyName("timestamp_utc")]
    public DateTime TimestampUtc { get; set; }

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("old_group")]
    public string OldGroup { get; set; } = string.Empty;

    [JsonPropertyName("new_group")]
    public string NewGroup { get; set; } = string.Empty;

    [JsonPropertyName("actor_discord_id")]
    public ulong ActorDiscordId { get; set; }

    [JsonPropertyName("actor_discord_name")]
    public string ActorDiscordName { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class StaffListResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("staff")]
    public List<StaffMemberDto> Staff { get; set; } = new();
}

internal sealed class StaffMemberResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("member")]
    public StaffMemberDto? Member { get; set; }
}

internal sealed class StaffAddRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("discord_user_id")]
    public ulong DiscordUserId { get; set; }

    [JsonPropertyName("discord_user_name")]
    public string DiscordUserName { get; set; } = string.Empty;

    [JsonPropertyName("group")]
    public string Group { get; set; } = string.Empty;

    [JsonPropertyName("server_scope")]
    public string ServerScope { get; set; } = "all";

    [JsonPropertyName("actor_discord_id")]
    public ulong ActorDiscordId { get; set; }

    [JsonPropertyName("actor_discord_name")]
    public string ActorDiscordName { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

internal sealed class StaffRemoveRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("actor_discord_id")]
    public ulong ActorDiscordId { get; set; }

    [JsonPropertyName("actor_discord_name")]
    public string ActorDiscordName { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}
