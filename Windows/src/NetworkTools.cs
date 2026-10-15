using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YunTiPro
{
    /// <summary>
    /// 网络工具：TCP 延迟测试 + 基于本地 SOCKS5 的远端连通性验证。
    /// </summary>
    public static class NetworkTools
    {
        /// <summary>
        /// TCP 连接耗时测试，超时返回 -1。dokodemo-door 节点返回 -2 表示不适用。
        /// </summary>
        public static async Task<int> TcpLatencyAsync(string host, int port, int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0) return -1;
            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(host, port);
                    var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false);
                    if (completed != connectTask) return -1; // 超时
                    await connectTask; // 抛出连接异常
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>对节点做 TCP 测速；dokodemo-door 不适用</summary>
        public static Task<int> TcpLatencyAsync(NodeConfig node, int timeoutMs = 3000)
        {
            if (node.Protocol == "dokodemo-door") return Task.FromResult(-2);
            return TcpLatencyAsync(node.Address, node.Port, timeoutMs);
        }

        /// <summary>
        /// 批量测速，最多 maxConcurrency 并发，逐项回调结果。
        /// </summary>
        public static async Task TestAllAsync(IList<NodeConfig> nodes, Action<NodeConfig, int> onResult, int maxConcurrency = 4, int timeoutMs = 3000, CancellationToken ct = default(CancellationToken))
        {
            using (var sem = new SemaphoreSlim(maxConcurrency))
            {
                var tasks = new List<Task>();
                foreach (var node in nodes)
                {
                    await sem.WaitAsync(ct).ConfigureAwait(false);
                    var n = node;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            int ms = await TcpLatencyAsync(n, timeoutMs).ConfigureAwait(false);
                            if (onResult != null) onResult(n, ms);
                        }
                        finally { sem.Release(); }
                    }, ct));
                }
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 通过本地 SOCKS5(带用户名密码认证) 访问 cp.cloudflare.com/generate_204。
        /// 返回远端验证耗时毫秒；失败返回 -1。
        /// </summary>
        public static async Task<int> VerifyRemoteViaSocksAsync(int socksPort, string user, string pass, int timeoutMs = 8000)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync("127.0.0.1", socksPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != connectTask) return -1;
                    await connectTask;

                    var stream = client.GetStream();
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;

                    // ---- SOCKS5 握手：声明支持 无认证 与 用户名密码 ----
                    byte[] greeting = { 0x05, 0x02, 0x00, 0x02 };
                    await stream.WriteAsync(greeting, 0, greeting.Length).ConfigureAwait(false);
                    byte[] resp = await ReadExactAsync(stream, 2, timeoutMs).ConfigureAwait(false);
                    if (resp == null || resp[0] != 0x05) return -1;

                    if (resp[1] == 0x02)
                    {
                        // 用户名密码认证 (RFC 1929)
                        byte[] u = Encoding.UTF8.GetBytes(user);
                        byte[] p = Encoding.UTF8.GetBytes(pass);
                        var auth = new List<byte> { 0x01, (byte)u.Length };
                        auth.AddRange(u);
                        auth.Add((byte)p.Length);
                        auth.AddRange(p);
                        await stream.WriteAsync(auth.ToArray(), 0, auth.Count).ConfigureAwait(false);
                        byte[] authResp = await ReadExactAsync(stream, 2, timeoutMs).ConfigureAwait(false);
                        if (authResp == null || authResp[1] != 0x00) return -1; // 认证失败
                    }
                    else if (resp[1] != 0x00)
                    {
                        return -1; // 服务端拒绝所有认证方式
                    }

                    // ---- CONNECT cp.cloudflare.com:80 ----
                    byte[] domain = Encoding.ASCII.GetBytes("cp.cloudflare.com");
                    var req = new List<byte> { 0x05, 0x01, 0x00, 0x03, (byte)domain.Length };
                    req.AddRange(domain);
                    req.Add(0x00); req.Add(0x50); // 端口 80
                    await stream.WriteAsync(req.ToArray(), 0, req.Count).ConfigureAwait(false);

                    byte[] head = await ReadExactAsync(stream, 4, timeoutMs).ConfigureAwait(false);
                    if (head == null || head[1] != 0x00) return -1; // 连接目标失败
                    int atyp = head[3];
                    int skip = atyp == 0x01 ? 4 : (atyp == 0x03 ? (await ReadExactAsync(stream, 1, timeoutMs))[0] : 16);
                    await ReadExactAsync(stream, skip + 2, timeoutMs).ConfigureAwait(false); // 地址 + 端口

                    // ---- HTTP 请求 ----
                    string httpReq = "GET /generate_204 HTTP/1.1\r\nHost: cp.cloudflare.com\r\nConnection: close\r\nUser-Agent: YunTiPro/1.0\r\n\r\n";
                    byte[] httpBytes = Encoding.ASCII.GetBytes(httpReq);
                    await stream.WriteAsync(httpBytes, 0, httpBytes.Length).ConfigureAwait(false);

                    // 读取响应状态行
                    string statusLine = await ReadLineAsync(stream, timeoutMs).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(statusLine)) return -1;
                    if (!statusLine.Contains(" 204")) return -1;

                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 直连远端验证：TCP 直连 cp.cloudflare.com:80 并请求 /generate_204。
        /// 用于 TUN 模式——由独立探测子进程调用（其路径被路由到 proxy），
        /// 因此“直连”流量实际经过隧道，返回 -1 表示隧道不通。
        /// </summary>
        public static async Task<int> VerifyRemoteDirectAsync(int timeoutMs = 8000)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync("cp.cloudflare.com", 80);
                    if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != connectTask) return -1;
                    await connectTask;

                    var stream = client.GetStream();
                    string httpReq = "GET /generate_204 HTTP/1.1\r\nHost: cp.cloudflare.com\r\nConnection: close\r\nUser-Agent: YunTiPro/1.0\r\n\r\n";
                    byte[] httpBytes = Encoding.ASCII.GetBytes(httpReq);
                    await stream.WriteAsync(httpBytes, 0, httpBytes.Length).ConfigureAwait(false);

                    string statusLine = await ReadLineAsync(stream, timeoutMs).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(statusLine)) return -1;
                    if (!statusLine.Contains(" 204")) return -1;

                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch
            {
                return -1;
            }
        }

        private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int count, int timeoutMs)
        {
            var buf = new byte[count];
            int got = 0;
            while (got < count)
            {
                var readTask = stream.ReadAsync(buf, got, count - got);
                if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != readTask)
                    return null;
                int n = await readTask.ConfigureAwait(false);
                if (n <= 0) return null;
                got += n;
            }
            return buf;
        }

        private static async Task<string> ReadLineAsync(NetworkStream stream, int timeoutMs)
        {
            var sb = new StringBuilder();
            var one = new byte[1];
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline && sb.Length < 512)
            {
                var readTask = stream.ReadAsync(one, 0, 1);
                if (await Task.WhenAny(readTask, Task.Delay(timeoutMs)).ConfigureAwait(false) != readTask) break;
                int n = await readTask.ConfigureAwait(false);
                if (n <= 0) break;
                char c = (char)one[0];
                if (c == '\n') break;
                if (c != '\r') sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
