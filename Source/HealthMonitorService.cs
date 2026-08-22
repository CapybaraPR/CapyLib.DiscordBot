using System.Collections.Generic;

namespace AspectDiscordBot;

internal sealed class HealthMonitorService
{
    private readonly DiscordClient _discord;
    private readonly BotRuntime _runtime;
    private readonly Dictionary<string, int> _failureCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alertedServers = new(StringComparer.OrdinalIgnoreCase);

    public HealthMonitorService(DiscordClient discord, BotRuntime runtime)
    {
        _discord = discord;
        _runtime = runtime;
    }

    public async Task RunAsync(CancellationToken token)
    {
        if (_runtime.Config.OwnerDmUserIds.Count == 0)
            return;

        int intervalMs = Math.Max(15, _runtime.Config.HealthCheckIntervalSeconds) * 1000;
        int threshold = Math.Max(1, _runtime.Config.HealthAlertFailuresThreshold);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(intervalMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            foreach (ServerRuntime server in _runtime.Servers)
            {
                string key = server.Config.Id;
                bool healthy;

                try
                {
                    await server.Api.GetHealthAsync(token).ConfigureAwait(false);
                    healthy = true;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    healthy = false;
                }

                if (!healthy)
                {
                    _failureCounts[key] = (_failureCounts.TryGetValue(key, out int count) ? count : 0) + 1;

                    if (_failureCounts[key] >= threshold && _alertedServers.Add(key))
                    {
                        await NotifyAsync(
                            $"🔴 **{server.Config.DisplayName}**: Bridge API недоступен ({_failureCounts[key]} проверок подряд).",
                            token).ConfigureAwait(false);
                    }
                }
                else if (_failureCounts.TryGetValue(key, out int previous) && previous > 0)
                {
                    bool wasAlerted = _alertedServers.Remove(key);
                    _failureCounts[key] = 0;

                    if (wasAlerted)
                    {
                        await NotifyAsync(
                            $"🟢 **{server.Config.DisplayName}**: связь с Bridge API восстановлена.",
                            token).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task NotifyAsync(string message, CancellationToken token)
    {
        foreach (ulong userId in _runtime.Config.OwnerDmUserIds)
        {
            try
            {
                DiscordGuild guild = await _discord.GetGuildAsync(_runtime.Config.GuildId).ConfigureAwait(false);
                DiscordMember member = await guild.GetMemberAsync(userId).ConfigureAwait(false);
                DiscordDmChannel dm = await member.CreateDmChannelAsync().ConfigureAwait(false);
                await dm.SendMessageAsync(message).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HealthMonitor] Не удалось отправить ЛС {userId}: {ex.Message}");
            }
        }
    }
}
