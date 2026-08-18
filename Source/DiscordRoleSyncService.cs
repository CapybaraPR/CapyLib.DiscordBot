namespace AspectDiscordBot;

internal sealed class DiscordRoleSyncService
{
    private readonly DiscordClient _discord;
    private readonly BotRuntime _runtime;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public DiscordRoleSyncService(DiscordClient discord, BotRuntime runtime)
    {
        _discord = discord;
        _runtime = runtime;
        _discord.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllServersAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:O}] Ошибка полной синхронизации Discord-ролей: {ex.Message}");
            }

            await Task.Delay(
                    TimeSpan.FromSeconds(_runtime.Config.LinkRoleSyncSeconds),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SyncAllServersAsync(CancellationToken cancellationToken)
    {
        await _syncLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DiscordGuild guild = await _discord.GetGuildAsync(_runtime.Config.GuildId, true).ConfigureAwait(false);
            foreach (ServerRuntime server in _runtime.Servers)
            {
                try
                {
                    await SyncServerAsync(server, guild, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Синхронизация Discord-ролей: {ex.Message}");
                }
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private static async Task SyncServerAsync(
        ServerRuntime server,
        DiscordGuild guild,
        CancellationToken cancellationToken)
    {
        LinkedAccountsResponse linked = await server.Api.GetLinkedAccountsAsync(cancellationToken)
            .ConfigureAwait(false);
        if (linked.Accounts.Count == 0)
            return;

        // Use the gateway member cache (populated by the GuildMembers intent)
        // to avoid per-user REST calls and Discord rate limits.
        var snapshots = new List<DiscordMemberRoleSnapshot>(linked.Accounts.Count);
        var uncached = new List<LinkedDiscordAccount>();

        foreach (LinkedDiscordAccount account in linked.Accounts)
        {
            if (guild.Members.TryGetValue(account.DiscordUserId, out var member) && member != null)
            {
                snapshots.Add(CreateSnapshot(member));
            }
            else
            {
                uncached.Add(account);
            }
        }

        // Fetch members not present in the gateway cache via REST.
        // Pause after every batch of REST calls to respect Discord rate limits.
        const int restBatchSize = 10;
        for (int i = 0; i < uncached.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (i > 0 && i % restBatchSize == 0)
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);

            LinkedDiscordAccount account = uncached[i];
            try
            {
                var member = await guild.GetMemberAsync(account.DiscordUserId, true)
                    .ConfigureAwait(false);
                snapshots.Add(CreateSnapshot(member));
            }
            catch (NotFoundException)
            {
                snapshots.Add(new DiscordMemberRoleSnapshot
                {
                    DiscordUserId = account.DiscordUserId,
                    DiscordUserName = account.DiscordUserName,
                    DiscordRoleIds = new List<ulong>()
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Не удалось получить Discord-участника {account.DiscordUserId}: {ex.Message}");
            }
        }

        if (snapshots.Count > 0)
        {
            await server.Api.SyncLinkRolesAsync(
                    new LinkRoleSyncRequest { Members = snapshots },
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task OnGuildMemberUpdatedAsync(DiscordClient sender, GuildMemberUpdateEventArgs args)
    {
        if (args.Guild.Id != _runtime.Config.GuildId ||
            args.RolesBefore.Select(role => role.Id).OrderBy(id => id)
                .SequenceEqual(args.RolesAfter.Select(role => role.Id).OrderBy(id => id)))
        {
            return;
        }

        await _syncLock.WaitAsync().ConfigureAwait(false);
        try
        {
            DiscordMemberRoleSnapshot snapshot = CreateSnapshot(args.MemberAfter);
            foreach (ServerRuntime server in _runtime.Servers)
            {
                try
                {
                    await server.Api.SyncLinkRolesAsync(
                            new LinkRoleSyncRequest
                            {
                                Members = new List<DiscordMemberRoleSnapshot> { snapshot }
                            },
                            CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[{DateTimeOffset.Now:O}] [{server.Config.DisplayName}] Немедленная синхронизация Discord-роли: {ex.Message}");
                }
            }
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private static DiscordMemberRoleSnapshot CreateSnapshot(DiscordMember member) => new()
    {
        DiscordUserId = member.Id,
        DiscordUserName = member.Username,
        DiscordRoleIds = member.Roles.Select(role => role.Id).Where(id => id != 0).Distinct().ToList()
    };
}
