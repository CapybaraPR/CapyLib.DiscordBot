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
    [SlashCommandPermissions(Permissions.ManageMessages)]
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
                .WithTitle($"👥 {server.Config.DisplayName} • Список игроков ({online}/{maximum})")
                .WithDescription(players)
                .WithColor(new DiscordColor(88, 101, 242))
                .WithFooter($"Капибара SCP:SL • Онлайн: {online}/{maximum}")
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
    [SlashCommandPermissions(Permissions.ManageGuild)]
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
                desc = "На сервере сейчас нет игроков с рангами.";
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
                .WithTitle($"👑 {server.Config.DisplayName} • Игроки с рангами ({ranked.Count})")
                .WithDescription(desc)
                .WithColor(new DiscordColor(241, 196, 15))
                .WithFooter($"Капибара SCP:SL • Онлайн: {online}/{maximum}")
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
    [SlashCommandPermissions(Permissions.Administrator)]
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
    [SlashCommandPermissions(Permissions.BanMembers)]
    public async Task BanAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "Выберите игрока онлайн или укажите PlayerId/SteamID.")]
        [Autocomplete(typeof(OnlinePlayerAutocompleteProvider))] string player,
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
    [SlashCommandPermissions(Permissions.KickMembers)]
    public async Task KickAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "Выберите игрока онлайн или укажите PlayerId/SteamID.")]
        [Autocomplete(typeof(OnlinePlayerAutocompleteProvider))] string player,
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

    [SlashCommand("setgroup", "Установить или снять группу игрока через setgroup.")]
    [SlashCommandPermissions(Permissions.Administrator)]
    public async Task SetGroupAsync(
        InteractionContext context,
        [Option("server", "Выберите NR или MRP.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "Выберите игрока онлайн или укажите PlayerId.")]
        [Autocomplete(typeof(OnlinePlayerAutocompleteProvider))] string player,
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
            await SendAuditAsync(context, server.Config, $"setgroup {player} {group}", access, false, "Недостаточно прав для команды setgroup.")
                .ConfigureAwait(false);
            return;
        }

        string targetGroup = string.IsNullOrWhiteSpace(group) || group.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? "none"
            : group;

        string fullCommand = $"setgroup {player} {targetGroup}";
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

    [SlashCommand("help", "Справка по доступным командам и уровню доступа.")]
    public async Task HelpAsync(InteractionContext context)
    {
        BotRuntime runtime = BotRuntime.Current;

        var accessSb = new StringBuilder();
        foreach (ServerRuntime server in runtime.Servers)
        {
            string roleName = AccessResolver.GetHighestRoleDisplayName(context.Member, server.Config);
            string icon = roleName switch
            {
                "Владелец проекта" => "👑",
                "Ключ Создателя" => "🔑",
                "Высшее Руководство" => "⚡",
                "Руководство (RA)" or "Руководство" => "🛡️",
                "Администрация" => "⚔️",
                "Гость" => "👤",
                _ => "🔹"
            };
            accessSb.AppendLine($"• **{server.Config.DisplayName}:** {icon} `{roleName}`");
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle("📖 Справка по командам бота")
            .WithColor(new DiscordColor(88, 101, 242))
            .AddField("🛡️ Ваш уровень доступа", accessSb.ToString(), false);

        var generalSb = new StringBuilder();
        generalSb.AppendLine("` /server ` `server: <NR/MRP>`\n└ *Состояние, онлайн и адрес выбранного сервера.*\n");
        generalSb.AppendLine("` /linksteam ` `server: <NR/MRP>`\n└ *Получить персональный код привязки Steam к серверу.*");
        embed.AddField("🌐 Общие команды", generalSb.ToString(), false);

        var infoSb = new StringBuilder();
        bool canPlayers = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "players"));
        bool canListRanked = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "listranked"));
        if (canPlayers)
            infoSb.AppendLine("` /players ` `server: <NR/MRP>`\n└ *Список игроков онлайн и их игровые роли.*\n");
        if (canListRanked)
            infoSb.AppendLine("` /listranked ` `server: <NR/MRP>`\n└ *Список присутствующих игроков с рангами/привилегиями.*");

        if (infoSb.Length > 0)
            embed.AddField("👥 Мониторинг игроков", infoSb.ToString().TrimEnd(), false);

        var adminSb = new StringBuilder();
        bool canConsole = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "console"));
        bool canBan = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "ban"));
        bool canKick = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "kick"));
        bool canSetGroup = runtime.Servers.Any(s => AccessResolver.CanExecute(context.Member, s.Config, "setgroup"));

        if (canConsole)
            adminSb.AppendLine("` /console ` `server: <NR/MRP>` `text: <команда>`\n└ *Отправить серверную команду в консоль сервера.*\n");
        if (canKick)
            adminSb.AppendLine("` /kick ` `server: <NR/MRP>` `player: <ID>` `reason: <причина>`\n└ *Кикнуть указанного игрока с сервера.*\n");
        if (canBan)
            adminSb.AppendLine("` /ban ` `server: <NR/MRP>` `player: <ID>` `duration: <время>` `reason: <причина>`\n└ *Заблокировать игрока на сервере.*\n");
        if (canSetGroup)
            adminSb.AppendLine("` /setgroup ` `server: <NR/MRP>` `player: <ID>` `group: <выбор>`\n└ *Установить или снять привилегию/ранг игрока.*");

        if (adminSb.Length > 0)
            embed.AddField("⚡ Управление и модерация", adminSb.ToString().TrimEnd(), false);

        if (canSetGroup)
        {
            var staffSb = new StringBuilder();
            staffSb.AppendLine("` /admin-panel ` `[server: NR/MRP]`\n└ *Интерактивная панель управления: состав, личные дела, норма недели, назначение и снятие стаффа.*");
            embed.AddField("🛡️ Управление персоналом (Admin Panel)", staffSb.ToString().TrimEnd(), false);
        }

        embed.WithFooter("Капибара SCP:SL • Доступ определяется ролями Discord")
             .WithTimestamp(DateTimeOffset.UtcNow);

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
                ? "Команда выполнена без ответа."
                : response.Output;

            DiscordEmbed embed = new DiscordEmbedBuilder()
                .WithTitle(response.Success ? $"✅ {actionTitle} • Успешно" : $"❌ {actionTitle} • Ошибка")
                .WithDescription(DiscordPresentation.CodeBlock(output))
                .AddField("Сервер", server.Config.DisplayName, true)
                .AddField("Уровень", access.DisplayName(), true)
                .AddField("Команда", $"`{DiscordPresentation.Limit(response.Command, 100)}`", true)
                .WithColor(response.Success
                    ? new DiscordColor(46, 204, 113)
                    : new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
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
            .WithTitle($"🔗 {server.DisplayName} • Привязка Steam")
            .WithDescription(
                $"Ваш персональный код привязки:\n```text\n.linkdiscord {response.Code}\n```\n" +
                $"**Как активировать:**\n" +
                $"1. Зайдите на сервер **{server.DisplayName}** в игре.\n" +
                $"2. Откройте игровую консоль (нажав **~** / **ё**).\n" +
                $"3. Введите команду: `.linkdiscord {response.Code}`\n\n" +
                $"⏱️ *Код действует {minutes} мин. Повторный вызов команды аннулирует старый код.*")
            .WithColor(new DiscordColor(88, 101, 242))
            .WithFooter("Капибара SCP:SL")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    private static async Task<ServerRuntime?> ResolveServerAsync(InteractionContext context, string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        if (runtime.TryGetServer(serverId, out ServerRuntime? server) && server != null)
            return server;

        var embed = new DiscordEmbedBuilder()
            .WithTitle("⚠️ Сервер не найден")
            .WithDescription($"Сервер `{serverId}` не настроен в конфигурации бота.")
            .WithColor(new DiscordColor(231, 76, 60))
            .WithFooter("Капибара SCP:SL")
            .Build();
        await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
        return null;
    }

    private static Task DeferAsync(InteractionContext context, bool ephemeral) =>
        context.CreateResponseAsync(
            InteractionResponseType.DeferredChannelMessageWithSource,
            new DiscordInteractionResponseBuilder().AsEphemeral(ephemeral));

    private static Task DenyAsync(InteractionContext context, ServerConfig server)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle($"🚫 {server.DisplayName} • Доступ ограничен")
            .WithDescription("У вас недостаточно прав для выполнения этой команды на выбранном сервере.")
            .WithColor(new DiscordColor(231, 76, 60))
            .WithFooter("Капибара SCP:SL")
            .Build();
        return context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    private static Task RespondErrorAsync(
        InteractionContext context,
        ServerConfig server,
        Exception exception)
    {
        var embed = new DiscordEmbedBuilder()
            .WithTitle($"⚠️ {server.DisplayName} • Ошибка выполнения")
            .WithDescription(FriendlyError(exception))
            .WithColor(new DiscordColor(231, 76, 60))
            .WithFooter("Капибара SCP:SL")
            .Build();
        return context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed));
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        BridgeApiException api => api.Message,
        HttpRequestException => "Не удалось подключиться к серверному Bridge API.",
        TaskCanceledException => "Серверный Bridge API не ответил вовремя.",
        InvalidOperationException invalid => invalid.Message,
        _ => "Внутренняя ошибка обработки команды."
    };

    private static string BuildPlayerList(PlayersResponse response, bool showNames)
    {
        if (response.Players.Count == 0)
            return "На сервере сейчас нет игроков.";

        var builder = new StringBuilder();
        int shown = 0;
        foreach (ApiPlayer player in response.Players)
        {
            string name = showNames ? DiscordPresentation.Limit(player.DisplayName, 60) : "имя скрыто";
            string line = $"`#{player.Id}` {name} — `{player.Role}`\n";
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
                .WithTitle(success ? "🛡️ Аудит • Команда выполнена" : "⚠️ Аудит • Команда отклонена")
                .AddField("Сервер", server.DisplayName, true)
                .AddField("Уровень", access.DisplayName(), true)
                .AddField("Пользователь", $"{context.User.Mention} (`{context.User.Id}`)", false)
                .AddField("Команда", $"`{DiscordPresentation.Limit(command, 1024)}`", false)
                .AddField("Результат", DiscordPresentation.CodeBlock(result, 1000), false)
                .WithColor(success ? new DiscordColor(46, 204, 113) : new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL • Логирование действий")
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
