using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace bilibili_music_player_windows.Api
{
    public sealed class HttpRangeStream : Stream
    {
        private readonly HttpClient _client;
        private readonly Uri _uri;
        private HttpResponseMessage? _response;
        private Stream? _responseStream;
        private bool _initialized;
        private long _position;
        private long _length = -1;

        public HttpRangeStream(HttpClient client, Uri uri)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _uri = uri ?? throw new ArgumentNullException(nameof(uri));
        }

        public string ContentType { get; private set; } = "video/mp4";

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            await OpenResponseAsync(0, CancellationToken.None).ConfigureAwait(false);
            _initialized = true;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;

        public override long Length
        {
            get
            {
                if (_length >= 0)
                {
                    return _length;
                }

                throw new NotSupportedException("Stream length is unknown.");
            }
        }

        public override long Position
        {
            get => _position;
            set => Seek(value, SeekOrigin.Begin);
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Use ReadAsync");
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            await EnsureResponseAsync(cancellationToken).ConfigureAwait(false);

            if (_responseStream == null)
            {
                return 0;
            }

            var bytesRead = await _responseStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken)
                .ConfigureAwait(false);
            _position += bytesRead;
            return bytesRead;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long newPosition = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End when _length >= 0 => _length + offset,
                SeekOrigin.End => throw new NotSupportedException("Cannot seek from end without length."),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };

            if (newPosition < 0)
            {
                throw new IOException("Cannot seek to a negative position.");
            }

            _position = newPosition;
            DisposeResponse();
            return _position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("Stream is read-only.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Stream is read-only.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeResponse();
            }

            base.Dispose(disposing);
        }

        private async Task EnsureResponseAsync(CancellationToken cancellationToken)
        {
            if (!_initialized)
            {
                await InitializeAsync().ConfigureAwait(false);
            }

            if (_responseStream == null)
            {
                await OpenResponseAsync(_position, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task OpenResponseAsync(long position, CancellationToken cancellationToken)
        {
            DisposeResponse();

            var request = new HttpRequestMessage(HttpMethod.Get, _uri)
            {
                Headers = { Range = new RangeHeaderValue(position, null) },
            };
            request.Headers.AcceptEncoding.Clear();
            request.Headers.AcceptEncoding.ParseAdd("identity");

            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            _response = response;
            _responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            ContentType = response.Content.Headers.ContentType?.MediaType ?? ContentType;
            var contentRange = response.Content.Headers.ContentRange;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                if (contentRange?.Length.HasValue == true)
                {
                    _length = (long)contentRange.Length.Value;
                }
                else if (response.Content.Headers.ContentLength.HasValue)
                {
                    _length = response.Content.Headers.ContentLength.Value + position;
                }
            }
            else if (response.Content.Headers.ContentLength.HasValue)
            {
                _length = response.Content.Headers.ContentLength.Value;
            }

            if (response.StatusCode == HttpStatusCode.OK && position > 0 && _responseStream != null)
            {
                await SkipAsync(_responseStream, position, cancellationToken).ConfigureAwait(false);
            }
        }

        private void DisposeResponse()
        {
            _responseStream?.Dispose();
            _responseStream = null;

            _response?.Dispose();
            _response = null;
        }

        private static async Task SkipAsync(Stream stream, long bytesToSkip, CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            var remaining = bytesToSkip;

            while (remaining > 0)
            {
                var readSize = (int)Math.Min(buffer.Length, remaining);
                var read = await stream.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                remaining -= read;
            }
        }
    }
}
