using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace YunTiPro
{
    /// <summary>
    /// 将节点配置转换为 Xray 核心配置 JSON。
    /// 本地入站为 SOCKS5(带随机认证) + HTTP(复用同端口不行，独立 HTTP 入站端口)。
    /// </summary>
    public static class XrayConfigBuilder
    {
        /// <summary>
        /// 生成完整配置。localSocksPort/localHttpPort 为本机监听端口，authUser/authPass 为随机认证。
        /// </summary>
        public static string Build(NodeConfig node, int localSocksPort, int localHttpPort, string authUser, string authPass)
        {
            if (node == null) throw new ArgumentNullException("node");
            if (!node.IsOutboundCapable) throw new InvalidOperationException("该协议不能作为代理出口: " + node.Protocol);

            var inbounds = new List<object>();
            // SOCKS5 入站：仅监听 127.0.0.1，随机端口 + 随机账号密码
            inbounds.Add(new Dictionary<string, object>
            {
                ["tag"] = "socks-in",
                ["listen"] = "127.0.0.1",
                ["port"] = localSocksPort,
                ["protocol"] = "socks",
                ["settings"] = new Dictionary<string, object>
                {
                    ["auth"] = "password",
                    ["accounts"] = new object[] {
                        new Dictionary<string, object> { ["user"] = authUser, ["pass"] = authPass }
                    },
                    ["udp"] = false
                },
                ["sniffing"] = new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["destOverride"] = new object[] { "http", "tls" }
                }
            });
            // HTTP 入站：供系统代理使用。不带认证——系统代理无法附带凭据，
            // 否则浏览器会反复弹"代理需要用户名和密码"。仅监听 127.0.0.1 + 随机端口已足够。
            inbounds.Add(new Dictionary<string, object>
            {
                ["tag"] = "http-in",
                ["listen"] = "127.0.0.1",
                ["port"] = localHttpPort,
                ["protocol"] = "http",
                ["settings"] = new Dictionary<string, object>
                {
                    ["allowTransparent"] = false
                }
            });

            var outbound = BuildOutbound(node);

            var root = new Dictionary<string, object>
            {
                ["log"] = new Dictionary<string, object> { ["loglevel"] = "warning" },
                ["inbounds"] = inbounds,
                ["outbounds"] = new object[]
                {
                    outbound,
                    new Dictionary<string, object> { ["tag"] = "direct", ["protocol"] = "freedom" },
                    new Dictionary<string, object> { ["tag"] = "block", ["protocol"] = "blackhole" }
                },
                ["routing"] = new Dictionary<string, object>
                {
                    ["domainStrategy"] = "AsIs",
                    ["rules"] = new object[]
                    {
                        // 私有地址直连，避免把局域网流量送进代理
                        new Dictionary<string, object>
                        {
                            ["type"] = "field",
                            ["ip"] = new object[] { "geoip:private" },
                            ["outboundTag"] = "direct"
                        }
                    }
                }
            };

            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 1024 * 1024;
            return ser.Serialize(root);
        }

        /// <summary>按协议构建单个出站对象</summary>
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

        private static Dictionary<string, object> BuildVmess(NodeConfig n)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = "proxy",
                ["protocol"] = "vmess",
                ["settings"] = new Dictionary<string, object>
                {
                    ["vnext"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["address"] = n.Address,
                            ["port"] = n.Port,
                            ["users"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["id"] = n.Uuid,
                                    ["alterId"] = n.AlterId,
                                    ["security"] = string.IsNullOrEmpty(n.Encryption) ? "auto" : n.Encryption
                                }
                            }
                        }
                    }
                },
                ["streamSettings"] = BuildStream(n)
            };
        }

        private static Dictionary<string, object> BuildVless(NodeConfig n)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = "proxy",
                ["protocol"] = "vless",
                ["settings"] = new Dictionary<string, object>
                {
                    ["vnext"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["address"] = n.Address,
                            ["port"] = n.Port,
                            ["users"] = new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    ["id"] = n.Uuid,
                                    ["encryption"] = "none"
                                }
                            }
                        }
                    }
                },
                ["streamSettings"] = BuildStream(n)
            };
        }

        private static Dictionary<string, object> BuildTrojan(NodeConfig n)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = "proxy",
                ["protocol"] = "trojan",
                ["settings"] = new Dictionary<string, object>
                {
                    ["servers"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["address"] = n.Address,
                            ["port"] = n.Port,
                            ["password"] = n.Password
                        }
                    }
                },
                ["streamSettings"] = BuildStream(n)
            };
        }

        private static Dictionary<string, object> BuildShadowsocks(NodeConfig n)
        {
            return new Dictionary<string, object>
            {
                ["tag"] = "proxy",
                ["protocol"] = "shadowsocks",
                ["settings"] = new Dictionary<string, object>
                {
                    ["servers"] = new object[]
                    {
                        new Dictionary<string, object>
                        {
                            ["address"] = n.Address,
                            ["port"] = n.Port,
                            ["method"] = n.Method,
                            ["password"] = n.Password
                        }
                    }
                }
            };
        }

        private static Dictionary<string, object> BuildSocks(NodeConfig n)
        {
            var server = new Dictionary<string, object>
            {
                ["address"] = n.Address,
                ["port"] = n.Port
            };
            if (!string.IsNullOrEmpty(n.Username))
            {
                server["users"] = new object[]
                {
                    new Dictionary<string, object> { ["user"] = n.Username, ["pass"] = n.Password ?? "" }
                };
            }
            return new Dictionary<string, object>
            {
                ["tag"] = "proxy",
                ["protocol"] = "socks",
                ["settings"] = new Dictionary<string, object> { ["servers"] = new object[] { server } }
            };
        }

        private static Dictionary<string, object> BuildHttp(NodeConfig n)
        {
            var server = new Dictionary<string, object>
            {
                ["address"] = n.Address,
                ["port"] = n.Port
            };
            if (!string.IsNullOrEmpty(n.Username))
            {
                server["users"] = new object[]
                {
                    new Dictionary<string, object> { ["user"] = n.Username, ["pass"] = n.Password ?? "" }
                };
            }
            return new Dictionary<string, object>
            {
                ["tag"] = "proxy",
                ["protocol"] = "http",
                ["settings"] = new Dictionary<string, object> { ["servers"] = new object[] { server } }
            };
        }

        /// <summary>构建 streamSettings：tcp / ws + 可选 TLS(强制校验证书)</summary>
        private static Dictionary<string, object> BuildStream(NodeConfig n)
        {
            var stream = new Dictionary<string, object>();
            string network = string.IsNullOrEmpty(n.Network) ? "tcp" : n.Network.ToLowerInvariant();
            bool tls = string.Equals(n.Tls, "tls", StringComparison.OrdinalIgnoreCase);

            stream["network"] = network;
            stream["security"] = tls ? "tls" : "none";

            if (tls)
            {
                // TLS 验证开启：不设置 allowInsecure（默认 false）
                stream["tlsSettings"] = new Dictionary<string, object>
                {
                    ["serverName"] = string.IsNullOrEmpty(n.Sni) ? n.Address : n.Sni,
                    ["allowInsecure"] = false
                };
            }
            if (network == "ws")
            {
                var ws = new Dictionary<string, object>
                {
                    ["path"] = string.IsNullOrEmpty(n.WsPath) ? "/" : n.WsPath
                };
                if (!string.IsNullOrEmpty(n.WsHost))
                {
                    ws["headers"] = new Dictionary<string, object> { ["Host"] = n.WsHost };
                }
                stream["wsSettings"] = ws;
            }
            return stream;
        }
    }
}
