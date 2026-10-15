using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace YunTiPro
{
    /// <summary>
    /// 代理节点配置模型。一个类承载所有协议字段，按协议取用对应字段。
    /// </summary>
    public class NodeConfig
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "";
        /// <summary>协议：vmess / vless / trojan / shadowsocks / socks / http / dokodemo-door</summary>
        public string Protocol = "vmess";
        public string Address = "";
        public int Port = 443;

        // vmess / vless 字段
        public string Uuid = "";
        public int AlterId = 0;
        /// <summary>vmess 加密方式（auto/aes-128-gcm/chacha20-poly1305/none）；vless 固定 none</summary>
        public string Encryption = "auto";
        /// <summary>vless flow（如 xtls-rprx-vision 不支持，留空）</summary>
        public string Flow = "";

        // trojan / shadowsocks 密码
        public string Password = "";
        /// <summary>shadowsocks 加密方法</summary>
        public string Method = "aes-128-gcm";
        /// <summary>socks/http 出站用户名</summary>
        public string Username = "";

        // 传输层
        /// <summary>网络类型：tcp / ws（仅保留 Win7 兼容核心稳定支持的类型）</summary>
        public string Network = "tcp";
        /// <summary>传输安全：tls / none（拒绝 reality）</summary>
        public string Tls = "none";
        public string Sni = "";
        public string WsPath = "/";
        public string WsHost = "";

        // dokodemo-door（仅本机入站转发，不能作为出口）
        public string ListenAddress = "127.0.0.1";
        public string TargetAddress = "";
        public int TargetPort = 0;

        // 运行时数据，不持久化
        [ScriptIgnore]
        public int LatencyMs = -1;
        [ScriptIgnore]
        public bool LatencyTesting;

        /// <summary>支持作为代理出口的协议</summary>
        public static readonly HashSet<string> OutboundProtocols = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "vmess", "vless", "trojan", "shadowsocks", "socks", "http" };

        public bool IsOutboundCapable
        {
            get { return OutboundProtocols.Contains(Protocol ?? ""); }
        }

        /// <summary>卡片副标题：网络·TLS</summary>
        public string Subtitle
        {
            get
            {
                string net = string.IsNullOrEmpty(Network) ? "tcp" : Network;
                string tls = string.Equals(Tls, "tls", StringComparison.OrdinalIgnoreCase) ? "TLS" : "无TLS";
                if (Protocol == "dokodemo-door") return "本机转发";
                if (Protocol == "shadowsocks" || Protocol == "socks" || Protocol == "http") return net;
                return net + " · " + tls;
            }
        }

        public NodeConfig Clone()
        {
            return (NodeConfig)MemberwiseClone();
        }

        /// <summary>基础合法性校验，返回错误信息或 null</summary>
        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) return "节点名称不能为空";
            string p = (Protocol ?? "").ToLowerInvariant();
            if (p == "dokodemo-door")
            {
                if (string.IsNullOrWhiteSpace(TargetAddress) || TargetPort <= 0 || TargetPort > 65535)
                    return "dokodemo-door 需要有效的目标地址与端口";
                return null;
            }
            if (!OutboundProtocols.Contains(p)) return "不支持的协议: " + p;
            if (string.IsNullOrWhiteSpace(Address)) return "服务器地址不能为空";
            if (Port <= 0 || Port > 65535) return "端口无效";
            if ((p == "vmess" || p == "vless") && string.IsNullOrWhiteSpace(Uuid)) return "UUID 不能为空";
            if ((p == "trojan" || p == "shadowsocks") && string.IsNullOrWhiteSpace(Password)) return "密码不能为空";
            if (p == "shadowsocks" && string.IsNullOrWhiteSpace(Method)) return "加密方法不能为空";
            if (string.Equals(Tls, "reality", StringComparison.OrdinalIgnoreCase)) return "不支持 Reality";
            return null;
        }
    }
}
