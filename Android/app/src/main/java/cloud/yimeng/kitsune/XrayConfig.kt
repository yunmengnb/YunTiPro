package cloud.yimeng.kitsune

import org.json.JSONArray
import org.json.JSONObject

object XrayConfig {
    private fun obj(vararg pairs: Pair<String, Any>) = JSONObject().apply { pairs.forEach { put(it.first, it.second) } }
    private fun arr(vararg values: Any) = JSONArray().apply { values.forEach { put(it) } }
    fun build(node: Node, forwards: List<Node>, socksPort: Int, password: String): String {
        node.validate(); require(node.protocol != "dokodemo-door") { "转发规则不能作为出口" }
        val server = obj("address" to node.host, "port" to node.port)
        val settings = when (node.protocol) {
            "vmess", "vless" -> {
                val user = obj("id" to node.credential)
                if (node.protocol == "vless") { user.put("encryption", "none"); if(node.flow.isNotEmpty()) user.put("flow", node.flow) }
                else { user.put("security", if(node.method in listOf("auto", "aes-128-gcm", "chacha20-poly1305", "none")) node.method else "auto"); user.put("alterId", 0) }
                server.put("users", arr(user)); obj("vnext" to arr(server))
            }
            "trojan", "shadowsocks" -> { server.put("password", node.credential); if(node.protocol == "shadowsocks") server.put("method", node.method); obj("servers" to arr(server)) }
            else -> { if(node.username.isNotEmpty()) server.put("users", arr(obj("user" to node.username, "pass" to node.credential))); obj("servers" to arr(server)) }
        }
        val stream = obj("network" to node.network, "security" to if(node.tls) "tls" else "none")
        if(node.tls) stream.put("tlsSettings", obj("allowInsecure" to false, "serverName" to node.sni.ifEmpty { node.host }))
        if(node.network == "ws") stream.put("wsSettings", obj("path" to node.path, "headers" to obj("Host" to node.wsHost.ifEmpty { node.host })))
        if(node.network == "grpc") stream.put("grpcSettings", obj("serviceName" to node.path))
        val inbound = arr(obj("tag" to "vpn", "listen" to "127.0.0.1", "port" to socksPort, "protocol" to "socks", "settings" to obj("auth" to "password", "accounts" to arr(obj("user" to "kitsune", "pass" to password)), "udp" to true, "ip" to "127.0.0.1")))
        val ports = mutableSetOf(socksPort)
        forwards.filter { it.enabled && it.protocol == "dokodemo-door" }.forEach {
            it.validate(); require(ports.add(it.listenPort)) { "本地转发端口冲突" }
            inbound.put(obj("tag" to "forward-${it.id}", "listen" to "127.0.0.1", "port" to it.listenPort, "protocol" to "dokodemo-door", "settings" to obj("address" to it.host, "port" to it.port, "network" to "tcp,udp", "followRedirect" to false)))
        }
        // #region debug-point A-B:xray-transport-log
        val debugLog = "/data/user/0/cloud.yimeng.kitsune/files/xray-game-debug.log"
        return obj("log" to obj("loglevel" to "debug", "access" to debugLog, "error" to debugLog), "inbounds" to inbound,
            "outbounds" to arr(obj("tag" to "proxy", "protocol" to node.protocol, "settings" to settings, "streamSettings" to stream)),
            "routing" to obj("domainStrategy" to "AsIs", "rules" to arr())).toString()
    }
}
