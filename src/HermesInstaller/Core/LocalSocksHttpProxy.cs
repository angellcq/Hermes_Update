using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HermesInstaller.Core
{
    /// <summary>
    /// E14：把 SSH 的 SOCKS5（-D）桥成本机 HTTP 代理。
    ///
    /// 原因：.NET Framework 的 HttpClient / HttpWebRequest 只认 HTTP 代理（SOCKS 支持是
    /// .NET 6+ 才有），而本工具的探针、install.ps1 下载、Electron 预热全是 in-process
    /// HttpClient。所以这里实现一个最小 HTTP 代理：
    ///   - CONNECT host:port → 经 SOCKS5 连目标 → 回 200 → 双向转发（HTTPS 走这条）；
    ///   - GET http://host/path → 经 SOCKS5 连目标 → 改写成 origin-form 转发（HTTP 走这条）。
    /// 只监听 127.0.0.1，不做鉴权（本机同用户进程本来就能直接用 SOCKS 端口）。
    /// </summary>
    public sealed class LocalSocksHttpProxy : IDisposable
    {
        private const int MaxHeaderBytes = 64 * 1024;
        private const int HeaderTimeoutMs = 30000;

        private readonly Logger _logger;
        private readonly object _sync = new object();
        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private Task _acceptTask;
        private volatile bool _stopped;

        public LocalSocksHttpProxy(Logger logger)
        {
            _logger = logger;
        }

        public int Port { get; private set; }

        public bool IsRunning
        {
            get { return _listener != null && !_stopped; }
        }

        /// <summary>启动监听。<paramref name="listenPort"/> 传 0 表示由系统分配（测试用）。</summary>
        public bool Start(string socksHost, int socksPort, int listenPort)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, listenPort);
                listener.Start(128);
                _listener = listener;
                _stopped = false;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _cts = new CancellationTokenSource();
                _acceptTask = Task.Run(async delegate
                {
                    await AcceptLoopAsync(socksHost, socksPort, _cts.Token).ConfigureAwait(false);
                });
                Info("[PROXY] HTTP→SOCKS5 桥已监听 127.0.0.1:" + Port
                    + "（上游 SOCKS5 " + socksHost + ":" + socksPort + "）");
                return true;
            }
            catch (Exception ex)
            {
                Warn("[PROXY] 启动本地 HTTP 代理失败: " + ex.Message);
                try { if (_listener != null) { _listener.Stop(); } }
                catch { }
                _listener = null;
                return false;
            }
        }

        private async Task AcceptLoopAsync(string socksHost, int socksPort, CancellationToken token)
        {
            TcpListener listener = _listener;
            while (!token.IsCancellationRequested)
            {
                TcpClient client = null;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }
                    Warn("[PROXY] accept 失败: " + ex.Message);
                    continue;
                }

                TcpClient accepted = client;
                Task connection = Task.Run(async delegate
                {
                    await HandleClientAsync(accepted, socksHost, socksPort, token).ConfigureAwait(false);
                });
            }
        }

        private async Task HandleClientAsync(TcpClient client, string socksHost, int socksPort, CancellationToken token)
        {
            TcpClient upstream = null;
            try
            {
                using (client)
                {
                    client.NoDelay = true;
                    NetworkStream clientStream = client.GetStream();

                    byte[] head = await ReadHeadersAsync(clientStream, token).ConfigureAwait(false);
                    if (head == null || head.Length == 0)
                    {
                        return;
                    }

                    string text = Encoding.ASCII.GetString(head);
                    int lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
                    string requestLine = lineEnd > 0 ? text.Substring(0, lineEnd) : text;
                    string rest = lineEnd > 0 ? text.Substring(lineEnd + 2) : string.Empty;

                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 2)
                    {
                        await WriteAsciiAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n").ConfigureAwait(false);
                        return;
                    }

                    string method = parts[0];
                    string target = parts[1];
                    bool isConnect = string.Equals(method, "CONNECT", StringComparison.OrdinalIgnoreCase);

                    string targetHost;
                    int targetPort;
                    if (isConnect)
                    {
                        if (!TryParseHostPort(target, 443, out targetHost, out targetPort))
                        {
                            await WriteAsciiAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n").ConfigureAwait(false);
                            return;
                        }
                    }
                    else
                    {
                        Uri uri;
                        if (!Uri.TryCreate(target, UriKind.Absolute, out uri))
                        {
                            await WriteAsciiAsync(clientStream, "HTTP/1.1 400 Bad Request\r\n\r\n").ConfigureAwait(false);
                            return;
                        }
                        targetHost = uri.Host;
                        targetPort = uri.Port;
                    }

                    upstream = await Socks5ConnectAsync(socksHost, socksPort, targetHost, targetPort, token)
                        .ConfigureAwait(false);
                    if (upstream == null)
                    {
                        await WriteAsciiAsync(clientStream, "HTTP/1.1 502 Bad Gateway\r\n\r\n").ConfigureAwait(false);
                        return;
                    }
                    upstream.NoDelay = true;
                    NetworkStream upstreamStream = upstream.GetStream();

                    if (isConnect)
                    {
                        await WriteAsciiAsync(clientStream, "HTTP/1.1 200 Connection Established\r\n\r\n")
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        string forwarded = method + " " + OriginForm(target) + " "
                            + (parts.Length > 2 ? parts[2] : "HTTP/1.1") + "\r\n"
                            + StripProxyHeaders(rest);
                        byte[] payload = Encoding.ASCII.GetBytes(forwarded);
                        await upstreamStream.WriteAsync(payload, 0, payload.Length, token).ConfigureAwait(false);
                    }

                    await RelayAsync(clientStream, upstreamStream, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Info("[PROXY] 连接处理结束（" + ex.GetType().Name + ": " + ex.Message + "）");
            }
            finally
            {
                try { if (upstream != null) { upstream.Close(); } }
                catch { }
            }
        }

        /// <summary>读到 \r\n\r\n 为止（含头部），超过上限或超时返回 null。</summary>
        private static async Task<byte[]> ReadHeadersAsync(NetworkStream stream, CancellationToken token)
        {
            var buffer = new byte[MaxHeaderBytes];
            int filled = 0;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(HeaderTimeoutMs);

            while (filled < buffer.Length)
            {
                if (DateTime.UtcNow > deadline)
                {
                    return null;
                }

                Task<int> readTask = stream.ReadAsync(buffer, filled, Math.Min(4096, buffer.Length - filled), token);
                Task finished = await Task.WhenAny(readTask, Task.Delay(2000, token)).ConfigureAwait(false);
                if (finished != readTask)
                {
                    continue;
                }
                int read = await readTask.ConfigureAwait(false);
                if (read <= 0)
                {
                    return filled > 0 ? Slice(buffer, filled) : null;
                }
                filled += read;
                int end = IndexOfHeaderEnd(buffer, filled);
                if (end >= 0)
                {
                    return Slice(buffer, end + 4);
                }
            }
            return null;
        }

        private static int IndexOfHeaderEnd(byte[] buffer, int length)
        {
            for (int i = 3; i < length; i++)
            {
                if (buffer[i - 3] == 13 && buffer[i - 2] == 10 && buffer[i - 1] == 13 && buffer[i] == 10)
                {
                    return i - 3;
                }
            }
            return -1;
        }

        private static byte[] Slice(byte[] source, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, 0, result, 0, length);
            return result;
        }

        private static string StripProxyHeaders(string headerBlock)
        {
            var sb = new StringBuilder();
            string[] lines = headerBlock.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                sb.Append(lines[i]).Append("\r\n");
            }
            // 头块必须以空行结束，否则源站会一直等后续头（实测：漏了这行 → curl 收到空响应）。
            sb.Append("\r\n");
            return sb.ToString();
        }

        private static string OriginForm(string absoluteUri)
        {
            try
            {
                var uri = new Uri(absoluteUri);
                string path = string.IsNullOrEmpty(uri.PathAndQuery) ? "/" : uri.PathAndQuery;
                return path;
            }
            catch
            {
                return "/";
            }
        }

        private static bool TryParseHostPort(string value, int defaultPort, out string host, out int port)
        {
            host = null;
            port = defaultPort;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            int colon = value.LastIndexOf(':');
            if (colon <= 0 || colon == value.Length - 1)
            {
                host = value;
                return true;
            }
            host = value.Substring(0, colon).Trim('[', ']');
            return int.TryParse(value.Substring(colon + 1), out port) && port > 0 && port < 65536;
        }

        private async Task<TcpClient> Socks5ConnectAsync(
            string socksHost, int socksPort, string targetHost, int targetPort, CancellationToken token)
        {
            var client = new TcpClient();
            try
            {
                client.NoDelay = true;
                await client.ConnectAsync(socksHost, socksPort).ConfigureAwait(false);
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 20000;
                stream.WriteTimeout = 20000;

                await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, 0, 3, token).ConfigureAwait(false);
                var greeting = new byte[2];
                if (await ReadExactAsync(stream, greeting, 2, token).ConfigureAwait(false) != 2 || greeting[0] != 0x05)
                {
                    client.Close();
                    return null;
                }

                byte[] hostBytes = Encoding.ASCII.GetBytes(targetHost ?? string.Empty);
                var request = new byte[4 + 1 + hostBytes.Length + 2];
                request[0] = 0x05;
                request[1] = 0x01;
                request[2] = 0x00;
                request[3] = 0x03;
                request[4] = (byte)hostBytes.Length;
                Buffer.BlockCopy(hostBytes, 0, request, 5, hostBytes.Length);
                int offset = 5 + hostBytes.Length;
                request[offset] = (byte)(targetPort >> 8);
                request[offset + 1] = (byte)(targetPort & 0xFF);
                await stream.WriteAsync(request, 0, request.Length, token).ConfigureAwait(false);

                var replyHead = new byte[4];
                if (await ReadExactAsync(stream, replyHead, 4, token).ConfigureAwait(false) != 4
                    || replyHead[0] != 0x05 || replyHead[1] != 0x00)
                {
                    Warn("[PROXY] SOCKS5 连接 " + targetHost + ":" + targetPort + " 被拒绝（rep="
                        + (replyHead.Length > 1 ? replyHead[1] : 255) + "）");
                    client.Close();
                    return null;
                }

                int addrLen;
                if (replyHead[3] == 0x01)
                {
                    addrLen = 4;
                }
                else if (replyHead[3] == 0x04)
                {
                    addrLen = 16;
                }
                else if (replyHead[3] == 0x03)
                {
                    var lenByte = new byte[1];
                    await ReadExactAsync(stream, lenByte, 1, token).ConfigureAwait(false);
                    addrLen = lenByte[0];
                }
                else
                {
                    client.Close();
                    return null;
                }

                var drain = new byte[addrLen + 2];
                await ReadExactAsync(stream, drain, drain.Length, token).ConfigureAwait(false);

                // 握手完成后取消超时：长下载可能远超握手窗口。
                stream.ReadTimeout = Timeout.Infinite;
                stream.WriteTimeout = Timeout.Infinite;
                return client;
            }
            catch (Exception ex)
            {
                Info("[PROXY] SOCKS5 握手失败（" + targetHost + ":" + targetPort + "）: " + ex.Message);
                try { client.Close(); }
                catch { }
                return null;
            }
        }

        private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int filled = 0;
            while (filled < count)
            {
                int read = await stream.ReadAsync(buffer, filled, count - filled, token).ConfigureAwait(false);
                if (read <= 0)
                {
                    return filled;
                }
                filled += read;
            }
            return filled;
        }

        private static async Task RelayAsync(NetworkStream client, NetworkStream upstream, CancellationToken token)
        {
            Task up = PumpAsync(client, upstream, token);
            Task down = PumpAsync(upstream, client, token);
            await Task.WhenAny(up, down).ConfigureAwait(false);

            try { client.Close(); }
            catch { }
            try { upstream.Close(); }
            catch { }
        }

        private static async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken token)
        {
            var buffer = new byte[64 * 1024];
            try
            {
                while (true)
                {
                    int read = await from.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        return;
                    }
                    await to.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                }
            }
            catch
            {
                // 一端关闭视为正常结束。
            }
        }

        private static async Task WriteAsciiAsync(NetworkStream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            try
            {
                await stream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            catch { }
        }

        public void Stop()
        {
            _stopped = true;
            try { if (_cts != null) { _cts.Cancel(); } }
            catch { }
            try { if (_listener != null) { _listener.Stop(); } }
            catch { }
            lock (_sync)
            {
                _listener = null;
            }
            try { if (_acceptTask != null) { _acceptTask.Wait(1500); } }
            catch { }
        }

        public void Dispose()
        {
            Stop();
        }

        private void Info(string message)
        {
            if (_logger != null) { _logger.Info(message); }
        }

        private void Warn(string message)
        {
            if (_logger != null) { _logger.Warn(message); }
        }
    }
}
