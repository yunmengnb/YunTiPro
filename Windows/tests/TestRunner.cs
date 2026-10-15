// 控制台测试程序：引用与主程序相同的源码，断言核心逻辑正确性。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using YunTiPro;

namespace YunTiPro.Tests
{
    public static class TestRunner
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("===== YunTiPro 单元测试 =====");

            // 链接解析
            TestParseVmessNumericPort();
            TestParseVmessStringPort();
            TestParseVlessTlsWs();
            TestParseVlessRealityRejected();
            TestParseVlessFlowRejected();
            TestParseTrojan();
            TestParseSsSip002();
            TestParseSsLegacyBase64();
            TestParseSsPluginRejected();
            TestParseSocks();
            TestParseHttp();
            TestParseEmptyNameDefault();
            TestParseBatchMixed();
            TestParseInvalidProtocol();

            // 配置生成
            TestBuildVmessConfig();
            TestBuildTlsConfigHasVerify();
            TestBuildDokodemoRejected();
            TestBuildSsConfig();

            // sing-box 配置生成（六协议）
            TestSingBoxVmess();
            TestSingBoxVless();
            TestSingBoxTrojan();
            TestSingBoxSs();
            TestSingBoxSocks();
            TestSingBoxHttp();
            // sing-box TUN 与分流规则
            TestSingBoxTunInbound();
            TestSingBoxWhitelistRules();
            TestSingBoxBlacklistRules();
            TestSingBoxAntiLoopback();
            TestSingBoxEmptyWhitelistDegenerate();
            TestSingBoxProbeRoutedToProxy();

            // 分享导出回环（导出 → 解析 → 字段一致）
            TestExportVmessRoundTrip();
            TestExportVlessGrpcRoundTrip();
            TestExportTrojanWsRoundTrip();
            TestExportSsRoundTrip();
            TestExportSocksAuthRoundTrip();
            TestExportHttpNoAuthRoundTrip();
            TestExportSpecialCharsRoundTrip();

            // 二维码生成-识别回环（导出 → QRCoder 生成 → ZXing 识别 → 解析 → 字段一致）
            TestQrRoundTripVmess();
            TestQrRoundTripTrojan();
            TestQrRoundTripSs();
            TestQrDownscaleIfNeeded();

            // 设置读写与兼容
            TestSettingsBackwardCompat();
            TestSettingsRoundTrip();

            // 加密存储
            TestDpapiRoundTrip();
            TestStoreSaveLoad();

            // TCP 测速
            TestTcpLatencyLocalListener().Wait();
            TestTcpLatencyClosedPort().Wait();
            TestTcpLatencyDokodemoNotApplicable().Wait();
            TestConcurrencyLimit().Wait();

