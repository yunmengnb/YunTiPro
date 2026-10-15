package cloud.yimeng.kitsune

import org.json.JSONObject
import java.net.URI
import java.net.URLDecoder
import java.util.Base64

object LinkImporter {
    private fun decode(s: String) = URLDecoder.decode(s.replace("+", "%2B"), "UTF-8")
    private fun b64(s: String): String = String(Base64.getDecoder().decode(s.replace('-', '+').replace('_', '/')), Charsets.UTF_8)
    fun parse(text: String): Node {
        require(text.length <= 16384) { "链接过长" }
        val raw = text.trim()
        if (raw.startsWith("vmess://")) {
            val j = JSONObject(b64(raw.removePrefix("vmess://")))
            require(j.optInt("aid", 0) == 0 && j.optString("type", "none") in listOf("", "none")) { "不支持旧版 alterId 或伪装头" }
            require(j.optString("scy", "auto") in listOf("auto", "aes-128-gcm", "chacha20-poly1305", "none")) { "不支持此 VMess 加密" }
            return Node(name=j.optString("ps").ifBlank { "VMess" }, protocol="vmess", host=j.getString("add"), port=j.opt("port")?.toString()?.toIntOrNull() ?: error("端口必须为整数"), credential=j.getString("id"), network=j.optString("net", "tcp"), tls=j.optString("tls") == "tls", sni=j.optString("sni"), path=j.optString("path", "/"), wsHost=j.optString("host"), method=j.optString("scy", "auto")).validate()
        }
        var normalized = raw
        if (raw.startsWith("ss://") && '@' !in raw.substringBefore('#')) {
            normalized = "ss://" + b64(raw.removePrefix("ss://").substringBefore('#')) + if ('#' in raw) "#" + raw.substringAfter('#') else ""
        }
        // 真实世界链接的节点名 fragment 常含未编码中文，java.net.URI 严格遵循 RFC 会拒解析；
        // 先手动拆出 fragment 单独解码，主体再交给 URI
        val hashIdx = normalized.indexOf('#')
        val rawFragment = if (hashIdx >= 0) normalized.substring(hashIdx + 1) else ""
        val u = URI(if (hashIdx >= 0) normalized.substring(0, hashIdx) else normalized)
        val proto = when(u.scheme) { "ss" -> "shadowsocks"; "socks5" -> "socks"; "https" -> "http"; else -> u.scheme }
        require(proto in listOf("vless", "trojan", "shadowsocks", "socks", "http")) { "链接协议不支持；dokodemo-door 请手动添加" }
        val q = u.rawQuery.orEmpty().split('&').filter { it.isNotEmpty() }.associate { decode(it.substringBefore('=')) to decode(it.substringAfter('=', "")) }
        val allowed = setOf("type", "security", "sni", "peer", "path", "host", "serviceName", "flow", "encryption")
        require(q.keys.all { it in allowed }) { "链接包含尚不支持的参数（如 Reality / 插件 / 指纹），未导入" }
        require(q["security"].orEmpty() in listOf("", "none", "tls")) { "暂不支持 Reality" }
        require(q["encryption"].orEmpty() in listOf("", "none")) { "不支持此加密参数" }
        var auth = decode(u.rawUserInfo.orEmpty())
        var method = "aes-128-gcm"
        var username = ""
        if (proto == "shadowsocks") {
            if (':' !in auth) auth = b64(auth)
            require(':' in auth) { "SS 凭证格式错误" }
            method = auth.substringBefore(':'); auth = auth.substringAfter(':')
        } else if (proto in listOf("http", "socks") && ':' in auth) {
            username = auth.substringBefore(':'); auth = auth.substringAfter(':')
        } else if (proto in listOf("http", "socks") && auth.isNotEmpty()) {
            error("代理认证需要 username:password")
        }
        return Node(name=decode(rawFragment).ifBlank { proto.uppercase() }, protocol=proto, host=u.host?.removeSurrounding("[", "]") ?: error("主机格式错误"), port=if(u.port == -1) if(u.scheme == "https" || proto == "trojan") 443 else 1080 else u.port, credential=auth, username=username, method=method, network=q["type"] ?: "tcp", tls=q["security"] == "tls" || proto == "trojan" || u.scheme == "https", sni=q["sni"] ?: q["peer"].orEmpty(), path=q["serviceName"] ?: q["path"] ?: "/", wsHost=q["host"].orEmpty(), flow=q["flow"].orEmpty()).validate()
    }
    fun batch(text: String): List<Node> {
        require(text.length <= 262144) { "一次最多导入 256 KiB" }
        val lines = text.lines().map { it.trim() }.filter { it.isNotEmpty() }
        require(lines.size in 1..200) { "每次导入 1–200 条链接" }
        return lines.mapIndexed { i, line -> try { parse(line) } catch (_: Exception) { throw IllegalArgumentException("第 ${i+1} 行无效或含不支持的参数；全部取消，未保存") } }
    }
}
