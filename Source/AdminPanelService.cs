using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;

namespace AspectDiscordBot;

internal sealed class AdminPanelService
{
    private static readonly Regex SteamId64Regex = new(@"^\d{17}$", RegexOptions.Compiled);

    private readonly DiscordClient _client;
    private readonly BotRuntime _runtime;

    public AdminPanelService(DiscordClient client, BotRuntime runtime)
    {
        _client = client;
        _runtime = runtime;
    }

    public void RegisterEvents()
    {
        _client.ComponentInteractionCreated += OnComponentInteractionAsync;
        _client.ModalSubmitted += OnModalSubmittedAsync;
    }

    public async Task<DiscordMessageBuilder> BuildMainPanelMessageAsync(string serverId, DiscordMember? actor)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
        {
            return new DiscordMessageBuilder().WithContent("⚠️ Нет доступных серверов в конфигурации бота.");
        }

        List<StaffMemberDto> staffList = new();
        PlayersResponse? onlinePlayers = null;
        try
        {
            StaffListResponse resp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            staffList = resp.Staff ?? new List<StaffMemberDto>();
        }
        catch
        {
        }

        try
        {
            onlinePlayers = await server.Api.GetPlayersAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        HashSet<string> onlineIds = (onlinePlayers?.Players ?? new List<ApiPlayer>())
            .Select(p => p.UserId)
            .OfType<string>()
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int onlineStaffCount = staffList.Count(s => onlineIds.Contains(s.Id));
        int quotaMetCount = staffList.Count(s => s.WeeklyPlaytimeSeconds >= 4 * 3600);

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"🛡️ ПАНЕЛЬ УПРАВЛЕНИЯ ПЕРСОНАЛОМ • {server.Config.DisplayName}")
            .WithDescription(
                "Интерактивный центр управления реестром персонала, мониторинга онлайна и личных дел.\n" +
                "Используйте кнопки и меню ниже для навигации и управления.")
            .WithColor(new DiscordColor(88, 101, 242))
            .AddField("👥 Реестр персонала", $"Всего сотрудников: **{staffList.Count}**", true)
            .AddField("🟢 Онлайн сейчас", $"В игре: **{onlineStaffCount}** чел.", true)
            .AddField("⏱️ Норма недели (≥4ч)", $"Выполнили: **{quotaMetCount}** из **{staffList.Count}**", true)
            .WithFooter("Капибара SCP:SL • Система администрирования")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var builder = new DiscordMessageBuilder().AddEmbed(embed.Build());

