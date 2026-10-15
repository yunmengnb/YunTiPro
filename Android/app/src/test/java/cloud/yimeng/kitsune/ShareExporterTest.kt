package cloud.yimeng.kitsune

import org.junit.Assert.*
import org.junit.Test

class ShareExporterTest {
    private val uuid = "4b418bb7-9037-433a-d7f0-c34330d73da7"

    private fun roundTrip(n: Node): Node = LinkImporter.parse(ShareExporter.export(n))

    @Test fun vmessRoundTrip() {
        val n = Node(name = "香港 01", protocol = "vmess", host = "1.2.3.4", port = 10086,
            credential = uuid, network = "ws", tls = false, path = "/ray", wsHost = "cdn.example.com")
        val m = roundTrip(n)
        assertEquals("vmess", m.protocol); assertEquals(n.name, m.name); assertEquals(n.host, m.host)
        assertEquals(n.port, m.port); assertEquals(uuid, m.credential); assertEquals("ws", m.network)
        assertFalse(m.tls); assertEquals("/ray", m.path); assertEquals("cdn.example.com", m.wsHost)
    }

    @Test fun vmessTlsSniRoundTrip() {
        val n = Node(name = "TLS 节点", protocol = "vmess", host = "a.com", port = 443,
            credential = uuid, network = "tcp", tls = true, sni = "sni.example.com")
        val m = roundTrip(n)
        assertTrue(m.tls); assertEquals("sni.example.com", m.sni); assertEquals("tcp", m.network)
    }

    @Test fun vlessRoundTrip() {
        val n = Node(name = "V", protocol = "vless", host = "b.com", port = 443,
            credential = uuid, network = "grpc", tls = true, path = "svc", sni = "s.b.com")
        val m = roundTrip(n)
        assertEquals("vless", m.protocol); assertEquals(uuid, m.credential)
        assertEquals("grpc", m.network); assertEquals("svc", m.path); assertEquals("s.b.com", m.sni)
    }

    @Test fun trojanRoundTrip() {
        val n = Node(name = "T 节点", protocol = "trojan", host = "t.com", port = 443,
            credential = "p@ss:w+rd", tls = true, network = "ws", path = "/t", wsHost = "h.com")
        val m = roundTrip(n)
        assertEquals("trojan", m.protocol); assertEquals("p@ss:w+rd", m.credential)
        assertTrue(m.tls); assertEquals("/t", m.path)
    }

    @Test fun shadowsocksRoundTrip() {
        val n = Node(name = "SS", protocol = "shadowsocks", host = "s.com", port = 8388,
            credential = "pass+word/1", method = "chacha20-ietf-poly1305", tls = false)
        val m = roundTrip(n)
        assertEquals("shadowsocks", m.protocol); assertEquals("chacha20-ietf-poly1305", m.method)
        assertEquals("pass+word/1", m.credential); assertEquals(8388, m.port)
    }

    @Test fun socksWithAuthRoundTrip() {
        val n = Node(name = "S", protocol = "socks", host = "10.0.0.1", port = 1080,
            username = "user", credential = "p@ss", tls = false)
        val m = roundTrip(n)
        assertEquals("socks", m.protocol); assertEquals("user", m.username); assertEquals("p@ss", m.credential)
    }

    @Test fun httpNoAuthRoundTrip() {
        val n = Node(name = "H", protocol = "http", host = "10.0.0.2", port = 8080, tls = false)
        val m = roundTrip(n)
        assertEquals("http", m.protocol); assertEquals("", m.username); assertEquals(8080, m.port)
    }

    @Test fun dokodemoRejected() {
        try { ShareExporter.export(Node(protocol = "dokodemo-door", host = "1.1.1.1", port = 80)); fail() }
        catch (_: IllegalArgumentException) {}
    }

    @Test fun specialCharsInNameSurvive() {
        val n = Node(name = "节点 A+B", protocol = "trojan", host = "t.com", port = 443, credential = "x", tls = true)
        assertEquals("节点 A+B", roundTrip(n).name)
    }
}
