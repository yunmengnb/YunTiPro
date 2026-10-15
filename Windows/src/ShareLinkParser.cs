using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace YunTiPro
{
    /// <summary>
    /// 分享链接解析结果
    /// </summary>
    public class ParseResult
    {
        public List<NodeConfig> Nodes = new List<NodeConfig>();
        public List<string> Errors = new List<string>();
    }

    /// <summary>
    /// 分享链接解析器：vmess:// vless:// trojan:// ss:// socks:// http://
    /// </summary>
    public static class ShareLinkParser
    {
        /// <summary>批量解析多行文本，空行与注释(#开头整行)忽略</summary>
        public static ParseResult ParseBatch(string text)
        {
            var result = new ParseResult();
            if (string.IsNullOrWhiteSpace(text)) { result.Errors.Add("内容为空"); return result; }
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int index = 0;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") && !line.Contains("://")) continue;
                index++;
                try
                {
                    var node = ParseSingle(line, index);
                    if (node != null) result.Nodes.Add(node);
                }
                catch (NotSupportedException ex)
                {
                    result.Errors.Add("第" + index + "行: " + ex.Message);
                }
                catch (Exception ex)
                {
                    result.Errors.Add("第" + index + "行: 解析失败 - " + ex.Message);
                }
            }
            if (result.Nodes.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add("未识别到有效链接");
            return result;
        }

        /// <summary>解析单条链接，不支持的参数抛出 NotSupportedException</summary>
        public static NodeConfig ParseSingle(string line, int indexForName)
        {
            string lower = line.ToLowerInvariant();
            NodeConfig node;
            if (lower.StartsWith("vmess://")) node = ParseVmess(line);
            else if (lower.StartsWith("vless://")) node = ParseVless(line);
            else if (lower.StartsWith("trojan://")) node = ParseTrojan(line);
            else if (lower.StartsWith("ss://")) node = ParseShadowsocks(line);
            else if (lower.StartsWith("socks://") || lower.StartsWith("socks5://")) node = ParseSocksHttp(line, "socks");
            else if (lower.StartsWith("http://")) node = ParseSocksHttp(line, "http");
            else throw new FormatException("无法识别的协议前缀");

            if (string.IsNullOrWhiteSpace(node.Name))
                node.Name = node.Protocol + "-" + indexForName;
            var err = node.Validate();
            if (err != null) throw new FormatException(err);
            return node;
        }

        // vmess://base64(json)
        private static NodeConfig ParseVmess(string link)
        {
            string b64 = link.Substring("vmess://".Length).Trim();
            string json = Encoding.UTF8.GetString(Base64Decode(b64));
            var ser = new JavaScriptSerializer();
            var d = ser.Deserialize<Dictionary<string, object>>(json);

            var node = new NodeConfig();
            node.Protocol = "vmess";
            node.Name = GetStr(d, "ps");
            node.Address = GetStr(d, "add");
            node.Port = GetPort(d, "port");
            node.Uuid = GetStr(d, "id");
            node.AlterId = GetPort(d, "aid");
            string scy = GetStr(d, "scy");
            node.Encryption = string.IsNullOrEmpty(scy) ? "auto" : scy;
            node.Network = string.IsNullOrEmpty(GetStr(d, "net")) ? "tcp" : GetStr(d, "net").ToLowerInvariant();
            node.Tls = string.Equals(GetStr(d, "tls"), "tls", StringComparison.OrdinalIgnoreCase) ? "tls" : "none";
            if (string.Equals(GetStr(d, "tls"), "reality", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("vmess 链接使用了不支持的 Reality");
            node.Sni = GetStr(d, "sni");
            node.WsPath = string.IsNullOrEmpty(GetStr(d, "path")) ? "/" : GetStr(d, "path");
            node.WsHost = GetStr(d, "host");
            // type 字段为 tcp 伪装类型，仅 none/http 常见；http 伪装此处按 tcp 处理
            return node;
        }

        // vless://uuid@host:port?query#name
        private static NodeConfig ParseVless(string link)
        {
            var uri = new Uri(link);
            var node = new NodeConfig();
            node.Protocol = "vless";
            node.Uuid = Uri.UnescapeDataString(uri.UserInfo);
            node.Address = uri.Host;
            node.Port = uri.Port > 0 ? uri.Port : 443;
            node.Name = DecodeFragment(uri.Fragment);
            var q = ParseQuery(uri.Query);

            string security = (q["security"] ?? "none").ToLowerInvariant();
            if (security == "reality")
                throw new NotSupportedException("vless 链接使用了不支持的 Reality，请更换节点");
            node.Tls = security == "tls" ? "tls" : "none";
            node.Network = string.IsNullOrEmpty(q["type"]) ? "tcp" : q["type"].ToLowerInvariant();
            node.Sni = q["sni"] ?? "";
            // grpc 用 serviceName 承载路径，ws 用 path
            string vp = q["serviceName"] ?? q["path"];
            node.WsPath = string.IsNullOrEmpty(vp) ? "/" : vp;
            node.WsHost = q["host"] ?? "";
            node.Flow = q["flow"] ?? "";
            if (!string.IsNullOrEmpty(node.Flow))
                throw new NotSupportedException("vless flow(xtls) 参数不受支持");
            string enc = q["encryption"] ?? "none";
            if (enc != "none")
                throw new NotSupportedException("vless encryption 仅支持 none");
            node.Encryption = "none";
            return node;
        }

        // trojan://password@host:port?query#name
        private static NodeConfig ParseTrojan(string link)
        {
            var uri = new Uri(link);
            var node = new NodeConfig();
            node.Protocol = "trojan";
            node.Password = Uri.UnescapeDataString(uri.UserInfo);
            node.Address = uri.Host;
            node.Port = uri.Port > 0 ? uri.Port : 443;
            node.Name = DecodeFragment(uri.Fragment);
            var q = ParseQuery(uri.Query);
            string security = (q["security"] ?? "tls").ToLowerInvariant();
            if (security == "reality")
                throw new NotSupportedException("trojan 链接使用了不支持的 Reality");
            node.Tls = security == "none" ? "none" : "tls";
            node.Sni = q["sni"] ?? q["peer"] ?? "";
            node.Network = string.IsNullOrEmpty(q["type"]) ? "tcp" : q["type"].ToLowerInvariant();
            string tp = q["serviceName"] ?? q["path"];
            node.WsPath = string.IsNullOrEmpty(tp) ? "/" : tp;
            node.WsHost = q["host"] ?? "";
            return node;
        }

        // ss://base64(method:password@host:port)#name 或 ss://base64(method:password)@host:port#name
        private static NodeConfig ParseShadowsocks(string link)
        {
            string body = link.Substring("ss://".Length);
            string name = "";
            int hash = body.IndexOf('#');
            if (hash >= 0) { name = DecodeFragment(body.Substring(hash)); body = body.Substring(0, hash); }
            // 去掉查询串中的 plugin 等参数
            int qm = body.IndexOf('?');
            if (qm >= 0)
            {
                var q = ParseQuery(body.Substring(qm));
                if (!string.IsNullOrEmpty(q["plugin"]))
                    throw new NotSupportedException("shadowsocks 插件(plugin)参数不受支持");
                body = body.Substring(0, qm);
            }

            var node = new NodeConfig();
            node.Protocol = "shadowsocks";
            node.Name = name;
            node.Network = "tcp";
            node.Tls = "none";

            string decoded = null;
            try { decoded = Encoding.UTF8.GetString(Base64Decode(body)); }
            catch { decoded = null; }

            if (decoded != null && decoded.Contains("@") && decoded.Contains(":"))
            {
                // method:password@host:port 整体 base64
                int at = decoded.LastIndexOf('@');
                string userInfo = decoded.Substring(0, at);
                string hostPort = decoded.Substring(at + 1);
                int colon = userInfo.IndexOf(':');
                node.Method = userInfo.Substring(0, colon);
                node.Password = userInfo.Substring(colon + 1);
                SplitHostPort(hostPort, out node.Address, out node.Port, 8388);
            }
            else
            {
                // base64(method:password)@host:port
                int at = body.LastIndexOf('@');
                if (at < 0) throw new FormatException("ss 链接缺少 @");
                string credB64 = body.Substring(0, at);
                string hostPort = body.Substring(at + 1);
                string cred = Encoding.UTF8.GetString(Base64Decode(credB64));
                int colon = cred.IndexOf(':');
                node.Method = cred.Substring(0, colon);
                node.Password = cred.Substring(colon + 1);
                SplitHostPort(hostPort, out node.Address, out node.Port, 8388);
            }
            if (node.Method.IndexOf("plugin", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new NotSupportedException("shadowsocks 插件参数不受支持");
            return node;
        }

        // socks://user:pass@host:port 或 http://user:pass@host:port
        private static NodeConfig ParseSocksHttp(string link, string protocol)
        {
            var uri = new Uri(link);
            var node = new NodeConfig();
            node.Protocol = protocol;
            node.Address = uri.Host;
            node.Port = uri.Port > 0 ? uri.Port : (protocol == "http" ? 8080 : 1080);
            node.Name = DecodeFragment(uri.Fragment);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var ui = Uri.UnescapeDataString(uri.UserInfo);
                int colon = ui.IndexOf(':');
                if (colon >= 0) { node.Username = ui.Substring(0, colon); node.Password = ui.Substring(colon + 1); }
                else node.Username = ui;
            }
            node.Network = "tcp";
            node.Tls = "none";
            return node;
        }

        // ---------- 工具方法 ----------

        /// <summary>兼容数字或字符串端口</summary>
        private static int GetPort(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            if (v is int) return (int)v;
            int p;
            return int.TryParse(Convert.ToString(v), out p) ? p : 0;
        }

        private static string GetStr(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : "";
        }

        /// <summary>Base64 解码，自动补 padding，兼容 URL-safe</summary>
        public static byte[] Base64Decode(string s)
        {
            s = s.Trim().Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
                case 1: throw new FormatException("Base64 长度非法");
            }
            return Convert.FromBase64String(s);
        }

        private static string DecodeFragment(string fragment)
        {
            if (string.IsNullOrEmpty(fragment)) return "";
            if (fragment.StartsWith("#")) fragment = fragment.Substring(1);
            return Uri.UnescapeDataString(fragment);
        }

        private static NameValueCollection ParseQuery(string query)
        {
            if (string.IsNullOrEmpty(query)) return new NameValueCollection();
            if (query.StartsWith("?")) query = query.Substring(1);
            return HttpUtility.ParseQueryString(query, Encoding.UTF8);
        }

        private static void SplitHostPort(string hostPort, out string host, out int port, int defaultPort)
        {
            hostPort = hostPort.Trim();
            // IPv6 [xxxx]:port
            if (hostPort.StartsWith("["))
            {
                int rb = hostPort.IndexOf(']');
                host = hostPort.Substring(1, rb - 1);
                port = defaultPort;
                if (rb + 1 < hostPort.Length && hostPort[rb + 1] == ':')
                    int.TryParse(hostPort.Substring(rb + 2), out port);
                return;
            }
            int c = hostPort.LastIndexOf(':');
            if (c >= 0)
            {
                host = hostPort.Substring(0, c);
                if (!int.TryParse(hostPort.Substring(c + 1), out port)) port = defaultPort;
            }
            else { host = hostPort; port = defaultPort; }
        }
    }
}
