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
    public bool Success { get; set; }

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
}

internal sealed class PlayersResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("online")]
    public int Online { get; set; }

    [JsonPropertyName("maximum")]
    public int Maximum { get; set; }

    [JsonPropertyName("players")]
    public List<ApiPlayer> Players { get; set; } = new();
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

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }
}

internal sealed class CommandRequest
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("access_level")]
    public string AccessLevel { get; set; } = string.Empty;

    [JsonPropertyName("discord_user_id")]
    public string DiscordUserId { get; set; } = string.Empty;

    [JsonPropertyName("discord_user_name")]
    public string DiscordUserName { get; set; } = string.Empty;
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
