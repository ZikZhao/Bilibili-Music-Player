using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace bilibili_music_player_windows.Api
{
    /// <summary>A seekable HTTP stream that keeps the client's CDN headers on every range request.</summary>
    public sealed class HttpRangeStream : Stream
    {
        private readonly HttpClient _client;
        private readonly Uri _uri;
        private readonly SemaphoreSlim _ioLock = new(1, 1);
        private readonly CancellationTokenSource _lifetimeCts = new();
        private HttpResponseMessage? _response;
        private Stream? _responseStream;
        private bool _initialized;
        private int _disposed;
        private long _position;
        private long _length = -1;
        private long _responseEnd;

        public HttpRangeStream(HttpClient client, Uri uri)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }

        public string ContentType { get; private set; } = "video/mp4";
        public override bool CanRead => _disposed == 0;
        public override bool CanSeek => _disposed == 0 && _length >= 0;
        public override bool CanWrite => false;

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
            await _ioLock.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (!_initialized)
                {
                    await OpenResponseAsync(_position, linked.Token).ConfigureAwait(false);
                    _initialized = true;
                }
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return _length >= 0 ? _length : throw new NotSupportedException("Stream length is unknown.");
            }
        }

        public override long Position
        {
            get { ThrowIfDisposed(); return _position; }
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush() => ThrowIfDisposed();

        // The WinRT random-access adapter can use either synchronous or asynchronous reads.
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (offset > buffer.Length - count) throw new ArgumentException("Invalid buffer range.");
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(30));
            await _ioLock.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                if (buffer.Length == 0 || (_length >= 0 && _position >= _length)) return 0;

                if (_responseStream is null)
                {
                    await OpenResponseAsync(_position, linked.Token).ConfigureAwait(false);
                    _initialized = true;
                }

                var bytesRead = await _responseStream!.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
                if (bytesRead == 0 && _position < _length)
                    throw new EndOfStreamException("视频连接提前中断，请重试。");

                _position += bytesRead;
                if (_position >= _responseEnd) DisposeResponse();
                return bytesRead;
            }
            catch
            {
                // A cancelled or failed response cannot safely be reused by the next read.
                DisposeResponse();
                throw;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            _ioLock.Wait();
            try
            {
                ThrowIfDisposed();
                var newPosition = origin switch
                {
                    SeekOrigin.Begin => offset,
                    SeekOrigin.Current => checked(_position + offset),
                    SeekOrigin.End => checked(Length + offset),
                    _ => throw new ArgumentOutOfRangeException(nameof(origin)),
                };
                if (newPosition < 0) throw new IOException("Cannot seek to a negative position.");
                if (newPosition != _position)
                {
                    _position = newPosition;
                    DisposeResponse();
                }
                return _position;
            }
            finally
            {
                _ioLock.Release();
            }
        }

        public override void SetLength(long value) => throw new NotSupportedException("Stream is read-only.");
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Stream is read-only.");

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                // Unblock network I/O before waiting for the reader to relinquish its response.
                _lifetimeCts.Cancel();
                _ioLock.Wait();
                try
                {
                    DisposeResponse();
                    _lifetimeCts.Dispose();
                }
                finally { _ioLock.Release(); }
            }
            base.Dispose(disposing);
        }

        private async Task OpenResponseAsync(long position, CancellationToken cancellationToken)
        {
            DisposeResponse();
            using var request = new HttpRequestMessage(HttpMethod.Get, _uri);
            request.Headers.Range = new RangeHeaderValue(position, null);
            request.Headers.AcceptEncoding.ParseAdd("identity");

            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            try
            {
                response.EnsureSuccessStatusCode();
                var range = response.Content.Headers.ContentRange;
                if (response.StatusCode == HttpStatusCode.PartialContent)
                {
                    if (range?.From != position || range.Length is null || range.To is null ||
                        range.To < range.From || range.To >= range.Length)
                        throw new IOException("视频服务器返回了无效的分段数据，请重试。");
                    SetLengthFromResponse(range.Length.Value);
                    _responseEnd = range.To.Value + 1;
                }
                else
                {
                    SetLengthFromResponse(response.Content.Headers.ContentLength
                        ?? throw new IOException("视频服务器未提供文件长度，无法进行拖动播放。"));
                    _responseEnd = _length;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                // Some CDNs ignore Range and return 200. Advance to the requested offset explicitly.
                if (response.StatusCode == HttpStatusCode.OK && position > 0)
                    await SkipAsync(stream, Math.Min(position, _length), cancellationToken).ConfigureAwait(false);

                ContentType = response.Content.Headers.ContentType?.MediaType ?? "video/mp4";
                if (ContentType == "application/octet-stream") ContentType = "video/mp4";
                _response = response;
                _responseStream = stream;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private void DisposeResponse()
        {
            _responseStream?.Dispose();
            _responseStream = null;
            _response?.Dispose();
            _response = null;
        }

        private void SetLengthFromResponse(long length)
        {
            if (_length >= 0 && _length != length)
                throw new IOException("视频文件在播放期间发生变化，请重试。");
            _length = length;
        }

        private static async Task SkipAsync(Stream stream, long bytesToSkip, CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            while (bytesToSkip > 0)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, bytesToSkip)), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("视频连接提前中断，请重试。");
                bytesToSkip -= read;
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }
}
