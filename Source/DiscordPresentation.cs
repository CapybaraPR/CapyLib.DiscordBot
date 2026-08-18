namespace AspectDiscordBot;

internal static class DiscordPresentation
{
    public static DiscordEmbed BuildStatus(ServerConfig server, ServerStatus status)
    {
        string round = status.RoundState switch
        {
            "in_progress" => "Раунд идёт",
            "ended" => "Раунд завершён",
            "lobby" => "Лобби",
            _ => status.RoundState
        };
        string actualName = string.IsNullOrWhiteSpace(status.ServerName)
            ? "SCP:SL сервер"
            : Limit(status.ServerName, 200);

        return new DiscordEmbedBuilder()
            .WithTitle($"{server.DisplayName} | {actualName}")
            .WithColor(new DiscordColor(46, 204, 113))
            .AddField("Онлайн", $"**{status.Online}/{status.Maximum}**", true)
            .AddField("Адрес", string.IsNullOrWhiteSpace(status.Address) ? "Не указан" : status.Address, true)
            .AddField("Состояние", round, true)
            .AddField("Время раунда", status.RoundTime, true)
            .AddField("TPS", status.Tps.ToString("0.0"), true)
            .WithFooter($"{server.DisplayName} | обновляется автоматически")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    public static DiscordEmbed BuildOffline(ServerConfig server, string error) => new DiscordEmbedBuilder()
        .WithTitle($"{server.DisplayName} | сервер недоступен")
        .WithDescription(Limit(error, 1000))
        .AddField("Адрес", string.IsNullOrWhiteSpace(server.PublicAddress) ? "Не указан" : server.PublicAddress, true)
        .WithColor(new DiscordColor(231, 76, 60))
        .WithFooter($"{server.DisplayName} | Bridge API не отвечает")
        .WithTimestamp(DateTimeOffset.UtcNow)
        .Build();

    public static string Limit(string value, int maximum)
    {
        string text = value ?? string.Empty;
        if (text.Length <= maximum)
            return text;
        return text.Substring(0, Math.Max(0, maximum - 3)) + "...";
    }

    public static string CodeBlock(string value, int maximum = 1800)
    {
        string safe = Limit(value ?? string.Empty, maximum).Replace("```", "` ` `");
        return $"```text\n{safe}\n```";
    }
}
