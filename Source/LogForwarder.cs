namespace AspectDiscordBot;

internal sealed class LogForwarder
{
    private readonly DiscordClient _client;
    private readonly BotRuntime _runtime;
    private readonly ServerRuntime _server;
    private long _cursor;
    private bool _initialized;

    public LogForwarder(DiscordClient client, BotRuntime runtime, ServerRuntime server)
    {
        _client = client;
        _runtime = runtime;
        _server = server;
        _cursor = runtime.State.GetLastLogEventId(server.Config.Id);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_runtime.Config.LogPollSeconds));
        do
        {
            await PollOnceSafeAsync(cancellationToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private async Task PollOnceSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!_initialized)
            {
                await InitializeCursorAsync(cancellationToken).ConfigureAwait(false);
                _initialized = true;
            }

            LogEventBatchResponse batch = await _server.Api
                .GetLogsAsync(_cursor, _runtime.Config.LogBatchSize, cancellationToken)
                .ConfigureAwait(false);
            if (batch.HasGap)
                await SendGapWarningAsync(batch, cancellationToken).ConfigureAwait(false);

            List<BridgeLogEvent> ordered = batch.Events.OrderBy(item => item.Id).ToList();
            if (ordered.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ForwardBatchAsync(ordered).ConfigureAwait(false);
                _cursor = ordered[ordered.Count - 1].Id;
                _runtime.State.SetLastLogEventId(_server.Config.Id, _cursor);
            }

            if (ordered.Count == 0 && batch.NextAfterId > _cursor)
            {
                _cursor = batch.NextAfterId;
                _runtime.State.SetLastLogEventId(_server.Config.Id, _cursor);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTimeOffset.Now:O}] [{_server.Config.DisplayName}] Пересылка Discord-логов: {ex.Message}");
        }
    }

    private async Task InitializeCursorAsync(CancellationToken cancellationToken)
    {
        if (_cursor != 0 || _runtime.Config.SendBufferedLogsOnStart)
            return;

        LogEventBatchResponse tail = await _server.Api.GetLogsAsync(long.MaxValue, 1, cancellationToken)
            .ConfigureAwait(false);
        _cursor = tail.LatestId;
        if (_cursor > 0)
            _runtime.State.SetLastLogEventId(_server.Config.Id, _cursor);
    }

    private async Task ForwardAsync(BridgeLogEvent entry)
    {
        IReadOnlyList<LogChannelTarget> targets = _server.Config.LogChannels.ForCategory(entry.Category);
        foreach (LogChannelTarget target in targets.Where(item => item.Id != 0))
        {
            DiscordChannel channel = await _client.GetChannelAsync(target.Id).ConfigureAwait(false);
            DiscordMessageBuilder message = target.Mode == "text"
                ? new DiscordMessageBuilder().WithContent(BuildText(entry, _server.Config))
                : new DiscordMessageBuilder().AddEmbed(BuildEmbed(entry, _server.Config));
            await channel.SendMessageAsync(message).ConfigureAwait(false);
        }
    }

    private async Task ForwardBatchAsync(IReadOnlyList<BridgeLogEvent> events)
    {
        // Group events by (channelId, mode) to send multiple embeds per message
        // and reduce the number of Discord API calls.
        var groups = new Dictionary<(ulong channelId, string mode), List<BridgeLogEvent>>();
        foreach (BridgeLogEvent entry in events)
        {
            IReadOnlyList<LogChannelTarget> targets = _server.Config.LogChannels.ForCategory(entry.Category);
            foreach (LogChannelTarget target in targets.Where(item => item.Id != 0))
            {
                var key = (target.Id, target.Mode ?? "embed");
                if (!groups.TryGetValue(key, out List<BridgeLogEvent>? list))
                {
                    list = new List<BridgeLogEvent>();
                    groups[key] = list;
                }
                list.Add(entry);
            }
        }

        var channelCache = new Dictionary<ulong, DiscordChannel>();
        foreach (((ulong channelId, string mode), List<BridgeLogEvent> entries) in groups)
        {
            if (!channelCache.TryGetValue(channelId, out DiscordChannel? channel))
            {
                channel = await _client.GetChannelAsync(channelId).ConfigureAwait(false);
                channelCache[channelId] = channel;
            }

            if (mode == "text")
                await SendTextBatchAsync(channel, entries).ConfigureAwait(false);
            else
                await SendEmbedBatchAsync(channel, entries).ConfigureAwait(false);
        }
    }

    private async Task SendEmbedBatchAsync(DiscordChannel channel, List<BridgeLogEvent> entries)
    {
        // Discord allows up to 10 embeds per message.
        const int maxEmbeds = 10;
        for (int i = 0; i < entries.Count; i += maxEmbeds)
        {
            var message = new DiscordMessageBuilder();
            foreach (BridgeLogEvent entry in entries.Skip(i).Take(maxEmbeds))
                message.AddEmbed(BuildEmbed(entry, _server.Config));
            await channel.SendMessageAsync(message).ConfigureAwait(false);
        }
    }

    private async Task SendTextBatchAsync(DiscordChannel channel, List<BridgeLogEvent> entries)
    {
        var builder = new StringBuilder();
        foreach (BridgeLogEvent entry in entries)
        {
            string text = BuildText(entry, _server.Config);
            if (builder.Length > 0 && builder.Length + 1 + text.Length > 1950)
            {
                await channel.SendMessageAsync(
                    new DiscordMessageBuilder().WithContent(builder.ToString())).ConfigureAwait(false);
                builder.Clear();
            }
            if (builder.Length > 0)
                builder.AppendLine();
            builder.Append(text);
        }

        if (builder.Length > 0)
        {
            await channel.SendMessageAsync(
                new DiscordMessageBuilder().WithContent(builder.ToString())).ConfigureAwait(false);
        }
    }

    private async Task SendGapWarningAsync(LogEventBatchResponse batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var warning = new BridgeLogEvent
        {
            Category = "server",
            EventType = "log_buffer_gap",
            Title = "Часть логов потеряна",
            Description = "Бот был отключён дольше, чем события хранились в буфере EXILED-плагина.",
            Severity = "warning",
            Utc = DateTimeOffset.UtcNow.ToString("O"),
            Fields = new List<BridgeLogField>
            {
                new() { Name = "Последний ID бота", Value = _cursor.ToString(CultureInfo.InvariantCulture) },
                new() { Name = "Первый доступный ID", Value = batch.OldestId.ToString(CultureInfo.InvariantCulture) }
            }
        };
        await ForwardAsync(warning).ConfigureAwait(false);
    }

    private static DiscordEmbed BuildEmbed(BridgeLogEvent entry, ServerConfig server)
    {
        var builder = new DiscordEmbedBuilder()
            .WithTitle(DiscordPresentation.Limit($"{server.DisplayName} | {entry.Title}", 256))
            .WithDescription(DiscordPresentation.Limit(entry.Description, 4096))
            .WithColor(ColorFor(entry))
            .WithFooter($"{server.DisplayName} | {CategoryName(entry.Category)} | {entry.EventType}");

        foreach (BridgeLogField field in (entry.Fields ?? new List<BridgeLogField>()).Take(25))
        {
            string name = string.IsNullOrWhiteSpace(field.Name) ? "Детали" : DiscordPresentation.Limit(field.Name, 256);
            string value = string.IsNullOrWhiteSpace(field.Value) ? "-" : DiscordPresentation.Limit(field.Value, 1024);
            builder.AddField(name, value);
        }

        if (DateTimeOffset.TryParse(entry.Utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp))
            builder.WithTimestamp(timestamp);
        else
            builder.WithTimestamp(DateTimeOffset.UtcNow);
        return builder.Build();
    }

    private static string BuildText(BridgeLogEvent entry, ServerConfig server)
    {
        var builder = new StringBuilder();
        builder.Append("**[").Append(SanitizeMentions(server.DisplayName)).Append("] ")
            .Append(SanitizeMentions(entry.Title)).AppendLine("**");
        if (!string.IsNullOrWhiteSpace(entry.Description))
            builder.AppendLine(SanitizeMentions(entry.Description));
        foreach (BridgeLogField field in entry.Fields ?? new List<BridgeLogField>())
            builder.Append("**").Append(SanitizeMentions(field.Name)).Append(":** ")
                .AppendLine(SanitizeMentions(field.Value));
        builder.Append('-').Append(' ').Append(server.DisplayName).Append(" | ")
            .Append(CategoryName(entry.Category)).Append(" | ").Append(entry.EventType);
        return DiscordPresentation.Limit(builder.ToString(), 1950);
    }

    private static string SanitizeMentions(string value) => (value ?? string.Empty).Replace("@", "@\u200B");

    private static DiscordColor ColorFor(BridgeLogEvent entry)
    {
        if (entry.Severity.Equals("danger", StringComparison.OrdinalIgnoreCase) ||
            entry.Severity.Equals("error", StringComparison.OrdinalIgnoreCase))
            return new DiscordColor(231, 76, 60);
        if (entry.Severity.Equals("warning", StringComparison.OrdinalIgnoreCase))
            return new DiscordColor(241, 196, 15);
        return entry.Category.ToLowerInvariant() switch
        {
            "punishments" => new DiscordColor(192, 57, 43),
            "rounds" => new DiscordColor(52, 152, 219),
            "commands" => new DiscordColor(155, 89, 182),
            "reports" => new DiscordColor(230, 126, 34),
            _ => new DiscordColor(149, 165, 166)
        };
    }

    private static string CategoryName(string category) => category.ToLowerInvariant() switch
    {
        "punishments" => "Наказания",
        "rounds" => "Раунд",
        "server" => "Сервер",
        "commands" => "Команды",
        "reports" => "Репорты",
        _ => category
    };
}
