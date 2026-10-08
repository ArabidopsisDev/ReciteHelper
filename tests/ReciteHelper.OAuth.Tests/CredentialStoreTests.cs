using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ReciteHelper.Infrastructure.Services;
using ReciteHelper.Infrastructure.OAuth;

namespace ReciteHelper.OAuth.Tests;

public sealed class CredentialStoreTests
{
    [Fact]
    public async Task CredentialsAreEncryptedAndPreserveAllProviderFields()
    {
        using var fixture = new TestStore();
        var credential = TestStore.Credential();
        credential["extra"] = new JsonObject { ["region"] = "test" };
        await fixture.Save("openai", credential);
        var data = fixture.Store.Read();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.DirectoryPath, "pi-oauth.dat"));
        Assert.DoesNotContain("synthetic-access-token", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("synthetic-refresh-token", Encoding.UTF8.GetString(bytes));
        Assert.Equal("test", fixture.Read("openai")["extra"]!["region"]!.GetValue<string>());
        Assert.Equal(data.DeviceId, fixture.Store.Read().DeviceId);
        Assert.Empty(Directory.GetFiles(fixture.DirectoryPath, "*.tmp"));
    }
    [Fact]
    public async Task CorruptCredentialsAreNotSilentlyReplaced()
    {
        using var fixture = new TestStore();
        await using var credentialLock = await fixture.Store.AcquireAsync(CancellationToken.None);
        var path = Path.Combine(fixture.DirectoryPath, "pi-oauth.dat");
        var invalid = Encoding.UTF8.GetBytes("corrupted-encrypted-data");
        await File.WriteAllBytesAsync(path, invalid);
        Assert.Throws<InvalidOperationException>(() => fixture.Store.Read());
        Assert.Equal(invalid, await File.ReadAllBytesAsync(path));
    }
    [Fact]
    public async Task IndependentStoreInstancesSerializeAndAllowCancellation()
    {
        using var fixture = new TestStore();
        var first = await fixture.Store.AcquireAsync(CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Store.AcquireAsync(cancel.Token));
        await first.DisposeAsync();
        await using var second = await fixture.Store.AcquireAsync(CancellationToken.None);
        Assert.True(second.CanWrite);
    }
    [Fact]
    public async Task MetadataIsOfflineAndInstallationIdStaysStable()
    {
        using var fixture = new TestStore();
        using var client = new HttpClient(new MockHttp((_, _) => throw new InvalidOperationException("Metadata must not make HTTP requests")));
        using var service = new PiModelService(fixture.Store, client);
        var providers = await service.GetProvidersAsync();
        Assert.Equal(9, providers.Count);
        var device = fixture.Store.Read().DeviceId;
        foreach (var provider in providers) Assert.NotEmpty(await service.GetModelsAsync(provider.Id));
        await service.GetProvidersAsync();
        Assert.Equal(device, fixture.Store.Read().DeviceId);
    }
    [Fact]
    public async Task LogoutRemovesOnlySelectedProvider()
    {
        using var fixture = new TestStore();
        await fixture.Save("openai");
        await fixture.Save("anthropic");
        using var client = new HttpClient();
        using var service = new PiModelService(fixture.Store, client);
        await service.LogoutAsync("openai");
        Assert.False(fixture.Store.Read().Credentials.ContainsKey("openai"));
        Assert.True(fixture.Store.Read().Credentials.ContainsKey("anthropic"));
    }
    [Fact]
    public async Task ParallelCallersRefreshOnceAcrossSeparateServiceInstances()
    {
        using var fixture = new TestStore();
        await fixture.Save("anthropic", TestStore.Credential(expires: 1));
        var count = 0;
        using var client = new HttpClient(new MockHttp(async (request, _) =>
        {
            if (request.RequestUri!.Host == "platform.claude.com")
            {
                Interlocked.Increment(ref count);
                await Task.Delay(40);
                return MockHttp.Json(new { access_token = "rotated-access-token", refresh_token = "rotated-refresh-token", expires_in = 3600 });
            }
            Assert.Equal("rotated-access-token", request.Headers.Authorization!.Parameter);
            Assert.Equal("rotated-refresh-token", OAuthHttp.Text(fixture.Read("anthropic"), "refresh"));
            return MockHttp.Json(new { content = new[] { new { type = "text", text = "answer" } }, stop_reason = "end_turn" });
        }));
        using var one = new PiModelService(fixture.Store, client);
        using var two = new PiModelService(fixture.Store, client);
        var model = PiModelCatalog.GetModels("anthropic")[0].Id;
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => (i % 2 == 0 ? one : two).RunChatAsync("anthropic", model, "prompt", null)));
        Assert.Equal(1, count);
        Assert.All(results, result => Assert.Equal("answer", result));
    }
    [Fact]
    public async Task CancellationDuringRefreshStillPersistsRotatedCredential()
    {
        using var fixture = new TestStore();
        await fixture.Save("anthropic", TestStore.Credential(expires: 1));
        using var cancel = new CancellationTokenSource();
        using var client = new HttpClient(new MockHttp(async (_, token) =>
        {
            cancel.Cancel();
            Assert.False(token.IsCancellationRequested);
            await Task.Delay(30, token);
            return MockHttp.Json(new { access_token = "new-access", refresh_token = "must-survive-cancellation", expires_in = 3600 });
        }));
        using var service = new PiModelService(fixture.Store, client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunChatAsync("anthropic", PiModelCatalog.GetModels("anthropic")[0].Id, "prompt", null, cancel.Token));
        Assert.Equal("must-survive-cancellation", OAuthHttp.Text(fixture.Read("anthropic"), "refresh"));
    }
    [Fact]
    public async Task FailedRefreshPreservesCredentialsAndDoesNotLeakTokens()
    {
        using var fixture = new TestStore();
        await fixture.Save("anthropic", TestStore.Credential(expires: 1));
        using var client = new HttpClient(new MockHttp((_, _) => Task.FromResult(MockHttp.Json(new { error = "invalid_grant", message = "synthetic-refresh-token" }, System.Net.HttpStatusCode.Unauthorized))));
        using var service = new PiModelService(fixture.Store, client);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RunChatAsync("anthropic", PiModelCatalog.GetModels("anthropic")[0].Id, "prompt", null));
        Assert.DoesNotContain("synthetic-refresh-token", error.ToString());
        Assert.Equal("synthetic-refresh-token", OAuthHttp.Text(fixture.Read("anthropic"), "refresh"));
    }
    [Fact]
    public async Task InferenceRequestsAreNotSerializedByCredentialLock()
    {
        using var fixture = new TestStore();
        await fixture.Save("anthropic");
        var active = 0;
        var maximum = 0;
        var overlapping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new MockHttp(async (_, _) =>
        {
            var current = Interlocked.Increment(ref active);
            if (current >= 2) { Interlocked.Exchange(ref maximum, current); overlapping.TrySetResult(); }
            await overlapping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Interlocked.Decrement(ref active);
            return MockHttp.Json(new { content = new[] { new { type = "text", text = "answer" } }, stop_reason = "end_turn" });
        }));
        using var service = new PiModelService(fixture.Store, client);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => service.RunChatAsync("anthropic", PiModelCatalog.GetModels("anthropic")[0].Id, "prompt", null)));
        Assert.True(maximum > 1);
    }
}
