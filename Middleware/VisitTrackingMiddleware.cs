using AutoSignals.Models;
using AutoSignals.Services;
using System.Security.Claims;

namespace AutoSignals.Middleware
{
    public class VisitTrackingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly VisitTrackingService _trackingService;

        public VisitTrackingMiddleware(RequestDelegate next, VisitTrackingService trackingService)
        {
            _next = next;
            _trackingService = trackingService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
                || IsStaticPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var originalBody = context.Response.Body;
            using var countingStream = new CountingStream(originalBody);
            context.Response.Body = countingStream;

            try
            {
                await _next(context);
            }
            finally
            {
                context.Response.Body = originalBody;

                var ip = context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
                      ?? context.Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                      ?? context.Connection.RemoteIpAddress?.ToString();

                var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault();
                var path = context.Request.Path.Value;

                _trackingService.Enqueue(new UserVisit
                {
                    UserId = userId,
                    IpAddress = ip?.Length > 50 ? ip[..50] : ip,
                    UserAgent = userAgent?.Length > 500 ? userAgent[..500] : userAgent,
                    PagePath = path?.Length > 256 ? path[..256] : path,
                    Timestamp = DateTime.UtcNow,
                    BytesSent = countingStream.BytesWritten
                });
            }
        }

        private static bool IsStaticPath(PathString path)
        {
            var p = path.Value ?? "";
            return p.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("/css/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("/js/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("/lib/", StringComparison.OrdinalIgnoreCase)
                || p.StartsWith("/images/", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
                || p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class CountingStream : Stream
        {
            private readonly Stream _inner;
            public long BytesWritten { get; private set; }

            public CountingStream(Stream inner) => _inner = inner;

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => _inner.CanWrite;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => _inner.Position = value; }

            public override void Flush() => _inner.Flush();
            public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
            public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void SetLength(long value) => _inner.SetLength(value);

            public override void Write(byte[] buffer, int offset, int count)
            {
                BytesWritten += count;
                _inner.Write(buffer, offset, count);
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            {
                BytesWritten += count;
                await _inner.WriteAsync(buffer, offset, count, ct);
            }

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
            {
                BytesWritten += buffer.Length;
                await _inner.WriteAsync(buffer, ct);
            }
        }
    }
}
