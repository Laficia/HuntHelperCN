using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed record LoopbackResponse(int Status, byte[] Body, bool Slow = false,
    int DelayMilliseconds = 0, long? AdvertisedLength = null);

internal sealed class LoopbackServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _connections = new();
    private readonly ConcurrentDictionary<string, int> _requests = new();
    private readonly Task _accepting;
    private int _active;
    private int _peak;
    public ConcurrentDictionary<string, LoopbackResponse> Responses { get; } = new();
    public TaskCompletionSource SlowRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int PeakConcurrentRequests => Volatile.Read(ref _peak);

    public LoopbackServer()
    {
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public string Url(string filename) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/{filename}";
    public int RequestCount(string path) => _requests.TryGetValue(path, out var count) ? count : 0;

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _connections.Add(HandleAsync(client));
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (_stop.IsCancellationRequested) { }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        {
            var active = Interlocked.Increment(ref _active);
            int previous;
            do { previous = Volatile.Read(ref _peak); }
            while (active > previous && Interlocked.CompareExchange(ref _peak, active, previous) != previous);
            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(_stop.Token);
                if (request == null) return;
                string? line;
                while ((line = await reader.ReadLineAsync(_stop.Token)) is { Length: > 0 }) { }
                var path = request.Split(' ')[1];
                _requests.AddOrUpdate(path, 1, (_, count) => count + 1);
                var response = Responses.TryGetValue(path, out var configured) ? configured : new LoopbackResponse(404, []);
                var length = response.AdvertisedLength ?? (response.Slow ? 1024 : response.Body.Length);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.Status} Test\r\nContent-Length: {length}\r\nContent-Type: image/jpeg\r\nConnection: close\r\n\r\n");
                if (response.DelayMilliseconds > 0) await Task.Delay(response.DelayMilliseconds, _stop.Token);
                await stream.WriteAsync(headers, _stop.Token);
                if (response.Slow)
                {
                    await stream.WriteAsync(new byte[] { 0xff, 0xd8, 0xff, 0xe0 }, _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                    SlowRequestStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }
                else await stream.WriteAsync(response.Body, _stop.Token);
            }
            catch (Exception error) when (error is IOException or SocketException or OperationCanceledException) { }
            finally { Interlocked.Decrement(ref _active); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _accepting;
        await Task.WhenAll(_connections);
        _stop.Dispose();
    }
}