            Console.WriteLine();
            Console.WriteLine("===== 结果: 通过 {0} / 失败 {1} =====", _passed, _failed);
            return _failed > 0 ? 1 : 0;
        }

        private static void Assert(bool cond, string name, string detail = "")
        {
            if (cond) { _passed++; Console.WriteLine("[PASS] " + name); }
            else { _failed++; Console.WriteLine("[FAIL] " + name + (detail.Length > 0 ? "  -- " + detail : "")); }
        }

        // ---------- 解析 ----------

        private static string MakeVmessLink(string portJsonValue)
        {
            string json = "{\"ps\":\"测试节点\",\"add\":\"example.com\",\"port\":" + portJsonValue +
                ",\"id\":\"b831381d-6324-4d53-ad4f-8cda48b30811\",\"aid\":\"0\",\"net\":\"ws\",\"path\":\"/ray\",\"host\":\"cdn.example.com\",\"tls\":\"tls\",\"sni\":\"sni.example.com\"}";
            return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        private static void TestParseVmessNumericPort()
        {
            var node = ShareLinkParser.ParseSingle(MakeVmessLink("443"), 1);
            Assert(node.Protocol == "vmess" && node.Port == 443 && node.Name == "测试节点"
                && node.Uuid == "b831381d-6324-4d53-ad4f-8cda48b30811"
                && node.Network == "ws" && node.Tls == "tls" && node.WsPath == "/ray"
                && node.WsHost == "cdn.example.com" && node.Sni == "sni.example.com",
                "vmess 数字端口解析");
        }

        private static void TestParseVmessStringPort()
        {
            var node = ShareLinkParser.ParseSingle(MakeVmessLink("\"8443\""), 1);
            Assert(node.Port == 8443, "vmess 字符串端口解析", "port=" + node.Port);
        }

        private static void TestParseVlessTlsWs()
        {
            var node = ShareLinkParser.ParseSingle(
                "vless://b831381d-6324-4d53-ad4f-8cda48b30811@host.com:443?security=tls&type=ws&path=%2Fapi&host=h.com&sni=s.com#%E6%88%91%E7%9A%84%E8%8A%82%E7%82%B9", 1);
            Assert(node.Protocol == "vless" && node.Address == "host.com" && node.Port == 443
                && node.Tls == "tls" && node.Network == "ws" && node.WsPath == "/api"
                && node.Name == "我的节点" && node.Sni == "s.com",
                "vless TLS+WS 解析");
        }

        private static void TestParseVlessRealityRejected()
        {
            try
            {
                ShareLinkParser.ParseSingle("vless://b831381d-6324-4d53-ad4f-8cda48b30811@host.com:443?security=reality&pbk=xxx#r", 1);
                Assert(false, "vless Reality 拒绝");
            }
            catch (NotSupportedException) { Assert(true, "vless Reality 拒绝"); }
        }

        private static void TestParseVlessFlowRejected()
        {
            try
            {
                ShareLinkParser.ParseSingle("vless://b831381d-6324-4d53-ad4f-8cda48b30811@host.com:443?security=tls&flow=xtls-rprx-vision#r", 1);
                Assert(false, "vless flow 拒绝");
            }
            catch (NotSupportedException) { Assert(true, "vless flow 拒绝"); }
        }

        private static void TestParseTrojan()
        {
            var node = ShareLinkParser.ParseSingle("trojan://p%40ssw0rd@tj.example.com:8443?sni=a.com#trojan1", 1);
            Assert(node.Protocol == "trojan" && node.Password == "p@ssw0rd"
                && node.Address == "tj.example.com" && node.Port == 8443
                && node.Tls == "tls" && node.Sni == "a.com" && node.Name == "trojan1",
                "trojan 解析");
        }

        private static void TestParseSsSip002()
        {
            // ss://base64(method:password)@host:port#name
            string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:myPassword"));
            var node = ShareLinkParser.ParseSingle("ss://" + cred + "@ss.example.com:8388#SS%E8%8A%82%E7%82%B9", 1);
            Assert(node.Protocol == "shadowsocks" && node.Method == "aes-256-gcm"
                && node.Password == "myPassword" && node.Address == "ss.example.com"
                && node.Port == 8388 && node.Name == "SS节点",
                "ss SIP002 解析");
        }

        private static void TestParseSsLegacyBase64()
        {
            // ss://base64(method:password@host:port)#name 整体编码
            string all = Convert.ToBase64String(Encoding.UTF8.GetBytes("chacha20-ietf-poly1305:pw123@1.2.3.4:1080"));
            var node = ShareLinkParser.ParseSingle("ss://" + all + "#legacy", 1);
            Assert(node.Method == "chacha20-ietf-poly1305" && node.Password == "pw123"
                && node.Address == "1.2.3.4" && node.Port == 1080,
                "ss 整体 base64 解析");
        }

        private static void TestParseSsPluginRejected()
        {
            string cred = Convert.ToBase64String(Encoding.UTF8.GetBytes("aes-256-gcm:pw"));
            try
            {
                ShareLinkParser.ParseSingle("ss://" + cred + "@host.com:8388?plugin=obfs-local%3Bobfs%3Dhttp#p", 1);
                Assert(false, "ss plugin 拒绝");
            }
            catch (NotSupportedException) { Assert(true, "ss plugin 拒绝"); }
        }

        private static void TestParseSocks()
        {
            var node = ShareLinkParser.ParseSingle("socks://user:pass123@10.0.0.5:1080#mysocks", 1);
            Assert(node.Protocol == "socks" && node.Username == "user" && node.Password == "pass123"
                && node.Address == "10.0.0.5" && node.Port == 1080,
                "socks 解析");
        }

        private static void TestParseHttp()
        {
            var node = ShareLinkParser.ParseSingle("http://user:pw@proxy.corp.com:8080", 1);
            Assert(node.Protocol == "http" && node.Port == 8080 && node.Username == "user",
                "http 解析");
        }

        private static void TestParseEmptyNameDefault()
        {
            var node = ShareLinkParser.ParseSingle("trojan://pw@noname.com:443", 7);
            Assert(node.Name == "trojan-7", "空名称自动补默认", "name=" + node.Name);
        }

        private static void TestParseBatchMixed()
        {
            var text = "trojan://pw@a.com:443#ok1\n" +
                       "not-a-link\n" +
                       "vless://b831381d-6324-4d53-ad4f-8cda48b30811@b.com:443?security=reality#bad\n" +
                       "http://c.com:8080\n";
            var result = ShareLinkParser.ParseBatch(text);
            Assert(result.Nodes.Count == 2 && result.Errors.Count == 2,
                "批量导入混合解析", "nodes=" + result.Nodes.Count + " errors=" + result.Errors.Count);
        }

        private static void TestParseInvalidProtocol()
        {
            var result = ShareLinkParser.ParseBatch("hy2://abc@x.com:443");
            Assert(result.Nodes.Count == 0 && result.Errors.Count == 1, "未知协议拒绝");
        }

        // ---------- 分享导出回环 ----------

        private static NodeConfig RoundTrip(NodeConfig n)
        {
            string link = ShareLinkExporter.Export(n);
            return ShareLinkParser.ParseSingle(link, 1);
        }

        private static void TestExportVmessRoundTrip()
        {
            var n = SampleVmess();
            n.Name = "香港 01"; n.Sni = "sni.example.com";
            var m = RoundTrip(n);
            Assert(m.Protocol == "vmess" && m.Name == "香港 01" && m.Address == n.Address && m.Port == n.Port
                && m.Uuid == n.Uuid && m.Network == n.Network && m.Tls == n.Tls
                && m.WsPath == n.WsPath && m.WsHost == n.WsHost && m.Sni == "sni.example.com",
                "vmess 导出回环", m.Name + "|" + m.Network + "|" + m.Tls);
        }

        private static void TestExportVlessGrpcRoundTrip()
        {
            var n = new NodeConfig { Name = "V", Protocol = "vless", Address = "b.com", Port = 443,
                Uuid = "b831381d-6324-4d53-ad4f-8cda48b30811", Network = "grpc", Tls = "tls", WsPath = "svc", Sni = "s.b.com" };
            var m = RoundTrip(n);
            Assert(m.Protocol == "vless" && m.Uuid == n.Uuid && m.Network == "grpc"
                && m.WsPath == "svc" && m.Sni == "s.b.com" && m.Tls == "tls",
                "vless grpc 导出回环", m.WsPath + "|" + m.Sni);
        }

        private static void TestExportTrojanWsRoundTrip()
        {
            var n = new NodeConfig { Name = "T", Protocol = "trojan", Address = "t.com", Port = 443,
                Password = "p@ss:w+rd", Network = "ws", Tls = "tls", WsPath = "/t", WsHost = "h.com" };
            var m = RoundTrip(n);
            Assert(m.Protocol == "trojan" && m.Password == "p@ss:w+rd" && m.Network == "ws"
                && m.WsPath == "/t" && m.WsHost == "h.com" && m.Tls == "tls",
                "trojan ws 导出回环", m.Password + "|" + m.WsPath);
        }

        private static void TestExportSsRoundTrip()
        {
            var n = new NodeConfig { Name = "SS", Protocol = "shadowsocks", Address = "s.com", Port = 8388,
                Method = "chacha20-ietf-poly1305", Password = "pass+word/1", Network = "tcp", Tls = "none" };
            var m = RoundTrip(n);
            Assert(m.Protocol == "shadowsocks" && m.Method == n.Method && m.Password == n.Password && m.Port == 8388,
                "ss 导出回环", m.Method + "|" + m.Password);
        }

        private static void TestExportSocksAuthRoundTrip()
        {
            var n = new NodeConfig { Name = "S", Protocol = "socks", Address = "10.0.0.1", Port = 1080,
                Username = "user", Password = "p@ss", Network = "tcp", Tls = "none" };
            var m = RoundTrip(n);
            Assert(m.Protocol == "socks" && m.Username == "user" && m.Password == "p@ss" && m.Port == 1080,
                "socks 带认证导出回环", m.Username + "|" + m.Password);
        }

        private static void TestExportHttpNoAuthRoundTrip()
        {
            var n = new NodeConfig { Name = "H", Protocol = "http", Address = "10.0.0.2", Port = 8080, Network = "tcp", Tls = "none" };
            var m = RoundTrip(n);
            Assert(m.Protocol == "http" && string.IsNullOrEmpty(m.Username) && m.Port == 8080,
                "http 无认证导出回环", m.Username);
        }

        private static void TestExportSpecialCharsRoundTrip()
        {
            var n = new NodeConfig { Name = "节点 A+B", Protocol = "trojan", Address = "t.com", Port = 443,
                Password = "x", Network = "tcp", Tls = "tls" };
            var m = RoundTrip(n);
            Assert(m.Name == "节点 A+B", "名称特殊字符回环", m.Name);
        }

        // ---------- 二维码生成-识别回环 ----------

        /// <summary>导出链接 → 生成二维码位图 → ZXing 识别 → 解析，断言关键字段一致</summary>
        private static void AssertQrRoundTrip(NodeConfig n, string name)
        {
            string link = ShareLinkExporter.Export(n);
            using (var bmp = QrCodeHelper.GenerateQrBitmap(link))
            {
                string decoded = QrCodeHelper.DecodeFromImage(bmp);
                if (decoded != link)
                {
                    Assert(false, name, "识别文本与导出链接不一致: " + (decoded ?? "null"));
                    return;
                }
                var m = ShareLinkParser.ParseSingle(decoded, 1);
                Assert(m.Protocol == n.Protocol && m.Name == n.Name && m.Address == n.Address && m.Port == n.Port
                    && m.Uuid == n.Uuid && m.Password == n.Password && m.Method == n.Method
                    && m.Network == n.Network && m.Tls == n.Tls
                    && m.WsPath == n.WsPath && m.WsHost == n.WsHost && m.Sni == n.Sni,
                    name, m.Protocol + "|" + m.Name + "|" + m.Address + ":" + m.Port);
            }
        }

        private static void TestQrRoundTripVmess()
        {
            var n = SampleVmess();
            n.Name = "二维码香港 01";
            n.Network = "ws"; n.Tls = "tls";
            n.WsPath = "/ray"; n.WsHost = "cdn.example.com"; n.Sni = "sni.example.com";
            AssertQrRoundTrip(n, "vmess 二维码回环");
        }

        private static void TestQrRoundTripTrojan()
        {
            var n = new NodeConfig { Name = "二维码T", Protocol = "trojan", Address = "tj.example.com", Port = 8443,
                Password = "p@ss:w+rd", Network = "ws", Tls = "tls", WsPath = "/t", WsHost = "h.com", Sni = "" };
            AssertQrRoundTrip(n, "trojan 二维码回环");
        }

        private static void TestQrRoundTripSs()
        {
            var n = new NodeConfig { Name = "二维码SS", Protocol = "shadowsocks", Address = "ss.example.com", Port = 8388,
                Method = "chacha20-ietf-poly1305", Password = "pass+word/1", Network = "tcp", Tls = "none", WsPath = "/" };
            AssertQrRoundTrip(n, "ss 二维码回环");
        }

        private static void TestQrDownscaleIfNeeded()
        {
            // 超大图等比压缩到最长边 2000
            using (var src = new Bitmap(3000, 1200))
            using (var dst = QrCodeHelper.DownscaleIfNeeded(src, QrCodeHelper.MaxDecodeEdge))
            {
                Assert(!ReferenceEquals(src, dst) && dst.Width == 2000 && dst.Height == 800,
                    "超大图片等比压缩", dst.Width + "x" + dst.Height);
            }
            // 小图不压缩，返回原引用
            using (var small = new Bitmap(100, 100))
            {
                Assert(ReferenceEquals(small, QrCodeHelper.DownscaleIfNeeded(small, QrCodeHelper.MaxDecodeEdge)),
                    "小图不压缩返回原引用");
            }
            // 放大到超限的二维码经压缩后仍可识别
            string link = ShareLinkExporter.Export(SampleVmess());
            using (var qr = QrCodeHelper.GenerateQrBitmap(link))
            using (var big = new Bitmap(qr, new Size(2400, 2400)))
            {
                string decoded = QrCodeHelper.DecodeFromImage(big);
                Assert(decoded == link, "超限二维码压缩后仍可识别", decoded == null ? "null" : "len=" + decoded.Length);
            }
        }

        // ---------- 配置生成 ----------

        private static NodeConfig SampleVmess()
        {
            return new NodeConfig
            {
                Name = "vm",
                Protocol = "vmess",
                Address = "example.com",
                Port = 443,
                Uuid = "b831381d-6324-4d53-ad4f-8cda48b30811",
                Network = "tcp",
                Tls = "none"
            };
        }

        private static void TestBuildVmessConfig()
        {
            var json = XrayConfigBuilder.Build(SampleVmess(), 23456, 23457, "u_abc", "p_xyz");
            Assert(json.Contains("\"protocol\":\"vmess\"") &&
                   json.Contains("\"address\":\"example.com\"") &&
                   json.Contains("\"port\":443") &&
                   json.Contains("\"listen\":\"127.0.0.1\"") &&
                   json.Contains("\"port\":23456") &&
                   json.Contains("\"user\":\"u_abc\"") &&
                   json.Contains("\"auth\":\"password\""),
                   "vmess 配置生成");
        }

        private static void TestBuildTlsConfigHasVerify()
        {
            var n = SampleVmess();
            n.Tls = "tls"; n.Sni = "sni.example.com";
            var json = XrayConfigBuilder.Build(n, 23456, 23457, "u", "p");
            Assert(json.Contains("\"security\":\"tls\"") &&
                   json.Contains("\"allowInsecure\":false") &&
                   json.Contains("\"serverName\":\"sni.example.com\""),
                   "TLS 配置强制证书校验");
        }

        private static void TestBuildDokodemoRejected()
        {
            var n = new NodeConfig { Protocol = "dokodemo-door", Name = "d", TargetAddress = "127.0.0.1", TargetPort = 80 };
            try
            {
                XrayConfigBuilder.Build(n, 23456, 23457, "u", "p");
                Assert(false, "dokodemo-door 不能作出口");
            }
            catch (InvalidOperationException) { Assert(true, "dokodemo-door 不能作出口"); }
        }

        private static void TestBuildSsConfig()
        {
            var n = new NodeConfig { Name = "ss", Protocol = "shadowsocks", Address = "s.com", Port = 8388, Method = "aes-256-gcm", Password = "pw" };
            var json = XrayConfigBuilder.Build(n, 23456, 23457, "u", "p");
            Assert(json.Contains("\"protocol\":\"shadowsocks\"") &&
                   json.Contains("\"method\":\"aes-256-gcm\""),
                   "shadowsocks 配置生成");
        }

        // ---------- sing-box 配置生成 ----------

        private static string BuildSingBox(NodeConfig node, string mode, string[] filterPaths, string probe = null)
        {
            var direct = new List<string> { @"C:\app\YunTiPro.exe", @"C:\app\core\sing-box\sing-box.exe" };
            return SingBoxConfigBuilder.Build(node, mode, filterPaths, direct, probe);
        }

        /// <summary>规则方向断言：path 所在 process_path 规则必须指向 expectedOutbound</summary>
        private static bool RulePointsTo(string json, string pathFragment, string expectedOutbound)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json,
                "\"process_path\":\\[[^\\]]*" + System.Text.RegularExpressions.Regex.Escape(pathFragment) + "[^\\]]*\\],\"outbound\":\"" + expectedOutbound + "\"");
            return m.Success;
        }

        private static void TestSingBoxVmess()
        {
            var json = BuildSingBox(SampleVmess(), "off", null);
            Assert(json.Contains("\"type\":\"vmess\"") &&
                   json.Contains("\"server\":\"example.com\"") &&
                   json.Contains("\"server_port\":443") &&
                   json.Contains("\"uuid\":\"b831381d-6324-4d53-ad4f-8cda48b30811\"") &&
                   json.Contains("\"security\":\"auto\"") &&
                   json.Contains("\"alter_id\":0"),
                   "sing-box vmess 出站字段");
        }

        private static void TestSingBoxVless()
        {
            var n = SampleVmess(); n.Protocol = "vless"; n.Tls = "tls"; n.Sni = "sni.example.com"; n.Network = "ws"; n.WsPath = "/ray"; n.WsHost = "cdn.example.com";
            var json = BuildSingBox(n, "off", null);
            Assert(json.Contains("\"type\":\"vless\"") &&
                   json.Contains("\"uuid\":\"b831381d-6324-4d53-ad4f-8cda48b30811\"") &&
                   json.Contains("\"tls\":{\"enabled\":true,\"server_name\":\"sni.example.com\",\"insecure\":false}") &&
                   json.Contains("\"transport\":{\"type\":\"ws\",\"path\":\"/ray\"") &&
                   json.Contains("\"Host\":\"cdn.example.com\""),
                   "sing-box vless TLS+WS 出站字段");
        }

        private static void TestSingBoxTrojan()
        {
            var n = new NodeConfig { Name = "tj", Protocol = "trojan", Address = "tj.example.com", Port = 8443, Password = "pw123", Tls = "tls" };
            var json = BuildSingBox(n, "off", null);
            Assert(json.Contains("\"type\":\"trojan\"") &&
                   json.Contains("\"password\":\"pw123\"") &&
                   json.Contains("\"enabled\":true") &&
                   json.Contains("\"insecure\":false"),
                   "sing-box trojan 出站字段");
        }

        private static void TestSingBoxSs()
        {
            var n = new NodeConfig { Name = "ss", Protocol = "shadowsocks", Address = "s.com", Port = 8388, Method = "aes-256-gcm", Password = "pw" };
            var json = BuildSingBox(n, "off", null);
            Assert(json.Contains("\"type\":\"shadowsocks\"") &&
                   json.Contains("\"method\":\"aes-256-gcm\"") &&
                   json.Contains("\"server_port\":8388"),
                   "sing-box shadowsocks 出站字段");
        }

        private static void TestSingBoxSocks()
        {
            var n = new NodeConfig { Name = "sk", Protocol = "socks", Address = "10.0.0.5", Port = 1080, Username = "u1", Password = "p1" };
            var json = BuildSingBox(n, "off", null);
            Assert(json.Contains("\"type\":\"socks\"") &&
                   json.Contains("\"version\":\"5\"") &&
                   json.Contains("\"username\":\"u1\"") &&
                   json.Contains("\"password\":\"p1\""),
                   "sing-box socks 出站字段");
        }

        private static void TestSingBoxHttp()
        {
            var n = new NodeConfig { Name = "hp", Protocol = "http", Address = "proxy.corp.com", Port = 8080, Username = "u1", Password = "p1" };
            var json = BuildSingBox(n, "off", null);
            Assert(json.Contains("\"type\":\"http\"") &&
                   json.Contains("\"server\":\"proxy.corp.com\"") &&
                   json.Contains("\"username\":\"u1\""),
                   "sing-box http 出站字段");
        }

        private static void TestSingBoxTunInbound()
        {
            var json = BuildSingBox(SampleVmess(), "off", null);
            Assert(json.Contains("\"type\":\"tun\"") &&
                   json.Contains("\"inet4_address\":\"172.18.0.1/30\"") &&
                   json.Contains("\"auto_route\":true") &&
                   json.Contains("\"strict_route\":false") &&
                   json.Contains("\"stack\":\"system\"") &&
                   json.Contains("\"sniff\":true"),
                   "sing-box TUN 入站字段");
        }

        private static void TestSingBoxWhitelistRules()
        {
            var json = BuildSingBox(SampleVmess(), "whitelist", new[] { @"C:\apps\foo.exe" });
            Assert(RulePointsTo(json, "foo.exe", "proxy") && json.Contains("\"final\":\"direct\""),
                   "白名单规则方向(名单→proxy, final→direct)");
        }

        private static void TestSingBoxBlacklistRules()
        {
            var json = BuildSingBox(SampleVmess(), "blacklist", new[] { @"C:\apps\bar.exe" });
            Assert(RulePointsTo(json, "bar.exe", "direct") && json.Contains("\"final\":\"proxy\""),
                   "黑名单规则方向(名单→direct, final→proxy)");
        }

        private static void TestSingBoxAntiLoopback()
        {
            var json = BuildSingBox(SampleVmess(), "blacklist", new[] { @"C:\apps\bar.exe" });
            Assert(RulePointsTo(json, "YunTiPro.exe", "direct") && RulePointsTo(json, "sing-box.exe", "direct"),
                   "防回环规则(自身与核心→direct)");
        }

        private static void TestSingBoxEmptyWhitelistDegenerate()
        {
            var json = BuildSingBox(SampleVmess(), "whitelist", new string[0]);
            Assert(json.Contains("\"final\":\"direct\"") &&
                   SingBoxConfigBuilder.IsDegenerateWhitelist("whitelist", new string[0]),
                   "空白名单退化为 final=direct");
        }

        private static void TestSingBoxProbeRoutedToProxy()
        {
            var json = BuildSingBox(SampleVmess(), "whitelist", new[] { @"C:\apps\foo.exe" }, @"C:\probe\YunTiProProbe.exe");
            Assert(RulePointsTo(json, "YunTiProProbe.exe", "proxy"),
                   "探测子进程强制路由到 proxy(保证验证经过隧道)");
        }

        // ---------- 设置读写与兼容 ----------

        private static void TestSettingsBackwardCompat()
        {
            // 旧格式仅两行，新字段必须取默认值
            var s = new AppSettings();
            AppServices.ParseSettingsInto("AutoStart=1\nDisclaimerAccepted=1", s);
            Assert(s.AutoStart && s.DisclaimerAccepted &&
                   s.ProxyMode == "system" && s.AppFilterMode == "off" && s.AppFilterList == "",
                   "旧格式 settings 向后兼容");
        }

        private static void TestSettingsRoundTrip()
        {
            var s = new AppSettings
            {
                AutoStart = true,
                DisclaimerAccepted = true,
                ProxyMode = "tun",
                AppFilterMode = "whitelist",
                AppFilterList = @"C:\apps\a.exe;C:\apps\b.exe"
            };
            string text = AppServices.FormatSettings(s);
            var s2 = new AppSettings();
            AppServices.ParseSettingsInto(text, s2);
            Assert(s2.AutoStart && s2.ProxyMode == "tun" && s2.AppFilterMode == "whitelist" &&
                   s2.AppFilterList == @"C:\apps\a.exe;C:\apps\b.exe",
                   "新字段 settings 读写往返");
        }

        // ---------- 加密存储 ----------

        private static void TestDpapiRoundTrip()
        {
            string secret = "密码/UUID-b831381d-6324-4d53-ad4f-8cda48b30811";
            string back = CredentialStore.EncryptRoundTrip(secret);
            Assert(back == secret, "DPAPI 加密解密往返");
        }

        private static void TestStoreSaveLoad()
        {
            string dir = Path.Combine(Path.GetTempPath(), "yuntipro-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new CredentialStore(Path.Combine(dir, "nodes.dat"));
                var nodes = new List<NodeConfig> { SampleVmess() };
                nodes[0].Name = "存储测试";
                store.Save(nodes);

                // 落盘内容不应是明文（包含 UUID 即判定失败）
                string raw = File.ReadAllText(Path.Combine(dir, "nodes.dat"), Encoding.Default);
                bool plainLeak = raw.Contains("b831381d-6324-4d53-ad4f-8cda48b30811");

                var loaded = store.Load();
                Assert(loaded.Count == 1 && loaded[0].Name == "存储测试"
                    && loaded[0].Uuid == "b831381d-6324-4d53-ad4f-8cda48b30811"
                    && !plainLeak,
                    "加密存储往返(且落盘无明文)");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        // ---------- TCP 测速 ----------

        private static async Task TestTcpLatencyLocalListener()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.BeginAcceptTcpClient(ar => { try { listener.EndAcceptTcpClient(ar).Close(); } catch { } }, null);
            int ms = await NetworkTools.TcpLatencyAsync("127.0.0.1", port, 3000);
            listener.Stop();
            Assert(ms >= 0 && ms < 3000, "TCP 测速-本地监听", "ms=" + ms);
        }

        private static async Task TestTcpLatencyClosedPort()
        {
            int ms = await NetworkTools.TcpLatencyAsync("127.0.0.1", 59998, 1500);
            Assert(ms == -1, "TCP 测速-关闭端口返回-1", "ms=" + ms);
        }

        private static async Task TestTcpLatencyDokodemoNotApplicable()
        {
            var n = new NodeConfig { Protocol = "dokodemo-door", Name = "d" };
            int ms = await NetworkTools.TcpLatencyAsync(n, 1000);
            Assert(ms == -2, "dokodemo-door 测速不适用(-2)", "ms=" + ms);
        }

        private static async Task TestConcurrencyLimit()
        {
            // 用一个永不快速响应的地址验证并发上限与超时
            var nodes = new List<NodeConfig>();
            for (int i = 0; i < 8; i++)
                nodes.Add(new NodeConfig { Protocol = "trojan", Name = "n" + i, Address = "192.0.2.1", Port = 1 }); // TEST-NET 不可达

            int done = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await NetworkTools.TestAllAsync(nodes, (n, ms) => { Interlocked.Increment(ref done); }, 4, 800);
            sw.Stop();
            // 8 个节点 / 4 并发 * 800ms 超时 ≈ 1600ms，不应串行超过 6.4s
            Assert(done == 8 && sw.ElapsedMilliseconds < 6400,
                "并发测速(4并发+超时)", "done=" + done + " elapsed=" + sw.ElapsedMilliseconds + "ms");
        }
    }
}
