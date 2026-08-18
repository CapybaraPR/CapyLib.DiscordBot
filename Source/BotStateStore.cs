namespace AspectDiscordBot;

internal sealed class BotStateStore
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly BotState _state;

    public BotStateStore(string path, BotConfig config)
    {
        _path = path;
        _state = Load();
        NormalizeAndMigrate(config);
    }

    public ulong GetStatusMessageId(string serverId)
    {
        lock (_sync)
            return GetOrCreateServerLocked(serverId).StatusMessageId;
    }

    public long GetLastLogEventId(string serverId)
    {
        lock (_sync)
            return GetOrCreateServerLocked(serverId).LastLogEventId;
    }

    public void SetStatusMessageId(string serverId, ulong id)
    {
        lock (_sync)
        {
            GetOrCreateServerLocked(serverId).StatusMessageId = id;
            SaveLocked();
        }
    }

    public void SetLastLogEventId(string serverId, long id)
    {
        lock (_sync)
        {
            ServerBotState server = GetOrCreateServerLocked(serverId);
            if (id <= server.LastLogEventId)
                return;
            server.LastLogEventId = id;
            SaveLocked();
        }
    }

    private BotState Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new BotState();
            return JsonSerializer.Deserialize<BotState>(File.ReadAllText(_path), JsonDefaults.Options) ?? new BotState();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Не удалось прочитать {_path}: {ex.Message}");
            return new BotState();
        }
    }

    private void NormalizeAndMigrate(BotConfig config)
    {
        lock (_sync)
        {
            _state.Servers = new Dictionary<string, ServerBotState>(
                _state.Servers ?? new Dictionary<string, ServerBotState>(),
                StringComparer.OrdinalIgnoreCase);

            bool changed = false;
            if ((_state.LegacyStatusMessageId != 0 || _state.LegacyLastLogEventId != 0) && config.Servers.Count > 0)
            {
                ServerBotState first = GetOrCreateServerLocked(config.Servers[0].Id);
                if (first.StatusMessageId == 0)
                    first.StatusMessageId = _state.LegacyStatusMessageId;
                if (first.LastLogEventId == 0)
                    first.LastLogEventId = _state.LegacyLastLogEventId;
                _state.LegacyStatusMessageId = 0;
                _state.LegacyLastLogEventId = 0;
                changed = true;
            }

            if (changed)
                SaveLocked();
        }
    }

    private ServerBotState GetOrCreateServerLocked(string serverId)
    {
        string normalized = (serverId ?? string.Empty).Trim().ToLowerInvariant();
        if (!_state.Servers.TryGetValue(normalized, out ServerBotState? server))
        {
            server = new ServerBotState();
            _state.Servers[normalized] = server;
        }
        return server;
    }

    private void SaveLocked()
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temporary = _path + ".tmp";
        File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(_state, JsonDefaults.Options),
            new UTF8Encoding(false));
        File.Move(temporary, _path, true);
    }

    private sealed class BotState
    {
        [JsonPropertyName("servers")]
        public Dictionary<string, ServerBotState> Servers { get; set; } = new();

        [JsonPropertyName("status_message_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public ulong LegacyStatusMessageId { get; set; }

        [JsonPropertyName("last_log_event_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long LegacyLastLogEventId { get; set; }
    }

    private sealed class ServerBotState
    {
        [JsonPropertyName("status_message_id")]
        public ulong StatusMessageId { get; set; }

        [JsonPropertyName("last_log_event_id")]
        public long LastLogEventId { get; set; }
    }
}
