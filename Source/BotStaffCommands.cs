using System;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;

namespace AspectDiscordBot;

internal sealed class BotStaffCommands : ApplicationCommandModule
{
    [SlashCommand("admin-panel", "Вызвать панель для взаимодействия с администрацией.")]
    [SlashCommandPermissions(Permissions.ManageGuild)]
    public async Task AdminPanelAsync(
        InteractionContext context,
        [Option("server", "Выберите целевой сервер.")]
        [Choice("Капибара | NR", "nr")]
        [Choice("Капибара | MRP", "mrp")] string serverId = "nr")
    {
        BotRuntime runtime = BotRuntime.Current;
        if (!runtime.TryGetServer(serverId, out ServerRuntime? server) || server == null)
            server = runtime.Servers.FirstOrDefault();

        if (server == null)
        {
            await context.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AsEphemeral(true).WithContent("⚠️ Нет доступных серверов."))
                .ConfigureAwait(false);
            return;
        }

        if (!AccessResolver.CanExecute(context.Member, server.Config, "setgroup"))
        {
            var denyEmbed = new DiscordEmbedBuilder()
                .WithTitle($"🚫 {server.Config.DisplayName} • Доступ ограничен")
                .WithDescription("У вас нет прав для доступа к панели управления персоналом.")
                .WithColor(new DiscordColor(231, 76, 60))
                .WithFooter("Капибара SCP:SL • Система безопасности")
                .Build();

            await context.CreateResponseAsync(
                InteractionResponseType.ChannelMessageWithSource,
                new DiscordInteractionResponseBuilder().AddEmbed(denyEmbed).AsEphemeral(true))
                .ConfigureAwait(false);
            return;
        }

        var panelService = new AdminPanelService(context.Client, runtime);
        DiscordMessageBuilder msg = await panelService.BuildMainPanelMessageAsync(server.Config.Id, context.Member).ConfigureAwait(false);

        await context.CreateResponseAsync(
            InteractionResponseType.ChannelMessageWithSource,
            new DiscordInteractionResponseBuilder(msg).AsEphemeral(runtime.Config.EphemeralCommandResponses))
            .ConfigureAwait(false);
    }
}