package cloud.yimeng.kitsune

import org.junit.Assert.*
import org.junit.Test
import org.json.JSONObject
import java.util.Base64

class ConfigTest {
    private val uuid = "00000000-0000-4000-8000-000000000001"
    private fun vmessFixture(name: String?, port: Any): String {
        val json = JSONObject().put("add", "example.com").put("id", uuid)
            .put("port", port).put("aid", 0).put("net", "ws").put("type", "none")
            .put("host", "").put("path", "/").put("tls", "none")
        if (name != null) json.put("ps", name)
        return "vmess://" + Base64.getEncoder().encodeToString(json.toString().toByteArray(Charsets.UTF_8))
    }
    @Test fun vmessBlankNamesAndPortTypes() {
        for (name in listOf(null, "", "   ", "测试")) {
            for (port in listOf(15584, "15584")) {
                val node = LinkImporter.parse(vmessFixture(name, port))
                assertEquals(if (name.isNullOrBlank()) "VMess" else name, node.name)
                assertEquals(15584, node.port); assertEquals("ws", node.network)
                assertFalse(node.tls); assertEquals("/", node.path); assertEquals("", node.wsHost)
                val config = JSONObject(XrayConfig.build(node, emptyList(), 32123, "local-secret"))
                val stream = config.getJSONArray("outbounds").getJSONObject(0).getJSONObject("streamSettings")
                assertEquals("ws", stream.getString("network"))
                assertEquals("none", stream.getString("security"))
            }
        }
    }
    @Test fun vmessRejectInvalidPorts() {
        for (port in listOf(0, 65536, -1, 15584.5, "15584.5", "invalid", true, JSONObject.NULL)) {
            assertTrue("Invalid port must fail", runCatching { LinkImporter.parse(vmessFixture("", port)) }.isFailure)
        }
    }
    @Test fun emptyFragmentsUseProtocolName() {
        for (fragment in listOf("", "#", "#%20%20")) {
            assertEquals("VLESS", LinkImporter.parse("vless://$uuid@example.com:443$fragment").name)
            assertEquals("TROJAN", LinkImporter.parse("trojan://test@example.com:443$fragment").name)
            assertEquals("SHADOWSOCKS", LinkImporter.parse("ss://YWVzLTEyOC1nY206cGFzcw@example.com:8388$fragment").name)
        }
    }
    @Test fun vlessImport() {
        val n = LinkImporter.parse("vless://$uuid@example.com:443?security=tls&type=ws&path=%2Fproxy#%E6%B5%8B%E8%AF%95")
        assertEquals("测试", n.name); assertEquals("/proxy", n.path); assertTrue(n.tls)
    }
    @Test fun unencodedChineseFragmentImport() {
        // 真实世界很多工具导出的链接 fragment 是未编码中文，java.net.URI 严格模式会拒解析，必须兼容
        val n = LinkImporter.parse("vless://$uuid@qr-test.example.com:443?type=ws&security=tls&path=%2Fray#E2E扫码测试")
        assertEquals("E2E扫码测试", n.name); assertEquals("qr-test.example.com", n.host)
        assertEquals(443, n.port); assertEquals("ws", n.network); assertTrue(n.tls); assertEquals("/ray", n.path)
        val t = LinkImporter.parse("trojan://p@example.com:443#香港节点 01")
        assertEquals("香港节点 01", t.name)
    }
    @Test fun ssImport() {
        val auth = Base64.getEncoder().withoutPadding().encodeToString("aes-128-gcm:pass+word".toByteArray())
        val n = LinkImporter.parse("ss://$auth@example.com:8388#SS")
        assertEquals("pass+word", n.credential); assertFalse(n.tls)
    }
    @Test fun ipv6() { assertEquals("2001:db8::1", LinkImporter.parse("trojan://secret@[2001:db8::1]:443#ipv6").host) }
    @Test(expected=IllegalArgumentException::class) fun rejectReality() { LinkImporter.parse("vless://$uuid@example.com:443?security=reality") }
    @Test(expected=IllegalArgumentException::class) fun rejectInsecure() { LinkImporter.parse("trojan://secret@example.com:443?allowInsecure=1") }
    @Test(expected=IllegalArgumentException::class) fun rejectPlugin() { LinkImporter.parse("ss://YWVzLTEyOC1nY206cGFzcw@example.com:443?plugin=x") }
    @Test fun allOutbounds() {
        protocols.filter { it != "dokodemo-door" }.forEach { p ->
            val n = Node(protocol=p, host="example.com", credential=if(p in listOf("vmess", "vless")) uuid else "secret")
            val config = JSONObject(XrayConfig.build(n, emptyList(), 32123, "local-secret"))
            assertEquals(p, config.getJSONArray("outbounds").getJSONObject(0).getString("protocol"))
            val tls = config.getJSONArray("outbounds").getJSONObject(0).getJSONObject("streamSettings").getJSONObject("tlsSettings")
            assertFalse(tls.getBoolean("allowInsecure"))
            assertEquals("127.0.0.1", config.getJSONArray("inbounds").getJSONObject(0).getString("listen"))
        }
    }
    @Test fun forwardOnlyInbound() {
        val forward = Node(protocol="dokodemo-door", host="example.com", enabled=true)
        val j = JSONObject(XrayConfig.build(Node(host="example.com", credential=uuid), listOf(forward), 32000, "secret"))
        assertEquals("dokodemo-door", j.getJSONArray("inbounds").getJSONObject(1).getString("protocol"))
        assertEquals(1, j.getJSONArray("outbounds").length())
    }
    @Test(expected=IllegalArgumentException::class) fun forbidForwardOutbound() { XrayConfig.build(Node(protocol="dokodemo-door", host="example.com"), emptyList(), 32000, "secret") }
    @Test(expected=IllegalArgumentException::class) fun portCollision() { XrayConfig.build(Node(host="example.com", credential=uuid), listOf(Node(protocol="dokodemo-door", host="example.com", enabled=true, listenPort=32000)), 32000, "secret") }
    @Test fun storageRoundTrip() { val n=Node(host="example.com", credential=uuid); assertEquals(n, Node.from(n.json())) }
    @Test(expected=IllegalArgumentException::class) fun batchAtomic() { LinkImporter.batch("trojan://secret@example.com:443\ninvalid") }
}
