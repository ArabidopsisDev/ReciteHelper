using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReciteHelper.Infrastructure.OAuth;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

internal sealed class MockHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => respond(request, token);
    internal static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };
    internal static HttpResponseMessage Sse(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };
    internal static async Task<JsonObject> Body(HttpRequestMessage request) =>
        JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
    internal static async Task<Dictionary<string, string>> Form(HttpRequestMessage request) =>
        OAuthHttp.ParseQuery(await request.Content!.ReadAsStringAsync());
}
internal sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    internal List<TimeSpan> Delays { get; } = [];
    public override DateTimeOffset GetUtcNow() => _now;
    internal Task Delay(TimeSpan duration, CancellationToken token)
    { token.ThrowIfCancellationRequested(); Delays.Add(duration); _now += duration; return Task.CompletedTask; }
}
internal sealed class TestStore : IDisposable
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "recitehelper-native-oauth-tests");
    internal string DirectoryPath { get; } = Path.Combine(Root, Guid.NewGuid().ToString("N"));
    internal PiCredentialStore Store => new(DirectoryPath);
    internal static JsonObject Credential(string access = "synthetic-access-token", long? expires = null) =>
        OAuthHttp.Credential(access, "synthetic-refresh-token", expires ?? DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
    internal async Task Save(string provider, JsonObject? credential = null)
    {
        await using var credentialLock = await Store.AcquireAsync(CancellationToken.None);
        var data = Store.Read();
        data.Credentials[provider] = JsonSerializer.SerializeToElement(credential ?? Credential());
        await Store.SaveAsync(data);
    }
    internal JsonObject Read(string provider) => JsonNode.Parse(Store.Read().Credentials[provider].GetRawText())!.AsObject();
    internal static string Jwt(string account = "synthetic-account") => "e30." + OAuthHttp.Base64Url(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new Dictionary<string, object> { ["https://api.openai.com/auth"] = new { chatgpt_account_id = account } }))) + ".signature";
    public void Dispose()
    {
        var resolved = Path.GetFullPath(DirectoryPath);
        if (!resolved.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid test cleanup path.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
