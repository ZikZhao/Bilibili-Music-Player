using System.Net;
using System.Net.Http.Headers;
using bilibili_music_player_windows.Api;

// Dependency-free regression runner: dotnet run --project windows.Tests
var payload = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251)).ToArray();
var uri = new Uri("https://media.example.test/video.mp4");
var tests = new (string Name, Func<Task> Run)[]
{
    ("range reads, forward/backward seeking, synchronous adapter and EOF", async () =>
    {
        using var handler = new RangeHandler(payload);
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        await stream.InitializeAsync();
        Equal(payload.Length, stream.Length);
        var buffer = new byte[32];
        Equal(32, stream.Read(buffer, 0, buffer.Length));
        Bytes(payload[..32], buffer);
        stream.Seek(4000, SeekOrigin.Begin);
        await stream.ReadExactlyAsync(buffer);
        Bytes(payload[4000..4032], buffer);
        stream.Seek(-16, SeekOrigin.Current);
        await stream.ReadExactlyAsync(buffer);
        Bytes(payload[4016..4048], buffer);
        stream.Seek(-32, SeekOrigin.End);
        await stream.ReadExactlyAsync(buffer);
        Bytes(payload[^32..], buffer);
        var requests = handler.Requests;
        Equal(0, await stream.ReadAsync(buffer));
        stream.Seek(100, SeekOrigin.End);
        Equal(0, await stream.ReadAsync(buffer));
        Equal(requests, handler.Requests);
    }),
    ("zero-byte reads and unchanged seeks do not open connections", async () =>
    {
        using var handler = new RangeHandler(payload);
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        Equal(0, await stream.ReadAsync(Memory<byte>.Empty));
        Equal(0, handler.Requests);
        await stream.InitializeAsync();
        stream.Seek(0, SeekOrigin.Current);
        await stream.ReadExactlyAsync(new byte[32]);
        Equal(1, handler.Requests);
    }),
    ("servers ignoring Range still return the requested bytes", async () =>
    {
        using var handler = new RangeHandler(payload) { IgnoreRange = true };
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        await stream.InitializeAsync();
        stream.Seek(6000, SeekOrigin.Begin);
        var buffer = new byte[128];
        await stream.ReadExactlyAsync(buffer);
        Bytes(payload[6000..6128], buffer);
        Equal("video/mp4", stream.ContentType);
    }),
    ("bounded HTTP 206 responses continue without truncating the video", async () =>
    {
        using var handler = new RangeHandler(payload) { MaxRangeSize = 256 };
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);
        Bytes(payload, destination.ToArray());
        Equal(32, handler.Requests);
    }),
    ("cancelled initialization can be retried", async () =>
    {
        using var handler = new RangeHandler(payload) { BlockNextRequest = true };
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        using var cts = new CancellationTokenSource();
        var loading = stream.InitializeAsync(cts.Token);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();
        await Throws<OperationCanceledException>(() => loading);
        await stream.InitializeAsync();
        Equal(payload.Length, stream.Length);
    }),
    ("closing during loading cancels I/O and rejects further access", async () =>
    {
        using var handler = new RangeHandler(payload) { BlockNextRequest = true };
        using var client = CreateClient(handler);
        var stream = new HttpRangeStream(client, uri);
        var loading = stream.InitializeAsync();
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Run(stream.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
        await Throws<OperationCanceledException>(() => loading);
        await Throws<ObjectDisposedException>(() => stream.ReadAsync(new byte[1], 0, 1));
        if (stream.CanRead || stream.CanSeek) throw new Exception("Disposed stream reports usable capabilities.");
        stream.Dispose();
        using var response = await client.GetAsync(uri); // The shared HttpClient must remain usable.
        response.EnsureSuccessStatusCode();
    }),
    ("invalid range offsets are rejected instead of corrupting decoder input", async () =>
    {
        using var handler = new RangeHandler(payload) { InvalidRange = true };
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        await Throws<IOException>(() => stream.InitializeAsync());
    }),
    ("closing during a decoder read interrupts the response body", async () =>
    {
        using var handler = new RangeHandler(payload) { BlockBodyRead = true };
        using var client = CreateClient(handler);
        var stream = new HttpRangeStream(client, uri);
        await stream.InitializeAsync();
        var reading = stream.ReadAsync(new byte[32], 0, 32);
        await handler.BodyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Run(stream.Dispose).WaitAsync(TimeSpan.FromSeconds(2));
        await Throws<OperationCanceledException>(() => reading);
    }),
    ("truncated responses surface an error instead of a false EOF", async () =>
    {
        using var handler = new RangeHandler(payload) { Truncate = true };
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        await Throws<EndOfStreamException>(() => stream.CopyToAsync(Stream.Null));
    }),
    ("concurrent decoder reads preserve all bytes", async () =>
    {
        using var handler = new RangeHandler(payload);
        using var client = CreateClient(handler);
        using var stream = new HttpRangeStream(client, uri);
        var buffers = Enumerable.Range(0, 8).Select(_ => new byte[1024]).ToArray();
        await Task.WhenAll(buffers.Select(buffer => stream.ReadExactlyAsync(buffer).AsTask()));
        Bytes(payload, buffers.SelectMany(buffer => buffer).ToArray());
    }),
};

foreach (var (name, run) in tests.Concat(SearchSessionTests.All).Concat(ResidentTabsTests.All))
{
    try
    {
        await run().WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex}");
        Environment.ExitCode = 1;
    }
}

static HttpClient CreateClient(HttpMessageHandler handler)
{
    var client = new HttpClient(handler);
    client.DefaultRequestHeaders.Referrer = new Uri("https://www.bilibili.com/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PlaybackRegressionTests/1.0");
    return client;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, received {actual}.");
}

static void Bytes(byte[] expected, byte[] actual)
{
    if (!expected.SequenceEqual(actual)) throw new Exception("Video bytes differ after a read or seek.");
}

static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

sealed class RangeHandler(byte[] payload) : HttpMessageHandler
{
    public bool IgnoreRange { get; init; }
    public bool InvalidRange { get; init; }
    public bool Truncate { get; init; }
    public bool BlockBodyRead { get; init; }
    public bool BlockNextRequest { get; set; }
    public int MaxRangeSize { get; init; } = int.MaxValue;
    public int Requests { get; private set; }
    public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource BodyReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (request.Headers.Referrer?.Host != "www.bilibili.com" || request.Headers.UserAgent.Count == 0)
            throw new Exception("CDN headers were lost on a range request.");
        if (BlockNextRequest)
        {
            BlockNextRequest = false;
            RequestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        var start = IgnoreRange ? 0 : (int)(request.Headers.Range?.Ranges.Single().From ?? 0);
        var end = Math.Min(payload.Length, (long)start + MaxRangeSize);
        var bodyEnd = Truncate ? Math.Min(end, start + 8) : end;
        HttpContent content = BlockBodyRead
            ? new StreamContent(new BlockingReadStream(payload, BodyReadStarted))
            : new ByteArrayContent(payload[start..(int)bodyEnd]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (!IgnoreRange)
            content.Headers.ContentRange = new ContentRangeHeaderValue(InvalidRange ? start + 1 : start, end - 1, payload.Length);
        return new HttpResponseMessage(IgnoreRange ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = content };
    }
}

sealed class BlockingReadStream(byte[] payload, TaskCompletionSource started) : MemoryStream(payload)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        started.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