        // Row 1: Quick navigation
        var row1 = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Primary, $"ap_list:{server.Config.Id}", "👥 Состав администрации", false, new DiscordComponentEmoji("👥")),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_stats:{server.Config.Id}:week", "⏱️ Рейтинг нормы", false, new DiscordComponentEmoji("⏱️")),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_refresh:{server.Config.Id}", "🔄 Обновить", false, new DiscordComponentEmoji("🔄"))
        };
        builder.AddComponents(row1);

        // Row 2: Management buttons
        var row2 = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Success, $"ap_add_flow:{server.Config.Id}", "➕ Назначить сотрудника", false, new DiscordComponentEmoji("➕")),
            new DiscordButtonComponent(ButtonStyle.Danger, $"ap_remove_menu:{server.Config.Id}", "➖ Снять с должности", staffList.Count == 0, new DiscordComponentEmoji("➖"))
        };
        builder.AddComponents(row2);

        // Row 3: Profile Selector Dropdown (max 25 options)
        if (staffList.Count > 0)
        {
            var options = new List<DiscordSelectComponentOption>();
            foreach (StaffMemberDto st in staffList.Take(25))
            {
                bool isOnline = onlineIds.Contains(st.Id);
                string statusEmoji = isOnline ? "🟢" : "⚪";
                string label = $"[{st.Group}] {(!string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id)}";
                if (label.Length > 100) label = label.Substring(0, 97) + "...";

                string desc = $"SteamID: {st.Id} | Неделя: {FormatTimeShort(st.WeeklyPlaytimeSeconds)}";
                if (desc.Length > 100) desc = desc.Substring(0, 97) + "...";

                options.Add(new DiscordSelectComponentOption(
                    label,
                    st.Id,
                    desc,
                    false,
                    new DiscordComponentEmoji(statusEmoji)));
            }

            var select = new DiscordSelectComponent(
                $"ap_profile_select:{server.Config.Id}",
                "🔍 Выберите сотрудника для открытия личного дела...",
                options,
                false,
                1,
                1);
            builder.AddComponents(select);
        }

        return builder;
    }

    public async Task<DiscordMessageBuilder> BuildStaffListMessageAsync(string serverId)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return new DiscordMessageBuilder().WithContent("Сервер не найден.");

        List<StaffMemberDto> staffList = new();
        PlayersResponse? onlinePlayers = null;
        try
        {
            StaffListResponse resp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            staffList = resp.Staff ?? new List<StaffMemberDto>();
            onlinePlayers = await server.Api.GetPlayersAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle($"⚠️ Ошибка загрузки списка персонала ({server.Config.DisplayName})")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .Build();
            return new DiscordMessageBuilder().AddEmbed(errEmbed);
        }

        HashSet<string> onlineIds = (onlinePlayers?.Players ?? new List<ApiPlayer>())
            .Select(p => p.UserId)
            .OfType<string>()
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"👥 ДЕЙСТВУЮЩИЙ СОСТАВ • {server.Config.DisplayName}")
            .WithDescription(staffList.Count == 0 ? "В базе данных пока нет зарегистрированных администраторов." : $"Всего сотрудников в реестре: **{staffList.Count}**")
            .WithColor(new DiscordColor(52, 152, 219))
            .WithFooter("Капибара SCP:SL • Реестр персонала")
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (staffList.Count > 0)
        {
            var categorized = staffList.GroupBy(s => GetCategory(s.Group));
            foreach (var cat in categorized)
            {
                var sb = new StringBuilder();
                foreach (StaffMemberDto st in cat.OrderBy(s => s.Group))
                {
                    bool isOnline = onlineIds.Contains(st.Id);
                    string status = isOnline ? "🟢" : "⚪";
                    string name = !string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id;
                    string discord = st.DiscordUserId != 0 ? $" (<@{st.DiscordUserId}>)" : "";
                    sb.AppendLine($"{status} **{name}**{discord} — `{st.Group}` `[Неделя: {FormatTimeShort(st.WeeklyPlaytimeSeconds)}]`");
                }
                embed.AddField(cat.Key, sb.ToString(), false);
            }
        }

        var builder = new DiscordMessageBuilder().AddEmbed(embed.Build());
        var row = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_back:{server.Config.Id}", "◀️ Главное меню", false, new DiscordComponentEmoji("◀️")),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_list:{server.Config.Id}", "🔄 Обновить список", false, new DiscordComponentEmoji("🔄")),
            new DiscordButtonComponent(ButtonStyle.Success, $"ap_add_flow:{server.Config.Id}", "➕ Назначить", false, new DiscordComponentEmoji("➕"))
        };
        builder.AddComponents(row);
        return builder;
    }

    public async Task<DiscordMessageBuilder> BuildStaffStatsMessageAsync(string serverId, string period)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return new DiscordMessageBuilder().WithContent("Сервер не найден.");

        bool isWeek = period == "week";
        List<StaffMemberDto> staffList = new();
        try
        {
            StaffListResponse resp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            staffList = resp.Staff ?? new List<StaffMemberDto>();
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle($"⚠️ Ошибка загрузки статистики ({server.Config.DisplayName})")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .Build();
            return new DiscordMessageBuilder().AddEmbed(errEmbed);
        }

        var sorted = isWeek
            ? staffList.OrderByDescending(s => s.WeeklyPlaytimeSeconds).ToList()
            : staffList.OrderByDescending(s => s.TotalPlaytimeSeconds).ToList();

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"⏱️ НОРМА И АКТИВНОСТЬ СТАФФА • {server.Config.DisplayName}")
            .WithDescription(
                $"Период статистики: **{(isWeek ? "Текущая неделя" : "За всё время")}**\n" +
                $"Минимальная норма недели: **4 ч.** (зелёный = норма выполнена)\n")
            .WithColor(new DiscordColor(241, 196, 15))
            .WithFooter("Капибара SCP:SL • Аналитика персонала")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var sb = new StringBuilder();
        int rank = 1;
        foreach (StaffMemberDto st in sorted.Take(15))
        {
            string medal = rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", _ => $"**#{rank}**" };
            string name = !string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id;
            long time = isWeek ? st.WeeklyPlaytimeSeconds : st.TotalPlaytimeSeconds;
            string statusIcon = (isWeek && time >= 4 * 3600) ? "✅" : (isWeek && time > 0 ? "⚠️" : "❌");

            sb.AppendLine($"{medal} {statusIcon} **{name}** `[{st.Group}]`\n" +
                          $"└ Онлайн: **{FormatTime(time)}** | Дежурство: **{FormatTime(st.DutyPlaytimeSeconds)}** | Нарушений пресечено: **{st.BansCount + st.MutesCount + st.KicksCount}**\n");
            rank++;
        }

        if (sb.Length == 0)
            sb.Append("В реестре стаффа пока нет записей.");

        embed.AddField("🏆 Рейтинг по онлайну", sb.ToString(), false);

        var builder = new DiscordMessageBuilder().AddEmbed(embed.Build());
        var row = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_back:{server.Config.Id}", "◀️ Главное меню", false, new DiscordComponentEmoji("◀️")),
            new DiscordButtonComponent(ButtonStyle.Primary, $"ap_stats:{server.Config.Id}:{(isWeek ? "all" : "week")}", isWeek ? "📅 Показать за всё время" : "📅 Показать за неделю", false, new DiscordComponentEmoji("📅")),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_stats:{server.Config.Id}:{period}", "🔄 Обновить", false, new DiscordComponentEmoji("🔄"))
        };
        builder.AddComponents(row);
        return builder;
    }

    public async Task<DiscordMessageBuilder> BuildStaffProfileMessageAsync(string serverId, string staffUserId)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return new DiscordMessageBuilder().WithContent("Сервер не найден.");

        List<StaffMemberDto> staffList = new();
        try
        {
            StaffListResponse resp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            staffList = resp.Staff ?? new List<StaffMemberDto>();
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle($"⚠️ Ошибка загрузки личного дела")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .Build();
            return new DiscordMessageBuilder().AddEmbed(errEmbed);
        }

        StaffMemberDto? st = staffList.FirstOrDefault(s => s.Id.Equals(staffUserId, StringComparison.OrdinalIgnoreCase));
        if (st == null)
        {
            var notFound = new DiscordEmbedBuilder()
                .WithTitle("📁 Личное дело не найдено")
                .WithDescription($"Пользователь `{staffUserId}` не найден в реестре персонала.")
                .WithColor(new DiscordColor(241, 196, 15))
                .Build();
            return new DiscordMessageBuilder().AddEmbed(notFound);
        }

        string name = !string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id;
        string discordMention = st.DiscordUserId != 0 ? $"<@{st.DiscordUserId}> (`{st.DiscordUserId}`)" : "*Не привязан*";
        string appointed = st.AssignedAtUtc != default ? st.AssignedAtUtc.ToString("dd.MM.yyyy HH:mm UTC") : "*Неизвестно*";

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"📁 ЛИЧНОЕ ДЕЛО • {name}")
            .WithColor(new DiscordColor(155, 89, 182))
            .AddField("🏷️ Должность", $"`{st.Group}` (Сервер: `{st.ServerScope}`)", true)
            .AddField("🆔 SteamID64", $"`{st.Id}`", true)
            .AddField("💬 Discord", discordMention, true)
            .AddField("⏱️ Активность",
                $"• За эту неделю: **{FormatTime(st.WeeklyPlaytimeSeconds)}**\n" +
                $"• За всё время: **{FormatTime(st.TotalPlaytimeSeconds)}**\n" +
                $"• В дежурстве / спеках: **{FormatTime(st.DutyPlaytimeSeconds)}**", false)
            .AddField("🔨 Модерация",
                $"• Выдано банов: **{st.BansCount}**\n" +
                $"• Выдано мутов: **{st.MutesCount}**\n" +
                $"• Выдано киков: **{st.KicksCount}**", true)
            .AddField("📅 Назначение",
                $"• Дата: **{appointed}**\n" +
                $"• Кем назначен: **{st.AssignedByDiscordName}**", true)
            .WithFooter("Капибара SCP:SL • Досье сотрудника")
            .WithTimestamp(DateTimeOffset.UtcNow);

        if (st.History != null && st.History.Count > 0)
        {
            var histSb = new StringBuilder();
            foreach (var h in st.History.Take(5))
            {
                histSb.AppendLine($"• `[{h.TimestampUtc:dd.MM.yyyy}]` **{h.Action}** ({h.OldGroup} ➔ {h.NewGroup}) — {h.Reason} (от {h.ActorDiscordName})");
            }
            embed.AddField("📜 История изменений", histSb.ToString(), false);
        }

        var builder = new DiscordMessageBuilder().AddEmbed(embed.Build());
        var row = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_back:{server.Config.Id}", "◀️ Главное меню", false, new DiscordComponentEmoji("◀️")),
            new DiscordButtonComponent(ButtonStyle.Danger, $"ap_quick_remove:{server.Config.Id}:{st.Id}", "➖ Снять с должности", false, new DiscordComponentEmoji("➖"))
        };
        builder.AddComponents(row);
        return builder;
    }

    public Task<DiscordMessageBuilder> BuildAddScopeSelectMessageAsync(string serverId)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return Task.FromResult(new DiscordMessageBuilder().WithContent("Сервер не найден."));

        var embed = new DiscordEmbedBuilder()
            .WithTitle("➕ НАЗНАЧЕНИЕ СОТРУДНИКА • ШАГ 1: ВЫБОР СЕРВЕРА")
            .WithDescription(
                "Выберите область действия привилегии из выпадающего списка:\n\n" +
                "• **🌐 Все серверы проекта (ALL)** — назначить на всю сеть серверов.\n" +
                "• **🔴 Капибара | NoRules (NR)** — права только на сервере NoRules.\n" +
                "• **🔵 Капибара | MediumRP (MRP)** — права только на сервере MediumRP.")
            .WithColor(new DiscordColor(46, 204, 113))
            .WithFooter("Капибара SCP:SL • Выбор области действия")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var options = new List<DiscordSelectComponentOption>
        {
            new DiscordSelectComponentOption(
                "🌐 Все серверы проекта (ALL)",
                "all",
                "Привилегия действует на серверах NR и MRP",
                false,
                new DiscordComponentEmoji("🌐")),
            new DiscordSelectComponentOption(
                "🔴 Капибара | NoRules (NR)",
                "nr",
                "Привилегия действует только на сервере NoRules (7777)",
                false,
                new DiscordComponentEmoji("🔴")),
            new DiscordSelectComponentOption(
                "🔵 Капибара | MediumRP (MRP)",
                "mrp",
                "Привилегия действует только на сервере MediumRP (7778)",
                false,
                new DiscordComponentEmoji("🔵"))
        };

        var select = new DiscordSelectComponent(
            $"ap_add_pick_scope:{server.Config.Id}",
            "🌐 Выберите сервер назначения (ALL / NR / MRP)...",
            options,
            false,
            1,
            1);

        var builder = new DiscordMessageBuilder()
            .AddEmbed(embed.Build())
            .AddComponents(select);

        var row = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_back:{server.Config.Id}", "◀️ Отмена / Назад", false, new DiscordComponentEmoji("◀️"))
        };
        builder.AddComponents(row);

        return Task.FromResult(builder);
    }

    public Task<DiscordMessageBuilder> BuildAddRoleSelectMessageAsync(string serverId, string scope)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return Task.FromResult(new DiscordMessageBuilder().WithContent("Сервер не найден."));

        string scopeDisplayName = scope switch
        {
            "nr" => "🔴 Только NoRules (NR)",
            "mrp" => "🔵 Только MediumRP (MRP)",
            _ => "🌐 Все серверы проекта (ALL)"
        };

        // Assignable staff groups ONLY: Administration and Events and Builders (NO ruk.*, NO vip.*)
        var assignable = new List<(string group, string title, string tag, string emoji)>
        {
            // Administration
            ("adm.curator", "Куратор администрации", "Администрация", "🛡️"),
            ("adm.senior", "Старший администратор", "Администрация", "🛡️"),
            ("adm.admin", "Администратор", "Администрация", "🛡️"),
            ("adm.junior", "Младший администратор", "Администрация", "🛡️"),
            ("adm.trainee", "Стажёр", "Администрация", "🛡️"),

            // Events
            ("event.curator", "Куратор ивентеров", "Ивентеры", "🎭"),
            ("event.manager", "Ивент-менеджер", "Ивентеры", "🎭"),
            ("event.senior", "Старший ивентер", "Ивентеры", "🎭"),
            ("event.eventer", "Ивентёр", "Ивентеры", "🎭"),
            ("event.trainee", "Стажёр ивентер", "Ивентеры", "🎭"),

            // Builders
            ("build.curator", "Куратор строителей", "Строители", "🔨"),
            ("build.senior", "Старший строитель", "Строители", "🔨"),
            ("build.builder", "Строитель", "Строители", "🔨"),
            ("build.trainee", "Стажёр строитель", "Строители", "🔨")
        };

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"➕ НАЗНАЧЕНИЕ СОТРУДНИКА • ШАГ 2: ВЫБОР ДОЛЖНОСТИ")
            .WithDescription(
                $"**Выбранная область:** {scopeDisplayName}\n\n" +
                "Выберите должность из выпадающего списка ниже. После выбора откроется окно ввода SteamID64.\n\n" +
                "🔒 *Должности руководства (`ruk.*`) и донат-группы (`vip.*`) исключены из меню выдачи.*")
            .WithColor(new DiscordColor(46, 204, 113))
            .WithFooter("Капибара SCP:SL • Назначение персонала")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var options = new List<DiscordSelectComponentOption>();
        foreach (var r in assignable)
        {
            options.Add(new DiscordSelectComponentOption(
                $"{r.title} ({r.group})",
                r.group,
                $"Отдел: {r.tag} | Ранг: {r.group}",
                false,
                new DiscordComponentEmoji(r.emoji)));
        }

        var select = new DiscordSelectComponent(
            $"ap_add_pick_group:{server.Config.Id}:{scope}",
            "🏷️ Выберите должность для назначения...",
            options,
            false,
            1,
            1);

        var builder = new DiscordMessageBuilder()
            .AddEmbed(embed.Build())
            .AddComponents(select);

        var row = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_add_flow:{server.Config.Id}", "◀️ Назад к выбору сервера", false, new DiscordComponentEmoji("◀️")),
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_back:{server.Config.Id}", "❌ Отмена", false, new DiscordComponentEmoji("❌"))
        };
        builder.AddComponents(row);

        return Task.FromResult(builder);
    }

    public async Task<DiscordMessageBuilder> BuildRemoveMenuMessageAsync(string serverId)
    {
        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return new DiscordMessageBuilder().WithContent("Сервер не найден.");

        List<StaffMemberDto> staffList = new();
        try
        {
            StaffListResponse resp = await server.Api.GetStaffListAsync(CancellationToken.None).ConfigureAwait(false);
            staffList = resp.Staff ?? new List<StaffMemberDto>();
        }
        catch
        {
        }

        var embed = new DiscordEmbedBuilder()
            .WithTitle($"➖ СНЯТИЕ СОТРУДНИКА С ДОЛЖНОСТИ • {server.Config.DisplayName}")
            .WithDescription(
                "Выберите администратора из выпадающего списка ниже для отзыва прав и снятия с должности.\n\n" +
                "⚠️ **Внимание:** После выбора бот автоматически отзовёт права в игре, удалит запись из базы и снимет роль в Discord.")
            .WithColor(new DiscordColor(231, 76, 60))
            .WithFooter("Капибара SCP:SL • Управление доступом")
            .WithTimestamp(DateTimeOffset.UtcNow);

        var builder = new DiscordMessageBuilder().AddEmbed(embed.Build());

        if (staffList.Count > 0)
        {
            var options = new List<DiscordSelectComponentOption>();
            foreach (StaffMemberDto st in staffList.Take(25))
            {
                string label = $"[{st.Group}] {(!string.IsNullOrEmpty(st.Nickname) ? st.Nickname : st.Id)}";
                if (label.Length > 100) label = label.Substring(0, 97) + "...";

                string desc = $"SteamID: {st.Id} | Неделя: {FormatTimeShort(st.WeeklyPlaytimeSeconds)}";
                if (desc.Length > 100) desc = desc.Substring(0, 97) + "...";

                options.Add(new DiscordSelectComponentOption(label, st.Id, desc, false, new DiscordComponentEmoji("👤")));
            }

            var select = new DiscordSelectComponent(
                $"ap_remove_select:{server.Config.Id}",
                "Выберите сотрудника для снятия...",
                options,
                false,
                1,
                1);
            builder.AddComponents(select);
        }

        var row = new List<DiscordComponent>
        {
            new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_back:{server.Config.Id}", "◀️ Отмена / Назад", false, new DiscordComponentEmoji("◀️"))
        };
        builder.AddComponents(row);
        return builder;
    }

    private async Task OnComponentInteractionAsync(DiscordClient sender, ComponentInteractionCreateEventArgs e)
    {
        string id = e.Id;
        if (!id.StartsWith("ap_"))
            return;

        string[] parts = id.Split(':');
        string action = parts[0];
        string serverId = parts.Length > 1 ? parts[1] : "nr";

        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return;

        // Security check
        DiscordMember? member = e.User as DiscordMember ?? await e.Guild.GetMemberAsync(e.User.Id).ConfigureAwait(false);
        if (member == null || !AccessResolver.CanExecute(member, server.Config, "setgroup"))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .AsEphemeral(true)
                    .WithContent("🚫 **Доступ ограничен.** У вас нет прав для управления составом администрации."))
                .ConfigureAwait(false);
            return;
        }

        try
        {
            switch (action)
            {
                case "ap_refresh":
                case "ap_back":
                {
                    DiscordMessageBuilder msg = await BuildMainPanelMessageAsync(serverId, member).ConfigureAwait(false);
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    break;
                }

                case "ap_list":
                {
                    DiscordMessageBuilder msg = await BuildStaffListMessageAsync(serverId).ConfigureAwait(false);
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    break;
                }

                case "ap_stats":
                {
                    string period = parts.Length > 2 ? parts[2] : "week";
                    DiscordMessageBuilder msg = await BuildStaffStatsMessageAsync(serverId, period).ConfigureAwait(false);
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    break;
                }

                case "ap_profile_select":
                {
                    string selectedUserId = e.Values.FirstOrDefault() ?? string.Empty;
                    if (!string.IsNullOrEmpty(selectedUserId))
                    {
                        DiscordMessageBuilder msg = await BuildStaffProfileMessageAsync(serverId, selectedUserId).ConfigureAwait(false);
                        await e.Interaction.CreateResponseAsync(
                            InteractionResponseType.UpdateMessage,
                            new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    }
                    break;
                }

                case "ap_add_flow":
                {
                    DiscordMessageBuilder msg = await BuildAddScopeSelectMessageAsync(serverId).ConfigureAwait(false);
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    break;
                }

                case "ap_add_pick_scope":
                {
                    string selectedScope = e.Values.FirstOrDefault() ?? "all";
                    DiscordMessageBuilder msg = await BuildAddRoleSelectMessageAsync(serverId, selectedScope).ConfigureAwait(false);
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    break;
                }

                case "ap_add_pick_group":
                {
                    string scope = parts.Length > 2 ? parts[2] : "all";
                    string chosenGroup = e.Values.FirstOrDefault() ?? string.Empty;
                    if (string.IsNullOrEmpty(chosenGroup))
                        return;

                    // Block ruk.* and vip.*
                    if (chosenGroup.StartsWith("ruk.", StringComparison.OrdinalIgnoreCase) ||
                        chosenGroup.StartsWith("vip.", StringComparison.OrdinalIgnoreCase) ||
                        chosenGroup.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
                        chosenGroup.Equals("creator", StringComparison.OrdinalIgnoreCase))
                    {
                        await e.Interaction.CreateResponseAsync(
                            InteractionResponseType.ChannelMessageWithSource,
                            new DiscordInteractionResponseBuilder().AsEphemeral(true).WithContent("🚫 Назначение этой роли через бота заблокировано."))
                            .ConfigureAwait(false);
                        return;
                    }

                    string scopeName = scope switch { "nr" => "NR", "mrp" => "MRP", _ => "ALL" };

                    // Open clean modal with SteamID64
                    var modal = new DiscordInteractionResponseBuilder()
                        .WithTitle($"Назначение: {chosenGroup} [{scopeName}]")
                        .WithCustomId($"ap_modal_add_submit:{server.Config.Id}:{scope}:{chosenGroup}")
                        .AddComponents(new TextInputComponent(
                            "SteamID64 администратора",
                            "input_steamid",
                            "Например: 76561198708583029 (17 цифр)",
                            null,
                            true,
                            TextInputStyle.Short,
                            17,
                            25))
                        .AddComponents(new TextInputComponent(
                            "Discord ID пользователя (для привязки)",
                            "input_discord",
                            "1497881868127965264 (опционально)",
                            null,
                            false,
                            TextInputStyle.Short))
                        .AddComponents(new TextInputComponent(
                            "Основание / Причина назначения",
                            "input_reason",
                            "Причина назначения в состав",
                            "Назначение в состав",
                            true,
                            TextInputStyle.Paragraph));

                    await e.Interaction.CreateResponseAsync(InteractionResponseType.Modal, modal).ConfigureAwait(false);
                    break;
                }

                case "ap_remove_menu":
                {
                    DiscordMessageBuilder msg = await BuildRemoveMenuMessageAsync(serverId).ConfigureAwait(false);
                    await e.Interaction.CreateResponseAsync(
                        InteractionResponseType.UpdateMessage,
                        new DiscordInteractionResponseBuilder(msg)).ConfigureAwait(false);
                    break;
                }

                case "ap_remove_select":
                case "ap_quick_remove":
                {
                    string targetUserId = action == "ap_quick_remove" && parts.Length > 2
                        ? parts[2]
                        : e.Values.FirstOrDefault() ?? string.Empty;

                    if (!string.IsNullOrEmpty(targetUserId))
                    {
                        await HandleRemoveExecutionAsync(e, server, targetUserId, member).ConfigureAwait(false);
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AdminPanel] Ошибка обработки компонента {id}: {ex}");
        }
    }

    private async Task OnModalSubmittedAsync(DiscordClient sender, ModalSubmitEventArgs e)
    {
        string id = e.Interaction.Data.CustomId;
        if (!id.StartsWith("ap_modal_add_submit"))
            return;

        string[] parts = id.Split(':');
        string serverId = parts.Length > 1 ? parts[1] : "nr";
        string scope = parts.Length > 2 ? parts[2] : "all";
        string group = parts.Length > 3 ? parts[3] : string.Empty;

        if (!_runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = _runtime.Servers.FirstOrDefault();

        if (server == null)
            return;

        DiscordMember? member = e.Interaction.User as DiscordMember ?? await e.Interaction.Guild.GetMemberAsync(e.Interaction.User.Id).ConfigureAwait(false);
        if (member == null || !AccessResolver.CanExecute(member, server.Config, "setgroup"))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral(true).WithContent("🚫 Доступ ограничен."))
                .ConfigureAwait(false);
            return;
        }

        // Security block on ruk.* & vip.*
        if (group.StartsWith("ruk.", StringComparison.OrdinalIgnoreCase) ||
            group.StartsWith("vip.", StringComparison.OrdinalIgnoreCase) ||
            group.Equals("owner", StringComparison.OrdinalIgnoreCase) ||
            group.Equals("creator", StringComparison.OrdinalIgnoreCase))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral(true).WithContent("🚫 **Отказ безопасности:** Выдача этой роли через бота заблокирована."))
                .ConfigureAwait(false);
            return;
        }

        string rawSteamId = e.Values["input_steamid"]?.Trim() ?? string.Empty;
        string discordStr = e.Values["input_discord"]?.Trim() ?? string.Empty;
        string reason = e.Values["input_reason"]?.Trim() ?? "Назначение в состав";

        // Clean steam ID
        string cleanSteamId = rawSteamId.Replace("@steam", "").Trim();
        if (!SteamId64Regex.IsMatch(cleanSteamId))
        {
            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder()
                    .AsEphemeral(true)
                    .WithContent("❌ **Некорректный SteamID64.** Укажите ровно 17 цифр SteamID игрока (например, `76561198708583029`)."))
                .ConfigureAwait(false);
            return;
        }

        ulong discordId = 0;
        if (!string.IsNullOrEmpty(discordStr))
            ulong.TryParse(discordStr.Replace("<@", "").Replace(">", "").Replace("!", "").Trim(), out discordId);

        string targetScope = scope == "nr" ? "nr" : (scope == "mrp" ? "mrp" : "all");
        string scopeDisplayName = targetScope switch
        {
            "nr" => "🔴 Только NoRules (NR)",
            "mrp" => "🔵 Только MediumRP (MRP)",
            _ => "🌐 Все серверы проекта (ALL)"
        };

        try
        {
            var req = new StaffAddRequest
            {
                UserId = $"{cleanSteamId}@steam",
                DiscordUserId = discordId,
                DiscordUserName = string.Empty,
                Group = group,
                ServerScope = targetScope,
                ActorDiscordId = e.Interaction.User.Id,
                ActorDiscordName = e.Interaction.User.Username,
                Reason = reason
            };

            StaffMemberResponse addResp = await server.Api.AddStaffAsync(req, CancellationToken.None).ConfigureAwait(false);

            var successEmbed = new DiscordEmbedBuilder()
                .WithTitle($"✅ Сотрудник успешно назначен")
                .WithDescription(
                    $"Сотрудник **{addResp.Member?.Nickname ?? cleanSteamId}** успешно добавлен в постоянный реестр!\n\n" +
                    $"• **Должность:** `{group}`\n" +
                    $"• **SteamID64:** `{cleanSteamId}`\n" +
                    $"• **Discord:** {(discordId != 0 ? $"<@{discordId}> (`{discordId}`)" : "*Не привязан*")}\n" +
                    $"• **Область действия:** `{scopeDisplayName}`\n" +
                    $"• **Основание:** *{reason}*")
                .WithColor(new DiscordColor(46, 204, 113))
                .WithFooter("Капибара SCP:SL • Реестр персонала")
                .WithTimestamp(DateTimeOffset.UtcNow);

            var builder = new DiscordMessageBuilder().AddEmbed(successEmbed.Build());
            var row = new List<DiscordComponent>
            {
                new DiscordButtonComponent(ButtonStyle.Primary, $"ap_back:{server.Config.Id}", "◀️ Вернуться в панель", false, new DiscordComponentEmoji("◀️")),
                new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_list:{server.Config.Id}", "👥 Показать состав", false, new DiscordComponentEmoji("👥"))
            };
            builder.AddComponents(row);

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder(builder).AsEphemeral(true)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("❌ Ошибка назначения сотрудника")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .Build();

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(errEmbed).AsEphemeral(true)).ConfigureAwait(false);
        }
    }

    private async Task HandleRemoveExecutionAsync(ComponentInteractionCreateEventArgs e, ServerRuntime server, string targetUserId, DiscordMember member)
    {
        try
        {
            var req = new StaffRemoveRequest
            {
                UserId = targetUserId,
                ActorDiscordId = e.User.Id,
                ActorDiscordName = e.User.Username,
                Reason = "Снятие с должности через Admin Panel"
            };

            CommandResponse remResp = await server.Api.RemoveStaffAsync(req, CancellationToken.None).ConfigureAwait(false);

            var confirmEmbed = new DiscordEmbedBuilder()
                .WithTitle($"🔴 Сотрудник снят с должности • {server.Config.DisplayName}")
                .WithDescription(
                    $"Администратор (`{targetUserId}`) успешно снят с должности.\n\n" +
                    $"• **Результат:** {remResp.Output}\n" +
                    $"• **Действие:** Права на сервере отозваны, запись удалена из реестра.")
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL • Управление персоналом")
                .WithTimestamp(DateTimeOffset.UtcNow);

            var builder = new DiscordMessageBuilder().AddEmbed(confirmEmbed.Build());
            var row = new List<DiscordComponent>
            {
                new DiscordButtonComponent(ButtonStyle.Primary, $"ap_back:{server.Config.Id}", "◀️ Вернуться в панель", false, new DiscordComponentEmoji("◀️")),
                new DiscordButtonComponent(ButtonStyle.Secondary, $"ap_list:{server.Config.Id}", "👥 Показать состав", false, new DiscordComponentEmoji("👥"))
            };
            builder.AddComponents(row);

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.UpdateMessage,
                new DiscordInteractionResponseBuilder(builder)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var errEmbed = new DiscordEmbedBuilder()
                .WithTitle("❌ Ошибка при снятии сотрудника")
                .WithDescription(ex.Message)
                .WithColor(new DiscordColor(231, 76, 60))
                .Build();

            await e.Interaction.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(errEmbed).AsEphemeral(true)).ConfigureAwait(false);
        }
    }

    private static string GetCategory(string group)
    {
        string g = (group ?? string.Empty).ToLowerInvariant();
        if (g.StartsWith("ruk.")) return "👑 Руководство";
        if (g.StartsWith("adm.")) return "🛡️ Администрация";
        if (g.StartsWith("event.")) return "🎭 Ивентеры";
        if (g.StartsWith("build.")) return "🔨 Строители";
        if (g.StartsWith("vip.")) return "⭐ VIP / Медиа";
        return "📋 Другие должности";
    }

    private static string FormatTime(long seconds)
    {
        if (seconds <= 0) return "0 мин.";
        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours} ч. {ts.Minutes} мин.";
        return $"{ts.Minutes} мин.";
    }

    private static string FormatTimeShort(long seconds)
    {
        if (seconds <= 0) return "0ч";
        TimeSpan ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}ч {ts.Minutes}м";
        return $"{ts.Minutes}м";
    }
}
