package cloud.yimeng.kitsune

import org.json.JSONObject
import java.util.UUID

val protocols = listOf("vmess", "vless", "trojan", "shadowsocks", "socks", "http", "dokodemo-door")
data class Node(
    val id: String = UUID.randomUUID().toString(), val name: String = "新节点",
    val protocol: String = "vless", val host: String = "", val port: Int = 443,
    val credential: String = "", val username: String = "", val method: String = "aes-128-gcm",
    val network: String = "tcp", val tls: Boolean = true, val sni: String = "",
    val path: String = "/", val wsHost: String = "", val flow: String = "",
    val listenPort: Int = 10808, val enabled: Boolean = false
) {
    fun validate(): Node {
        require(protocol in protocols) { "不支持的协议" }
        require(name.isNotBlank() && name.length <= 100) { "名称需为 1–100 字符" }
        require(host.isNotBlank() && host.length <= 253 && host.none { it.isWhitespace() || it in "/?#@" }) { "请输入有效主机名或 IP，不含协议前缀" }
        require(port in 1..65535 && listenPort in 1024..65535) { "端口范围错误，本地端口须为 1024–65535" }
        require(network in listOf("tcp", "ws", "grpc")) { "仅支持 TCP / WebSocket / gRPC" }
        if (protocol in listOf("vmess", "vless")) require(runCatching { UUID.fromString(credential) }.isSuccess) { "UUID 格式错误" }
        if (protocol in listOf("trojan", "shadowsocks")) require(credential.isNotEmpty()) { "密码不能为空" }
        if (protocol == "shadowsocks") require(method in listOf("aes-128-gcm", "aes-256-gcm", "chacha20-ietf-poly1305")) { "仅支持 Shadowsocks AEAD 三种标准加密方式" }
        if (protocol == "trojan") require(tls) { "Trojan 必须开启 TLS" }
        require(flow.isEmpty() || (protocol == "vless" && tls && network == "tcp" && flow == "xtls-rprx-vision")) { "Vision 仅支持 VLESS + TCP + TLS" }
        require(listOf(sni, path, wsHost, username, credential).all { it.length <= 4096 && '\n' !in it && '\r' !in it }) { "字段过长或含换行" }
        return this
    }
    fun json() = JSONObject().apply {
        put("id", id); put("name", name); put("protocol", protocol); put("host", host); put("port", port)
        put("credential", credential); put("username", username); put("method", method); put("network", network)
        put("tls", tls); put("sni", sni); put("path", path); put("wsHost", wsHost); put("flow", flow)
        put("listenPort", listenPort); put("enabled", enabled)
    }
    companion object {
        fun from(j: JSONObject) = Node(j.getString("id"), j.getString("name"), j.getString("protocol"), j.getString("host"), j.getInt("port"), j.optString("credential"), j.optString("username"), j.optString("method", "aes-128-gcm"), j.optString("network", "tcp"), j.optBoolean("tls", true), j.optString("sni"), j.optString("path", "/"), j.optString("wsHost"), j.optString("flow"), j.optInt("listenPort", 10808), j.optBoolean("enabled"))
    }
}
