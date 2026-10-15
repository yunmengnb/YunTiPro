package cloud.yimeng.kitsune

import org.junit.Assert.*
import org.junit.Test
import java.net.InetAddress
import java.net.ServerSocket

class RemoteProbeTest {
    private fun fakeSocks(secret: String, expectHost: String, authOk: Boolean = true, connectOk: Boolean = true): Pair<ServerSocket, Thread> {
        val server = ServerSocket(0, 1, InetAddress.getByName("127.0.0.1"))
        val t = Thread {
            runCatching {
                server.accept().use { s ->
                    val input = s.getInputStream(); val out = s.getOutputStream()
                    assertEquals(5, input.read()); assertEquals(1, input.read()); assertEquals(2, input.read())
                    out.write(byteArrayOf(5, 2)); out.flush()
                    assertEquals(1, input.read())
                    val ulen = input.read(); val u = ByteArray(ulen).also { input.read(it) }
                    val plen = input.read(); val p = ByteArray(plen).also { input.read(it) }
                    assertEquals("kitsune", String(u))
                    if (!authOk || String(p) != secret) { out.write(byteArrayOf(1, 1)); out.flush(); return@runCatching }
                    out.write(byteArrayOf(1, 0)); out.flush()
                    assertEquals(5, input.read()); assertEquals(1, input.read()); assertEquals(0, input.read()); assertEquals(3, input.read())
                    val hlen = input.read(); val h = ByteArray(hlen).also { input.read(it) }
                    assertEquals(expectHost, String(h))
                    input.read(); input.read()
                    if (!connectOk) { out.write(byteArrayOf(5, 5, 0, 1, 0, 0, 0, 0, 0, 0)); out.flush(); return@runCatching }
                    out.write(byteArrayOf(5, 0, 0, 1, 0, 0, 0, 0, 0, 0)); out.flush()
                    val req = ByteArray(256); val n = input.read(req)
                    assertTrue(String(req, 0, n).startsWith("GET /generate_204"))
                    out.write("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\n\r\n".toByteArray()); out.flush()
                }
            }
        }.apply { isDaemon = true; start() }
        return server to t
    }

    @Test fun probeSuccessReturnsLatency() {
        val (server, t) = fakeSocks("s3cret", "example.com")
        val ms = RemoteProbe.probe(server.localPort, "s3cret", "example.com", 80)
        server.close(); t.join(3000)
        assertNotNull(ms); assertTrue(ms!! >= 0)
    }

    @Test fun probeFailsOnAuthReject() {
        val (server, t) = fakeSocks("right", "example.com", authOk = false)
        assertNull(RemoteProbe.probe(server.localPort, "wrong", "example.com", 80))
        server.close(); t.join(3000)
    }

    @Test fun probeFailsOnConnectRefused() {
        val (server, t) = fakeSocks("s3cret", "example.com", connectOk = false)
        assertNull(RemoteProbe.probe(server.localPort, "s3cret", "example.com", 80))
        server.close(); t.join(3000)
    }

    @Test fun probeNoServerNeverThrows() {
        assertNull(RemoteProbe.probe(1, "x", connectTimeoutMs = 300, readTimeoutMs = 300))
    }
}
