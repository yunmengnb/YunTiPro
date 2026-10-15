package cloud.yimeng.kitsune

import org.json.JSONObject
import java.net.URLEncoder
import java.util.Base64

/** 节点 → 分享链接。导出格式与 LinkImporter 严格对应，保证跨设备导入可用。 */
object ShareExporter {
    // URLEncoder 把空格编成 "+"，而导入端按字面处理 "+"，这里换成 %20 保持语义一致
    private fun enc(s: String) = URLEncoder.encode(s, "UTF-8").replace("+", "%20")
    private fun b64Url(s: String) = Base64.getUrlEncoder().encodeToString(s.toByteArray(Charsets.UTF_8))

    fun export(n: Node): String {
        require(n.protocol != "dokodemo-door") { "本机入站转发不能分享" }
        val frag = "#" + enc(n.name)
        val hostPort = n.host + ":" + n.port
        return when (n.protocol) {
            "vmess" -> {
                val j = JSONObject()
                j.put("v", "2"); j.put("ps", n.name); j.put("add", n.host); j.put("port", n.port)
                j.put("id", n.credential); j.put("aid", 0); j.put("net", n.network); j.put("type", "none")
                j.put("host", n.wsHost); j.put("path", n.path); j.put("tls", if (n.tls) "tls" else "")
                if (n.sni.isNotEmpty()) j.put("sni", n.sni)
                if (n.method.isNotEmpty() && n.method != "auto") j.put("scy", n.method)
                "vmess://" + b64Url(j.toString())
            }
            "vless" -> "vless://" + enc(n.credential) + "@" + hostPort + query(n) + frag
            "trojan" -> "trojan://" + enc(n.credential) + "@" + hostPort + query(n) + frag
            "shadowsocks" -> "ss://" + b64Url(n.method + ":" + n.credential) + "@" + hostPort + frag
            "socks" -> "socks://" + userinfo(n) + hostPort + frag
            "http" -> "http://" + userinfo(n) + hostPort + frag
            else -> throw IllegalArgumentException("此协议不支持分享")
        }
    }

    private fun userinfo(n: Node) = if (n.username.isNotEmpty()) enc(n.username) + ":" + enc(n.credential) + "@" else ""

    private fun query(n: Node): String {
        val p = mutableListOf<String>()
        if (n.network != "tcp") p += "type=" + enc(n.network)
        if (n.tls) p += "security=tls"
        if (n.sni.isNotEmpty()) p += "sni=" + enc(n.sni)
        when (n.network) {
            "ws" -> {
                p += "path=" + enc(n.path)
                if (n.wsHost.isNotEmpty()) p += "host=" + enc(n.wsHost)
            }
            "grpc" -> p += "serviceName=" + enc(n.path)
        }
        if (n.protocol == "vless" && n.flow.isNotEmpty()) p += "flow=" + enc(n.flow)
        return if (p.isEmpty()) "" else "?" + p.joinToString("&")
    }
}
