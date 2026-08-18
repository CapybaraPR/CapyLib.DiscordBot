using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace AspectDiscordBot;

internal sealed class BotStaffCommands : ApplicationCommandModule
{
    [SlashCommand("staff_add", "Назначить администратора в реестр персонала (навсегда).")]
    [SlashCommandPermissions(Permissions.Administrator)]
    public async Task StaffAddAsync(
        InteractionContext context,
        [Option("server", "Выберите целевой сервер.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "Выберите игрока онлайн или укажите SteamID64/UserID.")]
        [Autocomplete(typeof(OnlinePlayerAutocompleteProvider))] string player,
        [Option("group", "Выберите внутриигровую должность.")]
        [Autocomplete(typeof(GroupAutocompleteProvider))] string group,
        [Option("scope", "Область действия привилегии.")]
        [Choice("Все серверы проекта (ALL)", "all")]
        [Choice("Только этот сервер", "local")] string scope,
        [Option("discord", "Discord-пользователь для привязки личного дела.")] DiscordUser? discordUser = null,
        [Option("reason", "Причина / основание назначения.")] string reason = "Назначение в состав")
    {
        BotRuntime runtime = BotRuntime.Current;
        await context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral(runtime.Config.EphemeralCommandResponses)).ConfigureAwait(false);
        
        if (!runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Сервер не найден.")).ConfigureAwait(false);
            return;
        }

        BotAccessLevel access = AccessResolver.GetAccess(context.Member, server.Config);
        if (!AccessResolver.CanExecute(context.Member, server.Config, "setgroup"))
        {
            var denyEmbed = new DiscordEmbedBuilder()
                .WithTitle($"🚫 {server.Config.DisplayName} • Доступ ограничен")
                .WithDescription("У вас нет прав для управления составом администрации.")
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(denyEmbed)).ConfigureAwait(false);
            return;
        }

        // Security hierarchy check
        string normalizedGroup = group.Trim().ToLowerInvariant();
        if ((normalizedGroup == "creator" || normalizedGroup == "owner") && access != BotAccessLevel.Creator)
        {
            var secEmbed = new DiscordEmbedBuilder()
                .WithTitle("🔒 Ограничение безопасности")
                .WithDescription("Выдавать высшие ранги (`creator`, `owner`) разрешено исключительно Владельцу проекта.")
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(secEmbed)).ConfigureAwait(false);
            return;
        }

        string targetScope = scope == "local" ? server.Config.Id : "all";

