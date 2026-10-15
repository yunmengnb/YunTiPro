package cloud.yimeng.kitsune

import java.net.InetSocketAddress
import java.net.Socket

/** End-to-end reachability probe through the local authenticated SOCKS5 inbound. */
object RemoteProbe {
    private const val TARGET_HOST = "cp.cloudflare.com"
    private const val TARGET_PORT = 80

    /** Returns round-trip latency in ms when the remote endpoint responds, null otherwise. Never throws. */
    fun probe(port: Int, secret: String, targetHost: String = TARGET_HOST, targetPort: Int = TARGET_PORT,
              connectTimeoutMs: Int = 3000, readTimeoutMs: Int = 5000): Long? = runCatching {
        val started = System.currentTimeMillis()
        Socket().use { socket ->
            socket.connect(InetSocketAddress("127.0.0.1", port), connectTimeoutMs)
            socket.soTimeout = readTimeoutMs
            val out = socket.getOutputStream(); val input = socket.getInputStream()
            out.write(byteArrayOf(5, 1, 2)); out.flush()
            if (input.read() != 5 || input.read() != 2) return null
            val user = "kitsune".toByteArray(Charsets.UTF_8); val pass = secret.toByteArray(Charsets.UTF_8)
            if (user.size > 255 || pass.size > 255) return null
            out.write(byteArrayOf(1, user.size.toByte()) + user + byteArrayOf(pass.size.toByte()) + pass); out.flush()
            if (input.read() != 1 || input.read() != 0) return null
            val host = targetHost.toByteArray(Charsets.UTF_8)
            if (host.size > 255 || targetPort !in 1..65535) return null
            out.write(byteArrayOf(5, 1, 0, 3, host.size.toByte()) + host +
                byteArrayOf((targetPort shr 8).toByte(), targetPort.toByte())); out.flush()
            if (input.read() != 5 || input.read() != 0) return null
            input.read()
            when (input.read()) {
                1 -> input.skip(6)
                3 -> input.skip((input.read() + 2).toLong())
                4 -> input.skip(18)
                else -> return null
            }
            out.write("GET /generate_204 HTTP/1.1\r\nHost: $targetHost\r\nUser-Agent: yunti-probe\r\nConnection: close\r\n\r\n"
                .toByteArray(Charsets.UTF_8)); out.flush()
            val buf = ByteArray(32)
            val n = input.read(buf)
            if (n < 5 || !String(buf, 0, n, Charsets.US_ASCII).startsWith("HTTP/")) return null
            System.currentTimeMillis() - started
        }
    }.getOrNull()
}
