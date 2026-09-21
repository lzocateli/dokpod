using Dokpod.Bff.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;

namespace Dokpod.Bff.Endpoints;

public static class DownstreamProxy
{
    private static readonly HashSet<string> AllowedRequestHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept",
        "Accept-Encoding",
        "Accept-Language",
        "Cache-Control",
        "Idempotency-Key",
        "If-Match",
        "If-Modified-Since",
        "If-None-Match",
        "If-Unmodified-Since",
        "Range",
        "User-Agent",
        "X-Correlation-Id",
    };

    private static readonly HashSet<string> AllowedResponseHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Accept-Ranges",
        "Cache-Control",
        "Content-Disposition",
        "Content-Encoding",
        "Content-Language",
        "Content-Location",
        "Content-Range",
        "Content-Type",
        "ETag",
        "Expires",
        "Last-Modified",
        "Retry-After",
        "Vary",
        "X-Correlation-Id",
    };

    public static async Task ProxyAsync(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        IOptions<DownstreamApiOptions> options)
    {
        var baseUrl = options.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = "downstream_api_not_configured" });
            return;
        }

        var token = await context.GetTokenAsync("access_token");
        if (string.IsNullOrWhiteSpace(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "missing_access_token" });
            return;
        }

        if (context.Request.ContentLength > options.Value.MaxRequestContentLengthBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "request_body_too_large" });
            return;
        }

        var relativePath = context.Request.Path.Value ?? string.Empty;

        if (HasUnsafePath(relativePath))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "invalid_downstream_path" });
            return;
        }

        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var relativeUri = relativePath.TrimStart('/') + context.Request.QueryString;
        if (Uri.TryCreate(relativeUri, UriKind.Absolute, out _)
            || relativeUri.StartsWith("//", StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "invalid_downstream_path" });
            return;
        }

        var downstreamUri = new Uri(baseUri, relativeUri);
        if (downstreamUri.Scheme != baseUri.Scheme
            || downstreamUri.Host != baseUri.Host
            || downstreamUri.Port != baseUri.Port)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "invalid_downstream_path" });
            return;
        }

        if (context.WebSockets.IsWebSocketRequest)
        {
            await ProxyWebSocketAsync(
                context,
                downstreamUri,
                token,
                timeoutSeconds: options.Value.TimeoutSeconds);
            return;
        }

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), downstreamUri);

        foreach (var header in context.Request.Headers)
        {
            if (AllowedRequestHeaders.Contains(header.Key))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        if ((!HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method)
            && !HttpMethods.IsOptions(context.Request.Method)
            && context.Request.Body.CanRead))
        {
            var content = new StreamContent(new LimitedReadStream(
                context.Request.Body,
                options.Value.MaxRequestContentLengthBytes));
            foreach (var header in context.Request.Headers)
            {
                if (!string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)
                    || !content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                {
                    continue;
                }
            }

            request.Content = content;
        }

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var httpClient = httpClientFactory.CreateClient("dokpod-bff-downstream");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new { error = "downstream_api_unavailable" });
            return;
        }
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsJsonAsync(new { error = "downstream_api_timeout" });
            return;
        }
        catch (InvalidDataException)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(new { error = "request_body_too_large" });
            return;
        }

        using (response)
        {
            if (response.Content.Headers.ContentLength > options.Value.MaxResponseContentLengthBytes)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new { error = "downstream_response_too_large" });
                return;
            }

            try
            {
                await CopyResponseAsync(context, response, options.Value.MaxResponseContentLengthBytes, timeout.Token);
            }
            catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await context.Response.WriteAsJsonAsync(new { error = "downstream_api_timeout" });
            }
        }
    }

    private static async Task CopyResponseAsync(
        HttpContext context,
        HttpResponseMessage response,
        long maxResponseContentLengthBytes,
        CancellationToken cancellationToken)
    {
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var limitedBody = new MemoryStream();
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await responseStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > maxResponseContentLengthBytes)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(
                    new { error = "downstream_response_too_large" },
                    cancellationToken);
                return;
            }

            await limitedBody.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        context.Response.StatusCode = (int)response.StatusCode;
        foreach (var header in response.Headers)
        {
            if (AllowedResponseHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in response.Content.Headers)
        {
            if (AllowedResponseHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        limitedBody.Position = 0;
        await limitedBody.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static bool HasUnsafePath(string path)
    {
        var decodedPath = Uri.UnescapeDataString(path);
        return decodedPath.Contains("://", StringComparison.Ordinal)
            || decodedPath.Split('/', StringSplitOptions.None)
                .Any(segment => segment is "." or "..");
    }

    private static async Task ProxyWebSocketAsync(
        HttpContext context,
        Uri downstreamUri,
        string accessToken,
        int timeoutSeconds)
    {
        var websocketUri = new UriBuilder(downstreamUri)
        {
            Scheme = downstreamUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws"
        }.Uri;
        using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        connectTimeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var downstreamSocket = new ClientWebSocket();
        downstreamSocket.Options.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        foreach (var protocol in context.WebSockets.WebSocketRequestedProtocols)
        {
            downstreamSocket.Options.AddSubProtocol(protocol);
        }

        try
        {
            await downstreamSocket.ConnectAsync(websocketUri, connectTimeout.Token);
            using var localSocket = await context.WebSockets.AcceptWebSocketAsync(downstreamSocket.SubProtocol);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var toDownstream = PumpWebSocketAsync(localSocket, downstreamSocket, lifetime.Token);
            var toLocal = PumpWebSocketAsync(downstreamSocket, localSocket, lifetime.Token);
            await Task.WhenAny(toDownstream, toLocal);
            lifetime.Cancel();
            await Task.WhenAll(toDownstream, toLocal);
        }
        catch (WebSocketException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
            }
        }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
        }
    }

    private static async Task PumpWebSocketAsync(
        WebSocket source,
        WebSocket destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        while (source.State == WebSocketState.Open && destination.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await source.ReceiveAsync(buffer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (WebSocketException)
            {
                return;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await destination.CloseOutputAsync(
                        source.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                        source.CloseStatusDescription,
                        cancellationToken);
                }
                catch (WebSocketException)
                {
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            await destination.SendAsync(
                new ArraySegment<byte>(buffer, 0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                cancellationToken);
        }
    }

    private sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long bytesRead;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => bytesRead;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (bytesRead >= maximumBytes)
            {
                var probe = new byte[1];
                if (await inner.ReadAsync(probe, cancellationToken) > 0)
                {
                    throw new InvalidDataException("O corpo da requisição excede o limite configurado.");
                }

                return 0;
            }

            var remaining = maximumBytes - bytesRead;
            var readBuffer = buffer.Length <= remaining
                ? buffer
                : buffer[..(int)remaining];
            var read = await inner.ReadAsync(readBuffer, cancellationToken);
            bytesRead += read;
            return read;
        }
    }
}
