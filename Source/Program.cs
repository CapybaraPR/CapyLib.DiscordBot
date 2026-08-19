namespace AspectDiscordBot;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Any(arg => arg.Equals("--generate-keys", StringComparison.OrdinalIgnoreCase)))
        {
            var (privKey, pubKey) = BridgeRequestSigner.GenerateKeyPair();
            File.WriteAllText("aspect_bridge.key", privKey, Encoding.UTF8);
            File.WriteAllText("aspect_bridge.pub", pubKey + Environment.NewLine, Encoding.UTF8);
            Console.WriteLine("Сгенерирована новая SSH RSA ключевая пара (2048 бит):");
            Console.WriteLine("  -> aspect_bridge.key (Приватный ключ для бота)");
            Console.WriteLine("  -> aspect_bridge.pub (Публичный ключ для SCP:SL сервера / Capy.API)");
            return 0;
        }

        bool validateOnly = args.Any(argument =>
            argument.Equals("--validate-config", StringComparison.OrdinalIgnoreCase));
        string configArgument = args.FirstOrDefault(argument =>
            !argument.Equals("--validate-config", StringComparison.OrdinalIgnoreCase) &&
            !argument.Equals("--generate-keys", StringComparison.OrdinalIgnoreCase)) ?? "bot-config.json";
        string configPath = Path.GetFullPath(configArgument);
        string statePath = Path.Combine(Path.GetDirectoryName(configPath) ?? AppContext.BaseDirectory, "bot-state.json");

        try
        {
            BotConfig config = BotConfig.Load(configPath);
            if (validateOnly)
            {
                Console.WriteLine(
                    $"Конфигурация корректна. Серверы: {string.Join(", ", config.Servers.Select(server => server.DisplayName + "=" + server.ApiBaseUrl))}.");
                return 0;
            }

            var state = new BotStateStore(statePath, config);
            using BotRuntime runtime = BotRuntime.Initialize(config, state);

            using var shutdown = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                shutdown.Cancel();
            };

            var discord = new DiscordClient(new DiscordConfiguration
            {
                Token = config.BotToken,
                TokenType = TokenType.Bot,
                Intents = DiscordIntents.AllUnprivileged | DiscordIntents.GuildMembers,
                MinimumLogLevel = Microsoft.Extensions.Logging.LogLevel.Information,
                AutoReconnect = true
            });

            SlashCommandsExtension slash = discord.UseSlashCommands();
            slash.RegisterCommands<BotSlashCommands>(config.GuildId);
            slash.RegisterCommands<BotStaffCommands>(config.GuildId);
            var roleSync = new DiscordRoleSyncService(discord, runtime);
            var adminPanel = new AdminPanelService(discord, runtime);
            adminPanel.RegisterEvents();

            await discord.ConnectAsync().ConfigureAwait(false);
            Console.WriteLine(
                $"Discord-бот подключён. Guild: {config.GuildId}. Серверы: {string.Join(", ", runtime.Servers.Select(server => server.Config.DisplayName))}.");
            Console.WriteLine(
                $"DSharpPlus: v{typeof(DiscordClient).Assembly.GetName().Version?.ToString() ?? "unknown"} (локальная сборка из lib/).");

            foreach (ServerRuntime server in runtime.Servers)
            {
                try
                {
                    HealthResponse health = await server.Api.GetHealthAsync(shutdown.Token).ConfigureAwait(false);
                    Console.WriteLine(
                        $"[{server.Config.DisplayName}] Bridge API: {health.Service} {health.Version}.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[{server.Config.DisplayName}] Bridge API пока недоступен: {ex.Message}");
                }
            }

            var updater = new StatusUpdater(discord, runtime);
            var tasks = new List<Task>
            {
                updater.RunAsync(shutdown.Token),
                roleSync.RunAsync(shutdown.Token)
            };
            tasks.AddRange(runtime.Servers.Select(server =>
                new LogForwarder(discord, runtime, server).RunAsync(shutdown.Token)));

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
            }

            await discord.DisconnectAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Критическая ошибка: {ex}");
            return 1;
        }
    }
}
