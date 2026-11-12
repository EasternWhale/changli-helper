using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

internal sealed record Reply(string Answer, string? ConversationId);

internal sealed class AgentApi : IDisposable
{
    private readonly HttpClient client;
    private readonly Uri endpoint;
    private readonly string origin;
    private readonly string accessCode;

    public AgentApi()
    {
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Changli.Connection")
            ?? throw new InvalidOperationException("客户端缺少服务配置。");
        using var reader = new StreamReader(resource);
        origin = reader.ReadLine() ?? throw new InvalidOperationException("客户端缺少服务地址。");
        accessCode = reader.ReadLine() ?? throw new InvalidOperationException("客户端缺少访问码。");
        endpoint = new Uri(new Uri(origin), "/api/chat");
        client = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseProxy = endpoint.Scheme == Uri.UriSchemeHttps
        }) { Timeout = TimeSpan.FromSeconds(130) };
    }

    public bool UsesPublicEndpoint => endpoint.Scheme == Uri.UriSchemeHttps;

    public async Task<Reply> SendAsync(string message, string userId, string? conversationId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("X-Coze-Access", accessCode);
        request.Content = JsonContent.Create(new { message, userId, conversationId });
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string messageText = response.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "当前版本的访问权限已失效，请联系分享人。"
                : "长离助手暂时无法回复，请稍后重试或联系分享人。";
            throw new InvalidOperationException(messageText);
        }
        var reply = await response.Content.ReadFromJsonAsync<Reply>(cancellationToken);
        if (string.IsNullOrWhiteSpace(reply?.Answer)) throw new JsonException("Missing text reply.");
        return reply;
    }

    public void Dispose() => client.Dispose();
}
