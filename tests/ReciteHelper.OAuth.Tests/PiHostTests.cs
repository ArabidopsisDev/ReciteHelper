using System.Text;
using System.Text.Json;
using ReciteHelper.Infrastructure.Services;

namespace ReciteHelper.OAuth.Tests;

public sealed class PiHostTests : IDisposable
{
    private static readonly string TestRoot = Path.Combine(Path.GetTempPath(), "recitehelper-oauth-tests");
    private readonly string _directory = Path.Combine(TestRoot, Guid.NewGuid().ToString("N"));
    private PiCredentialStore Store => new(_directory);
    private PiModelService FakeService => new(Store, Path.Combine(AppContext.BaseDirectory, "fake-bridge.mjs"));

    [Fact]
    public async Task CredentialsAreEncryptedAndKeepAllProviderFields()
    {
        await using var credentialLock = await Store.AcquireAsync(CancellationToken.None);
        var data = new PiCredentialData
        {
            Credentials = new() { ["provider"] = JsonSerializer.SerializeToElement(new { type = "oauth", access = "synthetic-access", refresh = "synthetic-refresh", expires = 123456789L, projectId = "project", extra = new { region = "test" } }) }
        };
        await Store.SaveAsync(data);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(_directory, "pi-oauth.dat"));
        Assert.DoesNotContain("synthetic-access", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("synthetic-refresh", Encoding.UTF8.GetString(bytes));
        var restored = Store.Read();
        Assert.Equal(data.DeviceId, restored.DeviceId);
        Assert.Equal("project", restored.Credentials["provider"].GetProperty("projectId").GetString());
        Assert.Equal("test", restored.Credentials["provider"].GetProperty("extra").GetProperty("region").GetString());
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task CorruptCredentialsAreNotSilentlyReplaced()
    {
        await using var credentialLock = await Store.AcquireAsync(CancellationToken.None);
        var path = Path.Combine(_directory, "pi-oauth.dat");
        var invalid = Encoding.UTF8.GetBytes("corrupted-encrypted-data");
        await File.WriteAllBytesAsync(path, invalid);
        Assert.Throws<InvalidOperationException>(() => Store.Read());
        Assert.Equal(invalid, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task FileLockSerializesIndependentStoreInstancesAndAllowsCancellation()
    {
        var first = await Store.AcquireAsync(CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store.AcquireAsync(cancel.Token));
        await first.DisposeAsync();
        await using var second = await Store.AcquireAsync(CancellationToken.None);
        Assert.True(second.CanWrite);
    }

    [Fact]
    public async Task DeviceIdIsStableAcrossBridgeInvocations()
    {
        var service = FakeService;
        await service.GetProvidersAsync();
        var first = Store.Read().DeviceId;
        await service.GetProvidersAsync();
        Assert.Equal(first, Store.Read().DeviceId);
    }

    [Fact]
    public async Task HostHandlesAuthorizationEventsAndManualPrompts()
    {
        var events = new List<string>();
        await FakeService.LoginAsync("manual", (prompt, _) =>
        {
            Assert.Equal("manual_code", prompt.Type);
            return Task.FromResult("用户授权码");
        }, authEvent => events.Add(authEvent.Type));
        Assert.Equal(["device_code"], events);
        Assert.Equal("new-access-token", Store.Read().Credentials["synthetic"].GetProperty("access").GetString());
    }

    [Fact]
    public async Task CallbackCancelsManualPromptWithoutBlockingProtocolReader()
    {
        var promptCancelled = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        await FakeService.LoginAsync("callback", async (_, token) =>
        {
            try { await Task.Delay(Timeout.Infinite, token); return ""; }
            catch (OperationCanceledException) { promptCancelled = true; throw; }
        }, _ => { }, timeout.Token);
        Assert.True(promptCancelled);
        Assert.True(Store.Read().Credentials.ContainsKey("synthetic"));
    }

    [Fact]
    public async Task TokenRotationIsPersistedBeforeReturningTheModelResponse()
    {
        Assert.Equal("学习文本", await FakeService.RunChatAsync("synthetic", "rotate", "学习文本", null));
        Assert.Equal("new-refresh-token", Store.Read().Credentials["synthetic"].GetProperty("refresh").GetString());
    }

    [Fact]
    public async Task HostPreservesRotatedTokensWhenCancellationRacesRefresh()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FakeService.RunChatAsync("synthetic", "cancel-refresh", "text", null, cancel.Token));
        Assert.Equal("new-refresh-token", Store.Read().Credentials["synthetic"].GetProperty("refresh").GetString());
    }

    [Fact]
    public async Task LargeUnicodeResponsesAreNotTruncated()
    {
        var text = string.Concat(Enumerable.Repeat("中文学习🎓", 20000));
        Assert.Equal(text, await FakeService.RunChatAsync("synthetic", "echo", text, "说明"));
    }

    [Fact]
    public async Task CopiedOfficialBundleListsAllOAuthProvidersThroughTheHost()
    {
        var service = new PiModelService(Store, Path.Combine(AppContext.BaseDirectory, "pi", "bridge.mjs"));
        var providers = await service.GetProvidersAsync();
        Assert.Equal(9, providers.Count);
        Assert.All(providers, provider => Assert.False(provider.LoggedIn));
        foreach (var provider in providers) Assert.NotEmpty(await service.GetModelsAsync(provider.Id));
    }

    [Fact]
    public async Task MissingRuntimeProducesActionableError()
    {
        var service = new PiModelService(Store, Path.Combine(AppContext.BaseDirectory, "fake-bridge.mjs"), Path.Combine(_directory, "missing-node.exe"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetProvidersAsync());
        Assert.Contains("Node.js", error.Message);
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(_directory);
        if (!resolved.StartsWith(Path.GetFullPath(TestRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid test cleanup path.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }
}
