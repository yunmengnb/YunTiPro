using System;
using System.Collections.Generic;
using System.Text;
using System.Web.Script.Serialization;

namespace YunTiPro
{
    /// <summary>
    /// 节点 → 分享链接。导出格式与 ShareLinkParser 严格对应，保证回环导入后字段一致。
    /// </summary>
    public static class ShareLinkExporter
    {
        public static string Export(NodeConfig n)
        {
            if (n == null) throw new ArgumentNullException("n");
            string frag = "#" + Uri.EscapeDataString(n.Name ?? "");
            string hostPort = FormatHost(n.Address) + ":" + n.Port;
            switch (n.Protocol)
            {
                case "vmess":
                {
                    var d = new Dictionary<string, object>
                    {
                        ["v"] = "2", ["ps"] = n.Name ?? "", ["add"] = n.Address, ["port"] = n.Port,
                        ["id"] = n.Uuid, ["aid"] = n.AlterId, ["net"] = string.IsNullOrEmpty(n.Network) ? "tcp" : n.Network,
                        ["type"] = "none", ["host"] = n.WsHost ?? "", ["path"] = string.IsNullOrEmpty(n.WsPath) ? "/" : n.WsPath,
                        ["tls"] = n.Tls == "tls" ? "tls" : ""
                    };
                    if (!string.IsNullOrEmpty(n.Sni)) d["sni"] = n.Sni;
                    if (!string.IsNullOrEmpty(n.Encryption) && n.Encryption != "auto") d["scy"] = n.Encryption;
                    string json = new JavaScriptSerializer().Serialize(d);
                    return "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
                        .Replace('+', '-').Replace('/', '_');
                }
                case "vless":
                    return "vless://" + Uri.EscapeDataString(n.Uuid) + "@" + hostPort + Query(n, "none") + frag;
                case "trojan":
                    return "trojan://" + Uri.EscapeDataString(n.Password) + "@" + hostPort + Query(n, "tls") + frag;
                case "shadowsocks":
                    return "ss://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(n.Method + ":" + n.Password))
                        .Replace('+', '-').Replace('/', '_') + "@" + hostPort + frag;
                case "socks":
                    return "socks://" + UserInfo(n) + hostPort + frag;
                case "http":
                    return "http://" + UserInfo(n) + hostPort + frag;
                default:
                    throw new NotSupportedException("此协议不支持分享: " + n.Protocol);
            }
        }

        private static string UserInfo(NodeConfig n)
        {
            return string.IsNullOrEmpty(n.Username) ? ""
                : Uri.EscapeDataString(n.Username) + ":" + Uri.EscapeDataString(n.Password ?? "") + "@";
        }

        private static string FormatHost(string host)
        {
            return host != null && host.Contains(":") ? "[" + host + "]" : host;
        }

        private static string Query(NodeConfig n, string defaultSecurity)
        {
            var p = new List<string>();
            string network = string.IsNullOrEmpty(n.Network) ? "tcp" : n.Network;
            if (network != "tcp") p.Add("type=" + Uri.EscapeDataString(network));
            string security = n.Tls == "tls" ? "tls" : defaultSecurity;
            if (security != "none") p.Add("security=" + security);
            if (!string.IsNullOrEmpty(n.Sni)) p.Add("sni=" + Uri.EscapeDataString(n.Sni));
            if (network == "ws")
            {
                p.Add("path=" + Uri.EscapeDataString(string.IsNullOrEmpty(n.WsPath) ? "/" : n.WsPath));
                if (!string.IsNullOrEmpty(n.WsHost)) p.Add("host=" + Uri.EscapeDataString(n.WsHost));
            }
            else if (network == "grpc")
            {
                p.Add("serviceName=" + Uri.EscapeDataString(n.WsPath ?? ""));
            }
            return p.Count == 0 ? "" : "?" + string.Join("&", p);
        }
    }
}
