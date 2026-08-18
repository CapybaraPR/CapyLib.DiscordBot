using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AspectDiscordBot;

internal sealed class BridgeApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly BridgeRequestSigner _signer;

    public BridgeApiClient(ServerConfig config)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.ApiBaseUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AspectDiscordBot/2.0");
        _signer = new BridgeRequestSigner(config);
    }

    public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken) =>
        GetAsync<HealthResponse>("v1/health", cancellationToken);

    public Task<ServerStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        GetAsync<ServerStatus>("v1/status", cancellationToken);

    public Task<PlayersResponse> GetPlayersAsync(CancellationToken cancellationToken) =>
        GetAsync<PlayersResponse>("v1/players", cancellationToken);

    public Task<GroupsResponse> GetGroupsAsync(CancellationToken cancellationToken) =>
        GetAsync<GroupsResponse>("v1/groups", cancellationToken);

    public Task<LinkCodeResponse> CreateLinkCodeAsync(
        LinkCodeRequest request,
        CancellationToken cancellationToken) =>
        PostAsync<LinkCodeRequest, LinkCodeResponse>("v1/links/code", request, cancellationToken);

    public Task<LinkedAccountsResponse> GetLinkedAccountsAsync(CancellationToken cancellationToken) =>
        GetAsync<LinkedAccountsResponse>("v1/links", cancellationToken);

    public Task<LinkRoleSyncResponse> SyncLinkRolesAsync(
        LinkRoleSyncRequest request,
        CancellationToken cancellationToken) =>
        PostAsync<LinkRoleSyncRequest, LinkRoleSyncResponse>("v1/links/sync", request, cancellationToken);

    public Task<StaffListResponse> GetStaffListAsync(CancellationToken cancellationToken) =>
        GetAsync<StaffListResponse>("v1/staff", cancellationToken);

    public Task<StaffMemberResponse> AddStaffAsync(
        StaffAddRequest request,
        CancellationToken cancellationToken) =>
        PostAsync<StaffAddRequest, StaffMemberResponse>("v1/staff/add", request, cancellationToken);

    public Task<CommandResponse> RemoveStaffAsync(
        StaffRemoveRequest request,
        CancellationToken cancellationToken) =>
        PostAsync<StaffRemoveRequest, CommandResponse>("v1/staff/remove", request, cancellationToken);

    public Task<LogEventBatchResponse> GetLogsAsync(long afterId, int limit, CancellationToken cancellationToken)
    {
        string path = $"v1/logs?after_id={Uri.EscapeDataString(afterId.ToString(CultureInfo.InvariantCulture))}&limit={limit.ToString(CultureInfo.InvariantCulture)}";
        return GetAsync<LogEventBatchResponse>(path, cancellationToken);
    }

    public async Task<CommandResponse> ExecuteCommandAsync(CommandRequest request, CancellationToken cancellationToken)
    {
        return await PostAsync<CommandRequest, CommandResponse>("v1/command", request, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        _signer.Dispose();
        _http.Dispose();
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        _signer.ApplyAuthHeaders(request, null);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest requestObj,
        CancellationToken cancellationToken)
    {
        byte[] bodyBytes = JsonSerializer.SerializeToUtf8Bytes(requestObj, JsonDefaults.Options);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(bodyBytes)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        _signer.ApplyAuthHeaders(request, bodyBytes);

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        return await ReadResponseAsync<TResponse>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            ErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<ErrorResponse>(JsonDefaults.Options, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
            }

            string message = string.IsNullOrWhiteSpace(error?.Error)
                ? $"Bridge API вернул HTTP {(int)response.StatusCode}."
                : error.Error;
            throw new BridgeApiException(response.StatusCode, message);
        }

        T? body = await response.Content.ReadFromJsonAsync<T>(JsonDefaults.Options, cancellationToken)
            .ConfigureAwait(false);
        return body ?? throw new BridgeApiException(response.StatusCode, "Bridge API вернул пустой ответ.");
    }
}

internal sealed class BridgeApiException : Exception
{
    public BridgeApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
