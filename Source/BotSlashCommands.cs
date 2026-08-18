namespace AspectDiscordBot;

internal sealed class BotSlashCommands : ApplicationCommandModule
{
    [SlashCommand("server", "Показать состояние выбранного SCP:SL сервера.")]
    public async Task ServerAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        try
        {
            ServerStatus status = await server.Api.GetStatusAsync(CancellationToken.None).ConfigureAwait(false);
            await context.EditResponseAsync(
                    new DiscordWebhookBuilder().AddEmbed(DiscordPresentation.BuildStatus(server.Config, status)))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RespondErrorAsync(context, server.Config, ex).ConfigureAwait(false);
        }
    }

    [SlashCommand("players", "Показать список игроков на сервере.")]
    public async Task PlayersAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        if (!AccessResolver.CanExecute(context.Member, server.Config, "players"))
        {
            await DenyAsync(context, server.Config).ConfigureAwait(false);
            return;
        }

        try
        {
            PlayersResponse response = await server.Api.GetPlayersAsync(CancellationToken.None).ConfigureAwait(false);
            int online = response.GetOnline();
            int maximum = response.GetMaximum();
            string players = BuildPlayerList(response, runtime.Config.ShowPlayerNames);
            DiscordEmbed embed = new DiscordEmbedBuilder()
                .WithTitle($"{server.Config.DisplayName} | игроки: {online}/{maximum}")
                .WithDescription(players)
                .WithColor(new DiscordColor(52, 152, 219))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RespondErrorAsync(context, server.Config, ex).ConfigureAwait(false);
        }
    }

    [SlashCommand("listranked", "Показать список игроков с рангами на сервере (Руководство).")]
    public async Task ListRankedAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        if (!AccessResolver.CanExecute(context.Member, server.Config, "listranked"))
        {
            await DenyAsync(context, server.Config).ConfigureAwait(false);
            return;
        }

        try
        {
            PlayersResponse response = await server.Api.GetPlayersAsync(CancellationToken.None).ConfigureAwait(false);
            List<ApiPlayer> ranked = response.Players.Where(p => p.HasRank).ToList();
            int online = response.GetOnline();
            int maximum = response.GetMaximum();

            string desc;
            if (ranked.Count == 0)
            {
                desc = "На сервере нет игроков с рангами.";
            }
            else
            {
                var sb = new StringBuilder();
                foreach (ApiPlayer p in ranked)
                {
                    string name = runtime.Config.ShowPlayerNames ? DiscordPresentation.Limit(p.DisplayName, 80) : "имя скрыто";
                    string line = $"`#{p.Id}` **[{p.DisplayRank}]** {name} — `{p.Role}`\n";
                    if (sb.Length + line.Length > 3800)
                    {
                        sb.Append($"\n...ещё {ranked.Count - sb.Length} игрок(ов).");
                        break;
                    }
                    sb.Append(line);
                }
                desc = sb.ToString();
            }

            DiscordEmbed embed = new DiscordEmbedBuilder()
                .WithTitle($"{server.Config.DisplayName} | Игроки с рангами: {ranked.Count}")
                .WithDescription(desc)
                .WithColor(new DiscordColor(241, 196, 15))
                .AddField("Сервер", $"{server.Config.DisplayName} ({online}/{maximum})", true)
                .WithFooter($"Доступ: {AccessResolver.GetHighestRoleDisplayName(context.Member, server.Config)}")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RespondErrorAsync(context, server.Config, ex).ConfigureAwait(false);
        }
    }

    [SlashCommand("console", "Выполнить команду на сервере.")]
    public async Task ConsoleAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("text", "Команда без начального слеша.")] string command)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        BotAccessLevel access = AccessResolver.GetAccess(context.Member, server.Config);
        if (!AccessResolver.CanExecute(context.Member, server.Config, "console"))
        {
            await DenyAsync(context, server.Config).ConfigureAwait(false);
            await SendAuditAsync(context, server.Config, command, access, false, "Нет требуемого набора Discord-ролей.")
                .ConfigureAwait(false);
            return;
        }

        await ExecuteServerCommandAsync(context, server, command, access, "Консольная команда").ConfigureAwait(false);
    }

    [SlashCommand("ban", "Забанить игрока.")]
    public async Task BanAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "PlayerId, SteamID64 или ник игрока.")] string player,
        [Option("duration", "Длительность бана (например: 1d, 30m, 2w, 0 = навсегда).")] string duration,
        [Option("reason", "Причина бана.")] string reason)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        BotAccessLevel access = AccessResolver.GetAccess(context.Member, server.Config);
        if (!AccessResolver.CanExecute(context.Member, server.Config, "ban"))
        {
            await DenyAsync(context, server.Config).ConfigureAwait(false);
            await SendAuditAsync(context, server.Config, $"ban {player} {duration} {reason}", access, false, "Нет требуемого набора Discord-ролей.")
                .ConfigureAwait(false);
            return;
        }

        string fullCommand = $"ban {player} {duration} {reason}";
        await ExecuteServerCommandAsync(context, server, fullCommand, access, "Блокировка игрока").ConfigureAwait(false);
    }

    [SlashCommand("kick", "Кикнуть игрока.")]
    public async Task KickAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "PlayerId, SteamID64 или ник игрока.")] string player,
        [Option("reason", "Причина кика.")] string reason)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        BotAccessLevel access = AccessResolver.GetAccess(context.Member, server.Config);
        if (!AccessResolver.CanExecute(context.Member, server.Config, "kick"))
        {
            await DenyAsync(context, server.Config).ConfigureAwait(false);
            await SendAuditAsync(context, server.Config, $"kick {player} {reason}", access, false, "Нет требуемого набора Discord-ролей.")
                .ConfigureAwait(false);
            return;
        }

        string fullCommand = $"kick {player} {reason}";
        await ExecuteServerCommandAsync(context, server, fullCommand, access, "Кик игрока").ConfigureAwait(false);
    }

    [SlashCommand("setgroup", "Установить или снять группу игрока через pm setgroup.")]
    public async Task SetGroupAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "PlayerId, SteamID64 или UserID игрока.")] string player,
        [Option("group", "Начните вводить для поиска группы или выберите из списка.")]
        [Autocomplete(typeof(GroupAutocompleteProvider))] string group)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, runtime.Config.EphemeralCommandResponses).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;

        BotAccessLevel access = AccessResolver.GetAccess(context.Member, server.Config);
        if (!AccessResolver.CanExecute(context.Member, server.Config, "setgroup"))
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                $"Недостаточно прав для {server.Config.DisplayName}. Выдача групп не разрешена вашей роли."))
                .ConfigureAwait(false);
            await SendAuditAsync(context, server.Config, $"pm setgroup {player} {group}", access, false, "Недостаточно прав для команды setgroup.")
                .ConfigureAwait(false);
            return;
        }

        string targetGroup = string.IsNullOrWhiteSpace(group) || group.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "none"
            : group;

        string fullCommand = $"pm setgroup {player} {targetGroup}";
        string actionTitle = targetGroup == "none" ? "Снятие группы игрока" : $"Установка группы ({targetGroup})";
        await ExecuteServerCommandAsync(context, server, fullCommand, access, actionTitle).ConfigureAwait(false);
    }

    [SlashCommand("linksteam", "Привязать Steam к Discord.")]
    public async Task LinkSteamAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        await DeferAsync(context, true).ConfigureAwait(false);
        ServerRuntime? server = await ResolveServerAsync(context, serverId).ConfigureAwait(false);
        if (server == null)
            return;
        if (context.Member == null)
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                "Команда доступна только внутри настроенного Discord-сервера.")).ConfigureAwait(false);
            return;
        }

        try
        {
            LinkCodeResponse response = await server.Api.CreateLinkCodeAsync(
                    new LinkCodeRequest
                    {
                        DiscordUserId = context.User.Id,
                        DiscordUserName = context.User.Username,
                        DiscordRoleIds = context.Member.Roles.Select(role => role.Id).Distinct().ToList()
                    },
                    CancellationToken.None)
                .ConfigureAwait(false);

            DiscordEmbed embed = BuildLinkCodeEmbed(server.Config, response);
            try
            {
                DiscordDmChannel dm = await context.Member.CreateDmChannelAsync().ConfigureAwait(false);
                await dm.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed)).ConfigureAwait(false);
                int minutes = Math.Max(1, (int)Math.Ceiling(response.ExpiresInSeconds / 60d));
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
                    $"Код привязки к {server.Config.DisplayName} отправлен в ЛС. Он действует {minutes} минут."))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Не удалось отправить код в ЛС {context.User.Id}: {ex.Message}");
                await context.EditResponseAsync(
                        new DiscordWebhookBuilder()
                            .WithContent("Личные сообщения закрыты, поэтому код показан здесь. Этот ответ виден только вам.")
                            .AddEmbed(embed))
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            await RespondErrorAsync(context, server.Config, ex).ConfigureAwait(false);
        }
    }

    [SlashCommand("discord_help", "Показать команды бота и доступ к NR/MRP.")]
    public async Task HelpAsync(InteractionContext context)
    {
        BotRuntime runtime = BotRuntime.Current;
        string access = string.Join(
            "\n",
            runtime.Servers.Select(server =>
                $"**{server.Config.DisplayName}:** {AccessResolver.GetHighestRoleDisplayName(context.Member, server.Config)}"));

        var embed = new DiscordEmbedBuilder()
            .WithTitle("CapyLib Discord Bot — Доступные команды")
            .WithDescription(access)
            .AddField("/server server:<NR/MRP>", "Состояние, онлайн и адрес выбранного сервера.")
            .AddField("/linksteam server:<NR/MRP>", "Получить код привязки для выбранного сервера.");

        bool canPlayers = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "players"));
        if (canPlayers)
            embed.AddField("/players server:<NR/MRP>", "Список игроков онлайн на выбранном сервере.");

        bool canListRanked = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "listranked"));
        if (canListRanked)
            embed.AddField("/listranked server:<NR/MRP>", "Список игроков с рангами (Руководство).");

        bool canConsole = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "console"));
        if (canConsole)
            embed.AddField("/console server:<NR/MRP> text:<команда>", "Выполнить произвольную команду на сервере.");

        bool canBan = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "ban"));
        if (canBan)
            embed.AddField("/ban server:<NR/MRP> player:<ID> duration:<время> reason:<причина>", "Забанить игрока на сервере.");

        bool canKick = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "kick"));
        if (canKick)
            embed.AddField("/kick server:<NR/MRP> player:<ID> reason:<причина>", "Кикнуть игрока с сервера.");

        bool canSetGroup = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "setgroup"));
        if (canSetGroup)
            embed.AddField("/setgroup server:<NR/MRP> player:<ID> group:<выбор>", "Установить или снять группу игрока.");

        embed.WithColor(new DiscordColor(149, 165, 166));

        await context.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder()
                .AddEmbed(embed.Build())
                .AsEphemeral(runtime.Config.EphemeralCommandResponses)).ConfigureAwait(false);
    }

    private static async Task ExecuteServerCommandAsync(
        InteractionContext context,
        ServerRuntime server,
        string command,
        BotAccessLevel access,
        string actionTitle)
    {
        try
        {
            var request = new CommandRequest
            {
                Command = command,
                AccessLevel = access.ApiName(),
                DiscordUserId = context.User.Id.ToString(),
                DiscordUserName = context.User.Username
            };
            CommandResponse response = await server.Api.ExecuteCommandAsync(request, CancellationToken.None)
                .ConfigureAwait(false);
            string output = string.IsNullOrWhiteSpace(response.Output)
                ? "Команда обработана без текстового ответа."
                : response.Output;
            DiscordEmbed embed = new DiscordEmbedBuilder()
                .WithTitle(response.Success ? $"{actionTitle}: успешно" : $"{actionTitle}: ошибка")
                .WithDescription(DiscordPresentation.CodeBlock(output))
                .AddField("Сервер", server.Config.DisplayName, true)
                .AddField("Уровень", access.DisplayName(), true)
                .AddField("Команда", DiscordPresentation.Limit(response.Command, 1024))
                .WithColor(response.Success
                    ? new DiscordColor(46, 204, 113)
                    : new DiscordColor(230, 126, 34))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
            await SendAuditAsync(context, server.Config, response.Command, access, response.Success, output)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RespondErrorAsync(context, server.Config, ex).ConfigureAwait(false);
            await SendAuditAsync(context, server.Config, command, access, false, FriendlyError(ex))
                .ConfigureAwait(false);
        }
    }

    private static DiscordEmbed BuildLinkCodeEmbed(ServerConfig server, LinkCodeResponse response)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling(response.ExpiresInSeconds / 60d));
        return new DiscordEmbedBuilder()
            .WithTitle($"Привязка Discord к {server.DisplayName}")
            .WithDescription(
                $"**Код привязки к {server.DisplayName}:** `{response.Code}`\n" +
                $"Зайдите на **{server.DisplayName}** и введите в игровой консоли: `.linkdiscord {response.Code}`\n" +
                $"Код действует **{minutes} минут**.\n\n" +
                $"Новый вызов `/linksteam server:{server.Id}` отменяет предыдущий код этого сервера.")
            .WithColor(new DiscordColor(52, 152, 219))
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    private static async Task<ServerRuntime?> ResolveServerAsync(InteractionContext context, string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        if (runtime.TryGetServer(serverId, out ServerRuntime? server) && server != null)
            return server;

        await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"Сервер '{serverId}' не настроен в bot-config.json.")).ConfigureAwait(false);
        return null;
    }

    private static Task DeferAsync(InteractionContext context, bool ephemeral) =>
        context.CreateResponseAsync(
            InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral(ephemeral));

    private static Task DenyAsync(InteractionContext context, ServerConfig server) =>
        context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"Недостаточно прав для {server.DisplayName}. Требуется роль сервера и общая административная роль."));

    private static Task RespondErrorAsync(
        InteractionContext context,
        ServerConfig server,
        Exception exception) =>
        context.EditResponseAsync(new DiscordWebhookBuilder().WithContent(
            $"Ошибка {server.DisplayName}: {FriendlyError(exception)}"));

    private static string FriendlyError(Exception exception) => exception switch
    {
        BridgeApiException api => api.Message,
        HttpRequestException => "не удалось подключиться к Bridge API.",
        TaskCanceledException => "Bridge API не ответил вовремя.",
        InvalidOperationException invalid => invalid.Message,
        _ => "внутренняя ошибка бота."
    };

    private static string BuildPlayerList(PlayersResponse response, bool showNames)
    {
        if (response.Players.Count == 0)
            return "Сервер пуст.";

        var builder = new StringBuilder();
        int shown = 0;
        foreach (ApiPlayer player in response.Players)
        {
            string name = showNames ? DiscordPresentation.Limit(player.DisplayName, 80) : "имя скрыто";
            string line = $"`#{player.Id}` {name} - `{player.Role}`\n";
            if (builder.Length + line.Length > 3800)
                break;
            builder.Append(line);
            shown++;
        }

        if (shown < response.Players.Count)
            builder.Append($"\n...ещё {response.Players.Count - shown} игрок(ов).");
        return builder.ToString();
    }

    private static async Task SendAuditAsync(
        InteractionContext context,
        ServerConfig server,
        string command,
        BotAccessLevel access,
        bool success,
        string result)
    {
        if (server.AuditChannelId == 0)
            return;

        try
        {
            DiscordChannel channel = await context.Client.GetChannelAsync(server.AuditChannelId).ConfigureAwait(false);
            DiscordEmbed embed = new DiscordEmbedBuilder()
                .WithTitle(success ? "Серверная команда выполнена" : "Серверная команда отклонена")
                .AddField("Сервер", server.DisplayName, true)
                .AddField("Уровень", access.DisplayName(), true)
                .AddField("Пользователь", $"{context.User.Mention} (`{context.User.Id}`)")
                .AddField("Команда", DiscordPresentation.Limit(command, 1024))
                .AddField("Результат", DiscordPresentation.Limit(result, 1024))
                .WithColor(success ? new DiscordColor(46, 204, 113) : new DiscordColor(231, 76, 60))
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();
            await channel.SendMessageAsync(new DiscordMessageBuilder().AddEmbed(embed)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[{DateTimeOffset.Now:O}] [{server.DisplayName}] Не удалось отправить аудит: {ex.Message}");
        }
    }
}
