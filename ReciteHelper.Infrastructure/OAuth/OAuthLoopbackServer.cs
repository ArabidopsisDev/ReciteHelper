using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace ReciteHelper.Infrastructure.OAuth;

// Native loopback HTTP listener: no http.sys URL reservations or elevated rights.
internal sealed class OAuthLoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource<Uri> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<Task> _clients = [];
    private readonly Task _accepting;
    private readonly string _path;
    private readonly string? _state;
    private readonly bool _issuedClientId;
    private int _claimed;
    internal string RedirectUri { get; }
    internal Task<Uri> Completion => _completion.Task;

    internal OAuthLoopbackServer(int port, string path, string? state, string redirectHost = "127.0.0.1", bool issuedClientId = false)
    {
        _path = path;
        _state = state;
        _issuedClientId = issuedClientId;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(16);
        RedirectUri = $"http://{redirectHost}:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";
        _accepting = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                var handling = HandleAsync(client);
                lock (_clients) _clients.Add(handling);
                _ = handling.ContinueWith(done => { lock (_clients) _clients.Remove(done); },
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                var stream = client.GetStream();
                var bytes = new byte[8192];
                var count = 0;
                var end = -1;
                while (end < 0 && count < bytes.Length)
                {
                    var received = await stream.ReadAsync(bytes.AsMemory(count), timeout.Token).ConfigureAwait(false);
                    if (received == 0) return;
                    count += received;
                    end = Array.IndexOf(bytes, (byte)'\n', 0, count);
                }
                var request = end < 0 ? [] : Encoding.ASCII.GetString(bytes, 0, end).TrimEnd('\r').Split(' ');
                var status = 400;
                var text = "Invalid OAuth callback.";
                Uri? accepted = null;
                if (request.Length >= 3 && request[0] == "GET" && Uri.TryCreate(new Uri(RedirectUri), request[1], out var uri))
                {
                    var query = OAuthHttp.ParseQuery(uri.Query);
                    var expected = new Uri(RedirectUri);
                    if (uri.Scheme != expected.Scheme || uri.Host != expected.Host || uri.Port != expected.Port || uri.AbsolutePath != _path)
                    { status = 404; text = "Callback route not found."; }
                    else if (_state is not null && !StateMatches(query.GetValueOrDefault("state"), _state)) text = "OAuth state mismatch.";
                    else if (query.ContainsKey("error"))
                    {
                        text = "Authorization was denied. Return to ReciteHelper and try again.";
                        _completion.TrySetException(new InvalidOperationException("账号授权被拒绝，请重新登录。"));
                    }
                    else if (string.IsNullOrWhiteSpace(query.GetValueOrDefault("code"))) text = "Missing authorization code.";
                    else if (_issuedClientId && string.IsNullOrWhiteSpace(query.GetValueOrDefault("client_id"))) text = "Missing issued client ID.";
                    else if (Interlocked.CompareExchange(ref _claimed, 1, 0) == 0 && !_completion.Task.IsCompleted)
                    { status = 200; text = "Authorization completed. ReciteHelper will open automatically."; accepted = uri; }
                    else { status = 409; text = "This authorization has already been handled."; }
                }
                var body = $"<!doctype html><html><meta charset=\"utf-8\"><title>ReciteHelper</title><p>{text}</p></html>";
                var response = Encoding.UTF8.GetBytes($"HTTP/1.1 {status} Result\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}");
                try { await stream.WriteAsync(response, timeout.Token).ConfigureAwait(false); }
                finally { if (accepted is not null) _completion.TrySetResult(accepted); }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (SocketException) { }
        }
    }

    internal static bool StateMatches(string? actual, string expected) => actual is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected));

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _accepting.ConfigureAwait(false);
        Task[] clients;
        lock (_clients) clients = _clients.ToArray();
        await Task.WhenAll(clients).ConfigureAwait(false);
        _stop.Dispose();
    }
}
