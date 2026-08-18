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
            _ => Safe(status.RoundState, "Лобби")
        };
        string actualName = string.IsNullOrWhiteSpace(status.ServerName)
            ? "SCP:SL сервер"
            : Limit(status.ServerName, 200);

        string address = !string.IsNullOrWhiteSpace(status.Address) 
            ? status.Address 
            : (!string.IsNullOrWhiteSpace(server.PublicAddress) ? server.PublicAddress : "127.0.0.1:7777");

        int online = status.GetOnline();
        int maximum = status.GetMaximum();

        return new DiscordEmbedBuilder()
            .WithTitle($"{server.DisplayName} | {actualName}")
            .WithColor(new DiscordColor(46, 204, 113))
            .AddField("Онлайн", $"**{online}/{maximum}**", true)
            .AddField("Адрес", Safe(address, "Не указан"), true)
            .AddField("Состояние", Safe(round, "Лобби"), true)
            .AddField("Время раунда", Safe(status.RoundTime, "00:00"), true)
            .AddField("TPS", status.Tps > 0 ? status.Tps.ToString("0.0") : "60.0", true)
            .WithFooter($"{server.DisplayName} | обновляется автоматически")
            .WithTimestamp(DateTimeOffset.UtcNow)
            .Build();
    }

    public static DiscordEmbed BuildOffline(ServerConfig server, string error) => new DiscordEmbedBuilder()
        .WithTitle($"{server.DisplayName} | сервер недоступен")
        .WithDescription(Safe(Limit(error, 1000), "Сервер временно недоступен или выключен."))
        .AddField("Адрес", Safe(server.PublicAddress, "Не указан"), true)
        .WithColor(new DiscordColor(231, 76, 60))
        .WithFooter($"{server.DisplayName} | Bridge API не отвечает")
        .WithTimestamp(DateTimeOffset.UtcNow)
        .Build();

    public static string Safe(string? value, string fallback = "—")
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

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