        try
        {
            var request = new StaffAddRequest
            {
                UserId = player,
                DiscordUserId = discordUser?.Id ?? 0,
                DiscordUserName = discordUser != null ? $"{discordUser.Username}" : string.Empty,
                Group = group,
                ServerScope = targetScope,
                ActorDiscordId = context.User.Id,
                ActorDiscordName = context.User.Username,
                Reason = reason
            };

            StaffMemberResponse response = await server.Api.AddStaffAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (response.Success && response.Member != null)
            {
                string scopeText = targetScope == "all" ? "Все серверы проекта (ALL)" : server.Config.DisplayName;
                string discordText = discordUser != null ? discordUser.Mention : "Не привязан";

                var embed = new DiscordEmbedBuilder()
                    .WithTitle("👑 Назначение в состав администрации")
                    .WithDescription($"Администратор успешно внесён в базу данных персонала.")
                    .AddField("👤 Игрок", $"`{response.Member.Id}`", true)
                    .AddField("💼 Должность", $"**`{response.Member.Group}`**", true)
                    .AddField("🌐 Серверы", scopeText, true)
                    .AddField("💬 Discord", discordText, true)
                    .AddField("👑 Назначил", $"{context.User.Mention} (`{context.User.Username}`)", true)
                    .AddField("📝 Причина", DiscordPresentation.Limit(reason, 200), true)
                    .WithColor(new DiscordColor(46, 204, 113))
                    .WithFooter("Капибара SCP:SL • Реестр персонала")
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
            }
            else
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Не удалось добавить администратора.")).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("⚠️ Ошибка назначения")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errEmbed)).ConfigureAwait(false);
        }
    }

    [SlashCommand("staff_remove", "Снять администратора из состава и отозвать права.")]
    [SlashCommandPermissions(Permissions.Administrator)]
    public async Task StaffRemoveAsync(
        InteractionContext context,
        [Option("server", "Выберите сервер.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "Выберите администратора для снятия.")]
        [Autocomplete(typeof(StaffMemberAutocompleteProvider))] string player,
        [Option("reason", "Причина снятия.")] string reason = "Снятие с должности")
    {
        BotRuntime runtime = BotRuntime.Current;
        await context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral(runtime.Config.EphemeralCommandResponses)).ConfigureAwait(false);

        if (!runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Сервер не найден.")).ConfigureAwait(false);
            return;
        }

        BotAccessLevel access = AccessResolver.GetAccess(context.Member, server.Config);
        if (!AccessResolver.CanExecute(context.Member, server.Config, "setgroup"))
        {
            var denyEmbed = new DiscordEmbedBuilder()
                .WithTitle($"🚫 {server.Config.DisplayName} • Доступ ограничен")
                .WithDescription("У вас нет прав для снятия администраторов.")
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(denyEmbed)).ConfigureAwait(false);
            return;
        }

        try
        {
            var request = new StaffRemoveRequest
            {
                UserId = player,
                ActorDiscordId = context.User.Id,
                ActorDiscordName = context.User.Username,
                Reason = reason
            };

            CommandResponse response = await server.Api.RemoveStaffAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (response.Success)
            {
                var embed = new DiscordEmbedBuilder()
                    .WithTitle("❌ Снятие с должности администратора")
                    .WithDescription($"Администратор `{player}` успешно снят с должности. Права отозваны.")
                    .AddField("👑 Снял", $"{context.User.Mention} (`{context.User.Username}`)", true)
                    .AddField("📝 Причина", DiscordPresentation.Limit(reason, 200), true)
                    .WithColor(new DiscordColor(231, 76, 60))
                    .WithFooter("Капибара SCP:SL • Реестр персонала")
                    .WithTimestamp(DateTimeOffset.UtcNow)
                    .Build();

                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
            }
            else
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Администратор не найден или уже снят.")).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("⚠️ Ошибка снятия")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errEmbed)).ConfigureAwait(false);
        }
    }

    [SlashCommand("staff_profile", "Показать личное дело и активность администратора.")]
    [SlashCommandPermissions(Permissions.ManageGuild)]
    public async Task StaffProfileAsync(
        InteractionContext context,
        [Option("server", "Выберите сервер.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("player", "Выберите администратора из списка или укажите SteamID.")]
        [Autocomplete(typeof(StaffMemberAutocompleteProvider))] string player)
    {
        BotRuntime runtime = BotRuntime.Current;
        await context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral(runtime.Config.EphemeralCommandResponses)).ConfigureAwait(false);

        if (!runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Сервер не найден.")).ConfigureAwait(false);
            return;
        }

        try
        {
            StaffListResponse listResp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            StaffMemberDto? member = listResp.Staff?.FirstOrDefault(s =>
                s.Id.Equals(player, StringComparison.OrdinalIgnoreCase) ||
                (s.DiscordUserId != 0 && s.DiscordUserId.ToString() == player) ||
                (!string.IsNullOrEmpty(s.Nickname) && s.Nickname.Contains(player, StringComparison.OrdinalIgnoreCase)));

            if (member == null)
            {
                var notFoundEmbed = new DiscordEmbedBuilder()
                    .WithTitle("🔍 Личное дело не найдено")
                    .WithDescription($"Администратор с идентификатором `{player}` не найден в реестре персонала.")
                    .WithColor(new DiscordColor(230, 126, 34))
                    .WithFooter("Капибара SCP:SL")
                    .Build();
                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(notFoundEmbed)).ConfigureAwait(false);
                return;
            }

            int totalDays = Math.Max(1, (int)(DateTime.UtcNow - member.AssignedAtUtc).TotalDays);
            string totalHours = FormatTime(member.TotalPlaytimeSeconds);
            string weeklyHours = FormatTime(member.WeeklyPlaytimeSeconds);
            string dutyHours = FormatTime(member.DutyPlaytimeSeconds);
            string scopeText = member.ServerScope == "all" ? "Все серверы (ALL)" : member.ServerScope.ToUpper();
            string discordText = member.DiscordUserId != 0 ? $"<@{member.DiscordUserId}>" : (string.IsNullOrEmpty(member.DiscordUserName) ? "Не указан" : member.DiscordUserName);

            var sbDesc = new StringBuilder();
            sbDesc.AppendLine($"👑 **Назначил:** {member.AssignedByDiscordName} (<@{member.AssignedByDiscordId}>)");
            sbDesc.AppendLine($"📅 **Дата назначения:** `{member.AssignedAtUtc:dd.MM.yyyy HH:mm}`");
            sbDesc.AppendLine($"⏱️ **Стаж в составе:** `{totalDays} дн.`");
            sbDesc.AppendLine($"🌐 **Серверы:** `{scopeText}`");

            var embed = new DiscordEmbedBuilder()
                .WithTitle($"🛡️ Личное дело: [{member.Group}] {(!string.IsNullOrEmpty(member.Nickname) ? member.Nickname : member.Id)}")
                .WithDescription(sbDesc.ToString())
                .AddField("💬 Discord", discordText, true)
                .AddField("🎮 SteamID", $"`{member.Id}`", true)
                .AddField("💼 Должность", $"**`{member.Group}`**", true)
                .AddField("📊 Онлайн за неделю", $"**{weeklyHours}**", true)
                .AddField("⌛ Общий онлайн", totalHours, true)
                .AddField("🛡️ На дежурстве", dutyHours, true)
                .AddField("⚖️ Наказания", $"🔨 Баны: **{member.BansCount}** | 🔇 Муты: **{member.MutesCount}** | 👢 Кики: **{member.KicksCount}**", false)
                .AddField("👁️ Последний заход", $"`{member.LastSeenUtc:dd.MM.yyyy HH:mm}` (UTC)", true)
                .WithColor(new DiscordColor(88, 101, 242))
                .WithFooter("Капибара SCP:SL • Реестр персонала")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("⚠️ Ошибка загрузки профиля")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errEmbed)).ConfigureAwait(false);
        }
    }

    [SlashCommand("staff_list", "Показать список всего действующего состава администрации.")]
    [SlashCommandPermissions(Permissions.ManageGuild)]
    public async Task StaffListAsync(
        InteractionContext context,
        [Option("server", "Выберите сервер.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId)
    {
        BotRuntime runtime = BotRuntime.Current;
        await context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral(runtime.Config.EphemeralCommandResponses)).ConfigureAwait(false);

        if (!runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Сервер не найден.")).ConfigureAwait(false);
            return;
        }

        try
        {
            StaffListResponse listResp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            List<StaffMemberDto> staff = listResp.Staff ?? new List<StaffMemberDto>();

            if (staff.Count == 0)
            {
                var emptyEmbed = new DiscordEmbedBuilder()
                    .WithTitle($"📋 {server.Config.DisplayName} • Реестр персонала")
                    .WithDescription("В базе данных пока нет зарегистрированных администраторов.")
                    .WithColor(new DiscordColor(241, 196, 15))
                    .WithFooter("Капибара SCP:SL")
                    .Build();
                await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(emptyEmbed)).ConfigureAwait(false);
                return;
            }

            var embed = new DiscordEmbedBuilder()
                .WithTitle($"📋 {server.Config.DisplayName} • Состав администрации ({staff.Count} чел.)")
                .WithColor(new DiscordColor(88, 101, 242))
                .WithFooter("Капибара SCP:SL • Реестр персонала")
                .WithTimestamp(DateTimeOffset.UtcNow);

            var groups = staff.GroupBy(s => s.Group, StringComparer.OrdinalIgnoreCase);
            foreach (var g in groups)
            {
                var sb = new StringBuilder();
                foreach (StaffMemberDto st in g)
                {
                    string name = !string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id;
                    string discord = st.DiscordUserId != 0 ? $" (<@{st.DiscordUserId}>)" : "";
                    sb.AppendLine($"• **{name}**{discord} — `{st.Id}` (Неделя: `{FormatTime(st.WeeklyPlaytimeSeconds)}`)");
                }
                embed.AddField($"👑 Должность: {g.Key.ToUpper()} ({g.Count()})", sb.ToString(), false);
            }

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed.Build())).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("⚠️ Ошибка загрузки списка персонала")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errEmbed)).ConfigureAwait(false);
        }
    }

    [SlashCommand("staff_stats", "Сводная статистика активности и проверка нормы онлайна стаффа.")]
    [SlashCommandPermissions(Permissions.ManageGuild)]
    public async Task StaffStatsAsync(
        InteractionContext context,
        [Option("server", "Выберите сервер.")]
        [Choice("NR", "nr")]
        [Choice("MRP", "mrp")] string serverId,
        [Option("period", "Период статистики.")]
        [Choice("За текущую неделю", "week")]
        [Choice("За всё время", "all")] string period = "week")
    {
        BotRuntime runtime = BotRuntime.Current;
        await context.CreateResponseAsync(InteractionResponseType.DeferredChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral(runtime.Config.EphemeralCommandResponses)).ConfigureAwait(false);

        if (!runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
        {
            await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("Сервер не найден.")).ConfigureAwait(false);
            return;
        }

        try
        {
            StaffListResponse listResp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            List<StaffMemberDto> staff = listResp.Staff ?? new List<StaffMemberDto>();

            if (staff.Count == 0)
            {
                await context.EditResponseAsync(new DiscordWebhookBuilder().WithContent("В реестре стаффа пока нет записей.")).ConfigureAwait(false);
                return;
            }

            bool isWeek = period == "week";
            var sorted = isWeek
                ? staff.OrderByDescending(s => s.WeeklyPlaytimeSeconds).ToList()
                : staff.OrderByDescending(s => s.TotalPlaytimeSeconds).ToList();

            var sb = new StringBuilder();
            int rank = 1;
            foreach (StaffMemberDto st in sorted.Take(20))
            {
                string medal = rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"#{rank}" };
                string name = !string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id;
                long playtime = isWeek ? st.WeeklyPlaytimeSeconds : st.TotalPlaytimeSeconds;
                sb.AppendLine($"{medal} **{name}** `[{st.Group}]`\n└ Онлайн: **{FormatTime(playtime)}** | Дежурство: **{FormatTime(st.DutyPlaytimeSeconds)}** | Наказания: **{st.BansCount + st.MutesCount + st.KicksCount}**\n");
                rank++;
            }

            var embed = new DiscordEmbedBuilder()
                .WithTitle($"📊 {server.Config.DisplayName} • Рейтинг активности стаффа ({(isWeek ? "Неделя" : "Всё время")})")
                .WithDescription(sb.ToString())
                .WithColor(new DiscordColor(241, 196, 15))
                .WithFooter("Капибара SCP:SL • Аналитика персонала")
                .WithTimestamp(DateTimeOffset.UtcNow)
                .Build();

            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(embed)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("⚠️ Ошибка загрузки статистики")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL")
                .Build();
            await context.EditResponseAsync(new DiscordWebhookBuilder().AddEmbed(errEmbed)).ConfigureAwait(false);
        }
    }

    private static string FormatTime(long seconds)
    {
        if (seconds <= 0) return "0 мин.";
        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours} ч. {ts.Minutes} мин.";
        return $"{ts.Minutes} мин.";
    }
}