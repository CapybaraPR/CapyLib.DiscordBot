namespace AspectDiscordBot;

internal sealed class StatusUpdater
{
    private readonly DiscordClient _client;
    private readonly BotRuntime _runtime;
    private readonly Dictionary<string, ulong> _messageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CachedStatus> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _cacheSync = new();

    public StatusUpdater(DiscordClient client, BotRuntime runtime)
    {
        _client = client;
        _runtime = runtime;
        foreach (ServerRuntime server in runtime.Servers)
        {
            ulong stored = runtime.State.GetStatusMessageId(server.Config.Id);
            _messageIds[server.Config.Id] = stored != 0 ? stored : server.Config.StatusMessageId;
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RefreshAllAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(
                RefreshLoopAsync(cancellationToken),
                PresenceLoopAsync(cancellationToken))
            .ConfigureAwait(false);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_runtime.Config.StatusRefreshSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            await RefreshAllAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        foreach (ServerRuntime server in _runtime.Servers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await RefreshServerAsync(server, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshServerAsync(ServerRuntime server, CancellationToken cancellationToken)
    {
        try
        {
            ServerStatus status = await server.Api.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            SetCache(server.Config.Id, status, null);
            await UpsertMessageAsync(server, DiscordPresentation.BuildStatus(server.Config, status))
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Обновление статуса: {ex.Message}");
            SetCache(server.Config.Id, null, ex.Message);
            try
            {
                await UpsertMessageAsync(server, DiscordPresentation.BuildOffline(server.Config, ex.Message))
                    .ConfigureAwait(false);
            }
            catch (Exception updateException)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Обновление offline-статуса: {updateException.Message}");
            }
        }
    }

    private async Task PresenceLoopAsync(CancellationToken cancellationToken)
    {
        int index = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            ServerRuntime server = _runtime.Servers[index % _runtime.Servers.Count];
            CachedStatus cached = GetCache(server.Config.Id);
            string address = !string.IsNullOrWhiteSpace(cached.Status?.Address)
                ? cached.Status.Address
                : server.Config.PublicAddress;
            string presence = cached.Error == null && cached.Status != null
                ? $"{server.Config.DisplayName} | {cached.Status.GetOnline()}/{cached.Status.GetMaximum()} | {address}"
                : $"{server.Config.DisplayName} | офлайн | {address}";

            try
            {
                await _client.UpdateStatusAsync(
                        new DiscordActivity(DiscordPresentation.Limit(presence.TrimEnd(' ', '|'), 128), ActivityType.Watching),
                        UserStatus.Online)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Обновление presence: {ex.Message}");
            }

            index++;
            await Task.Delay(
                    TimeSpan.FromSeconds(_runtime.Config.PresenceRotateSeconds),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task UpsertMessageAsync(ServerRuntime server, DiscordEmbed embed)
    {
        DiscordChannel channel = await _client.GetChannelAsync(server.Config.StatusChannelId).ConfigureAwait(false);
        ulong messageId = _messageIds[server.Config.Id];
        if (messageId != 0)
        {
            try
            {
                DiscordMessage existing = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
                await existing.ModifyAsync(new DiscordMessageBuilder().AddEmbed(embed)).ConfigureAwait(false);
                return;
            }
            catch (NotFoundException)
            {
                _messageIds[server.Config.Id] = 0;
            }
        }

        DiscordMessage created = await channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed))
            .ConfigureAwait(false);
        _messageIds[server.Config.Id] = created.Id;
        _runtime.State.SetStatusMessageId(server.Config.Id, created.Id);
        Console.WriteLine(
            $"[{server.Config.DisplayName}] Создано статус-сообщение Discord: {created.Id}");
    }

    private void SetCache(string serverId, ServerStatus? status, string? error)
    {
        lock (_cacheSync)
        {
            if (!_cache.TryGetValue(serverId, out CachedStatus? cached))
            {
                cached = new CachedStatus();
                _cache[serverId] = cached;
            }
            if (status != null)
                cached.Status = status;
            cached.Error = error;
        }
    }

    private CachedStatus GetCache(string serverId)
    {
        lock (_cacheSync)
        {
            if (!_cache.TryGetValue(serverId, out CachedStatus? cached))
                return new CachedStatus { Error = "Статус ещё не получен." };
            return new CachedStatus { Status = cached.Status, Error = cached.Error };
        }
    }

    private sealed class CachedStatus
    {
        public ServerStatus? Status { get; set; }
        public string? Error { get; set; }
    }
}
