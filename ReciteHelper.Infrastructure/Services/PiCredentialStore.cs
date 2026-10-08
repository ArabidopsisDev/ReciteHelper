using System.Security.Cryptography;
using System.Text.Json;

namespace ReciteHelper.Infrastructure.Services;

public sealed class PiCredentialData
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, JsonElement> Credentials { get; set; } = [];
}

public sealed class PiCredentialStore(string directory)
{
    private readonly string _path = Path.Combine(directory, "pi-oauth.dat");
    private static readonly byte[] Entropy = "ReciteHelper.pi.oauth.v1"u8.ToArray();
    internal bool Exists => File.Exists(_path);

    public async Task<FileStream> AcquireAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Serialize credential transactions, including refresh and durable
                // persistence, across application instances. Inference runs unlocked.
                return new FileStream(_path + ".lock", FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public PiCredentialData Read()
    {
        if (!File.Exists(_path))
            return new PiCredentialData();
        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            try
            {
                var data = JsonSerializer.Deserialize<PiCredentialData>(plain);
                if (data is null || data.Credentials is null || !Guid.TryParse(data.DeviceId, out _))
                    throw new InvalidDataException("OAuth credential data is invalid.");
                return data;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException("无法读取本机 OAuth 凭据。请使用原 Windows 用户登录，或备份并移走 pi-oauth.dat 后重新授权。", ex);
        }
    }

    public async Task SaveAsync(PiCredentialData data)
    {
        Directory.CreateDirectory(directory);
        var plain = JsonSerializer.SerializeToUtf8Bytes(data);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(encrypted).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
