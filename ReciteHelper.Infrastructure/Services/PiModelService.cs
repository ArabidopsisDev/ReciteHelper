using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReciteHelper.Core.Interfaces.Services;

namespace ReciteHelper.Infrastructure.Services;

public sealed class PiModelService : IPiModelService
{
    private readonly PiCredentialStore _store;
    private readonly string _bridgePath;
    private readonly string _nodePath;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PiModelService() : this(
        new PiCredentialStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReciteHelper")),
        Path.Combine(AppContext.BaseDirectory, "pi", "bridge.mjs")) { }

    public PiModelService(PiCredentialStore store, string bridgePath, string? nodePath = null)
    {
        _store = store;
        _bridgePath = bridgePath;
        var bundledNode = Path.Combine(Path.GetDirectoryName(bridgePath)!, "node.exe");
        _nodePath = nodePath ?? Environment.GetEnvironmentVariable("RECITEHELPER_NODE_PATH")
            ?? (File.Exists(bundledNode) ? bundledNode : "node");
    }

    public async Task<IReadOnlyList<PiOAuthProvider>> GetProvidersAsync(CancellationToken cancellationToken = default)
        => await InvokeAsync<PiOAuthProvider[]>(new { command = "providers" }, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<PiChatModel>> GetModelsAsync(string provider, CancellationToken cancellationToken = default)
        => await InvokeAsync<PiChatModel[]>(new { command = "models", provider }, cancellationToken).ConfigureAwait(false);

    public async Task LoginAsync(string provider, Func<PiAuthPrompt, CancellationToken, Task<string>> prompt,
        Action<PiAuthEvent> notify, CancellationToken cancellationToken = default)
        => _ = await InvokeAsync<bool>(new { command = "login", provider }, cancellationToken, prompt, notify).ConfigureAwait(false);

    public async Task LogoutAsync(string provider, CancellationToken cancellationToken = default)
        => _ = await InvokeAsync<bool>(new { command = "logout", provider }, cancellationToken).ConfigureAwait(false);

    public Task<string> RunChatAsync(string provider, string model, string prompt, string? instructions,
        CancellationToken cancellationToken = default)
        => InvokeAsync<string>(new { command = "chat", provider, model, prompt, instructions }, cancellationToken);

    private async Task<T> InvokeAsync<T>(object request, CancellationToken cancellationToken,
        Func<PiAuthPrompt, CancellationToken, Task<string>>? prompt = null, Action<PiAuthEvent>? notify = null)
    {
        if (!File.Exists(_bridgePath))
            throw new InvalidOperationException("缺少 pi OAuth 组件。请使用完整发布包，或重新构建 tools/pi-bridge。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = timeout.Token;
        await _operations.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await using var credentialLock = await _store.AcquireAsync(token).ConfigureAwait(false);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            var data = _store.Read();
            // Persist the stable device ID before a provider needs it on first login.
            await _store.SaveAsync(data).ConfigureAwait(false);
            var start = new ProcessStartInfo
            {
                FileName = _nodePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(_bridgePath)!
            };
            start.ArgumentList.Add(_bridgePath);
            using var process = new Process { StartInfo = start };
            try { process.Start(); }
            catch (Win32Exception)
            {
                throw new InvalidOperationException("无法启动 pi OAuth。请安装 Node.js 22.19 或更新版本，并确保 node 在 PATH 中。");
            }

            // Never include provider stderr or JSON protocol frames in logs/errors.
            var stderr = DrainAsync(process.StandardError);
            using var writes = new SemaphoreSlim(1, 1);
            var prompts = new ConcurrentDictionary<int, CancellationTokenSource>();
            var promptTasks = new List<Task>();
            async Task WriteAsync(object message)
            {
                await writes.WaitAsync().ConfigureAwait(false);
                try
                {
                    await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message, JsonOptions)).ConfigureAwait(false);
                    await process.StandardInput.FlushAsync().ConfigureAwait(false);
                }
                finally { writes.Release(); }
            }

            async Task HandlePromptAsync(int id, PiAuthPrompt authPrompt, CancellationTokenSource source)
            {
                try
                {
                    if (prompt is null) throw new InvalidOperationException("Login interaction is required.");
                    var value = await prompt(authPrompt, source.Token).ConfigureAwait(false);
                    source.Token.ThrowIfCancellationRequested();
                    await WriteAsync(new { type = "reply", id, value }).ConfigureAwait(false);
                }
                catch
                {
                    try { await WriteAsync(new { type = "reply", id, error = "Login prompt cancelled." }).ConfigureAwait(false); }
                    catch (IOException) { }
                    catch (InvalidOperationException) { }
                }
                finally { prompts.TryRemove(id, out _); source.Dispose(); }
            }

            async Task CancelAsync()
            {
                try { await WriteAsync(new { type = "cancel" }).ConfigureAwait(false); }
                catch (IOException) { }
                catch (InvalidOperationException) { }
                // Let pi abort its callback server and acknowledge any token that
                // already rotated; kill the process tree if it cannot cooperate.
                await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(25))).ConfigureAwait(false);
                TryKill(process);
            }

            Task? cancellationTask = null;
            using var registration = token.Register(() => cancellationTask = CancelAsync());
            try
            {
                var input = JsonSerializer.SerializeToNode(request, JsonOptions)!.AsObject();
                input["credentials"] = JsonSerializer.SerializeToNode(data.Credentials, JsonOptions);
                input["deviceId"] = data.DeviceId;
                await WriteAsync(input).ConfigureAwait(false);
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    JsonObject message;
                    try { message = JsonNode.Parse(line)!.AsObject(); }
                    catch (Exception ex) when (ex is JsonException or InvalidOperationException)
                    { throw new InvalidOperationException("pi OAuth 组件返回了无效数据。请重新构建组件。"); }
                    var type = message["type"]?.GetValue<string>();
                    var id = message["id"]?.GetValue<int>() ?? 0;
                    switch (type)
                    {
                        case "store":
                            data.Credentials = message["credentials"]!.Deserialize<Dictionary<string, JsonElement>>(JsonOptions)!;
                            // Persist without cancellation: a newly rotated token must
                            // survive a cancellation that raced the refresh response.
                            await _store.SaveAsync(data).ConfigureAwait(false);
                            await WriteAsync(new { type = "reply", id, value = true }).ConfigureAwait(false);
                            break;
                        case "event":
                            notify?.Invoke(message["event"]!.Deserialize<PiAuthEvent>(JsonOptions)!);
                            break;
                        case "prompt":
                            var source = CancellationTokenSource.CreateLinkedTokenSource(token);
                            prompts[id] = source;
                            promptTasks.Add(HandlePromptAsync(id, message["prompt"]!.Deserialize<PiAuthPrompt>(JsonOptions)!, source));
                            break;
                        case "cancel_prompt":
                            if (prompts.TryGetValue(id, out var pending))
                            {
                                try { pending.Cancel(); }
                                catch (ObjectDisposedException) { }
                            }
                            break;
                        case "result":
                            token.ThrowIfCancellationRequested();
                            return message["result"]!.Deserialize<T>(JsonOptions)!;
                        case "error":
                            token.ThrowIfCancellationRequested();
                            throw new InvalidOperationException($"pi：{message["message"]?.GetValue<string>()}");
                    }
                }
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("pi OAuth 组件已退出。请检查 Node.js 版本（至少 22.19）与组件完整性。");
            }
            finally
            {
                registration.Dispose();
                foreach (var source in prompts.Values)
                {
                    try { source.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                TryKill(process);
                await Task.WhenAll(promptTasks).ConfigureAwait(false);
                if (cancellationTask is not null) await cancellationTask.ConfigureAwait(false);
                await stderr.ConfigureAwait(false);
            }
        }
        finally { _operations.Release(); }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }
}
