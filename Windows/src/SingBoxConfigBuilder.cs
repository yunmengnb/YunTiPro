using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace YunTiPro
{
    /// <summary>
    /// 将节点配置 + 分流参数转换为 sing-box v1.3.6 配置 JSON（TUN 模式）。
    /// 仅使用 1.3.x 存在的字段（inet4_address / route.final / process_path 等），
    /// 生成结果应通过 sing-box.exe check 验证。
    /// </summary>
    public static class SingBoxConfigBuilder
    {
        /// <summary>TUN 虚拟网卡地址（/30 最小子网，避开常见私网段）</summary>
        public const string TunAddress = "172.18.0.1/30";

        /// <summary>
        /// 生成完整配置。
        /// filterMode: off / whitelist / blacklist。
        /// filterPaths: 分流名单（exe 完整路径）。
        /// alwaysDirectPaths: 始终直连的进程路径（本程序自身 + sing-box.exe，防回环）。
        /// probePath: 隧道探测子进程路径，非空时强制路由到 proxy（保证远端验证真实经过隧道）。
        /// </summary>
        public static string Build(NodeConfig node, string filterMode, IList<string> filterPaths,
            IList<string> alwaysDirectPaths, string probePath)
        {
            if (node == null) throw new ArgumentNullException("node");
            if (!node.IsOutboundCapable) throw new InvalidOperationException("该协议不能作为代理出口: " + node.Protocol);

            string mode = (filterMode ?? "off").ToLowerInvariant();
            var list = NormalizePaths(filterPaths);

            var rules = new List<object>();

            // 1) 防回环：本程序与 sing-box 自身的流量必须直连
            var directProcs = NormalizePaths(alwaysDirectPaths);
            if (directProcs.Count > 0)
            {
                rules.Add(new Dictionary<string, object>
                {
                    ["process_path"] = directProcs,
                    ["outbound"] = "direct"
                });
            }

            // 2) 探测子进程强制走代理：保证“远端验证”真实经过隧道而非本机直连
            if (!string.IsNullOrWhiteSpace(probePath))
            {
                rules.Add(new Dictionary<string, object>
                {
                    ["process_path"] = new List<string> { probePath },
                    ["outbound"] = "proxy"
                });
            }

            // 3) 私有/保留地址直连（tun 网段 172.18.0.0/30 包含在 172.16.0.0/12 内）
            rules.Add(new Dictionary<string, object>
            {
                ["ip_cidr"] = new List<string>
                {
                    "127.0.0.0/8", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
                    "169.254.0.0/16", "224.0.0.0/4", "255.255.255.255/32"
                },
                ["outbound"] = "direct"
            });

            // 4) 分流规则与默认出口
            string final;
            if (mode == "whitelist" && list.Count > 0)
            {
                rules.Add(new Dictionary<string, object>
                {
                    ["process_path"] = list,
                    ["outbound"] = "proxy"
                });
                final = "direct";
            }
            else if (mode == "blacklist" && list.Count > 0)
            {
                rules.Add(new Dictionary<string, object>
                {
                    ["process_path"] = list,
                    ["outbound"] = "direct"
                });
                final = "proxy";
            }
            else if (mode == "whitelist")
            {
                // 空白名单：退化为全局直连（调用方负责在 UI/日志中明示）
                final = "direct";
            }
            else
            {
                // off / 空黑名单：全局代理
                final = "proxy";
            }

            var root = new Dictionary<string, object>
            {
                ["log"] = new Dictionary<string, object> { ["level"] = "warn", ["timestamp"] = true },
                ["inbounds"] = new object[]
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "tun",
                        ["tag"] = "tun-in",
                        ["inet4_address"] = TunAddress,
                        ["auto_route"] = true,
                        ["strict_route"] = false,
                        // system 栈走系统网络协议栈，Win7 兼容性最好（gvisor 依赖更多系统调用）
                        ["stack"] = "system",
                        ["sniff"] = true
                    }
                },
                ["outbounds"] = new object[]
                {
                    BuildOutbound(node),
                    new Dictionary<string, object> { ["type"] = "direct", ["tag"] = "direct" },
                    new Dictionary<string, object> { ["type"] = "block", ["tag"] = "block" }
                },
                ["route"] = new Dictionary<string, object>
                {
                    ["rules"] = rules,
                    ["final"] = final,
                    // 自动检测出口网卡，避免代理服务器自身连接被 TUN 接管造成回环
                    ["auto_detect_interface"] = true
                }
            };

            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 1024 * 1024;
            return ser.Serialize(root);
        }

        /// <summary>白名单模式但名单为空（退化为全局直连），供 UI/日志提示</summary>
        public static bool IsDegenerateWhitelist(string filterMode, IList<string> filterPaths)
        {
            return string.Equals(filterMode, "whitelist", StringComparison.OrdinalIgnoreCase)
                && NormalizePaths(filterPaths).Count == 0;
        }

        /// <summary>清理名单：去空白、去重（路径比较不分大小写）</summary>
        public static List<string> NormalizePaths(IList<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            if (paths == null) return result;
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                string t = p.Trim();
                if (seen.Add(t)) result.Add(t);
            }
            return result;
        }

        /// <summary>按协议构建单个 sing-box 1.3.x 出站对象（tag=proxy）</summary>
        public static Dictionary<string, object> BuildOutbound(NodeConfig node)
        {
            string p = node.Protocol.ToLowerInvariant();
            switch (p)
            {
                case "vmess": return BuildVmess(node);
                case "vless": return BuildVless(node);
                case "trojan": return BuildTrojan(node);
                case "shadowsocks": return BuildShadowsocks(node);
                case "socks": return BuildSocks(node);
                case "http": return BuildHttp(node);
                default: throw new NotSupportedException("不支持的出站协议: " + p);
            }
        }

        private static Dictionary<string, object> BaseOutbound(NodeConfig n, string type)
        {
            return new Dictionary<string, object>
            {
                ["type"] = type,
                ["tag"] = "proxy",
                ["server"] = n.Address,
                ["server_port"] = n.Port
            };
        }

        private static Dictionary<string, object> BuildVmess(NodeConfig n)
        {
            var o = BaseOutbound(n, "vmess");
            o["uuid"] = n.Uuid;
            o["alter_id"] = n.AlterId;
            o["security"] = string.IsNullOrEmpty(n.Encryption) ? "auto" : n.Encryption;
            ApplyTlsAndTransport(o, n);
            return o;
        }

        private static Dictionary<string, object> BuildVless(NodeConfig n)
        {
            var o = BaseOutbound(n, "vless");
            o["uuid"] = n.Uuid;
            ApplyTlsAndTransport(o, n);
            return o;
        }

        private static Dictionary<string, object> BuildTrojan(NodeConfig n)
        {
            var o = BaseOutbound(n, "trojan");
            o["password"] = n.Password;
            ApplyTlsAndTransport(o, n);
            return o;
        }

        private static Dictionary<string, object> BuildShadowsocks(NodeConfig n)
        {
            var o = BaseOutbound(n, "shadowsocks");
            o["method"] = n.Method;
            o["password"] = n.Password;
            return o;
        }

        private static Dictionary<string, object> BuildSocks(NodeConfig n)
        {
            var o = BaseOutbound(n, "socks");
            o["version"] = "5";
            if (!string.IsNullOrEmpty(n.Username))
            {
                o["username"] = n.Username;
                o["password"] = n.Password ?? "";
            }
            return o;
        }

        private static Dictionary<string, object> BuildHttp(NodeConfig n)
        {
            var o = BaseOutbound(n, "http");
            if (!string.IsNullOrEmpty(n.Username))
            {
                o["username"] = n.Username;
                o["password"] = n.Password ?? "";
            }
            ApplyTls(o, n); // HTTPS 代理场景
            return o;
        }

        /// <summary>TLS 块：强制校验证书（insecure=false）</summary>
        private static void ApplyTls(Dictionary<string, object> o, NodeConfig n)
        {
            if (!string.Equals(n.Tls, "tls", StringComparison.OrdinalIgnoreCase)) return;
            o["tls"] = new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["server_name"] = string.IsNullOrEmpty(n.Sni) ? n.Address : n.Sni,
                ["insecure"] = false
            };
        }

        private static void ApplyTlsAndTransport(Dictionary<string, object> o, NodeConfig n)
        {
            ApplyTls(o, n);
            string network = string.IsNullOrEmpty(n.Network) ? "tcp" : n.Network.ToLowerInvariant();
            if (network == "ws")
            {
                var ws = new Dictionary<string, object>
                {
                    ["type"] = "ws",
                    ["path"] = string.IsNullOrEmpty(n.WsPath) ? "/" : n.WsPath
                };
                if (!string.IsNullOrEmpty(n.WsHost))
                {
                    ws["headers"] = new Dictionary<string, object> { ["Host"] = n.WsHost };
                }
                o["transport"] = ws;
            }
        }
    }
}
