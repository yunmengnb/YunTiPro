package cloud.yimeng.kitsune

import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.Socket
import java.net.SocketTimeoutException
import java.util.concurrent.*
import java.util.concurrent.atomic.AtomicInteger

/** TCP only: no proxy credentials, core controllers, or application requests. */
class TcpLatency(
    private val timeoutMs: Int = 3000,
    private val resolve: (String) -> InetAddress = { InetAddress.getByName(it) }
) : AutoCloseable {
    private val workers = Executors.newFixedThreadPool(4)
    private val sockets = ConcurrentHashMap.newKeySet<Socket>()
    @Volatile private var closed = false
    @Volatile var running = false
        private set

    @Synchronized fun start(nodes: List<Node>, result: (Node, String) -> Unit, done: () -> Unit): Boolean {
        if (closed || running) return false
        val targets = nodes.filter { it.protocol != "dokodemo-door" }
        if (targets.isEmpty()) return false
        running = true
        val index = AtomicInteger()
        val remaining = AtomicInteger(minOf(4, targets.size))
        repeat(remaining.get()) {
            workers.execute {
                try {
                    while (!closed) {
                        val n = targets.getOrNull(index.getAndIncrement()) ?: break
                        result(n, "TCP 测试中")
                        val value = measure(n.host, n.port)
                        if (!closed) result(n, value)
                    }
                } finally {
                    if (remaining.decrementAndGet() == 0) {
                        running = false
                        if (!closed) done()
                    }
                }
            }
        }
        return true
    }

    internal fun measure(host: String, port: Int): String {
        val deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(timeoutMs.toLong())
        var lookup: Future<InetAddress>? = null
        try {
            if (closed) return "TCP 未测"
            // DNS cannot reliably be interrupted on every platform. A shared capped pool
            // with no queue prevents blocked resolvers accumulating across Activity instances.
            lookup = dns.submit<InetAddress> { resolve(host.removeSurrounding("[", "]")) }
            val address = lookup.get(timeoutMs.toLong(), TimeUnit.MILLISECONDS)
            if (closed) return "TCP 未测"
            val remainingMs = TimeUnit.NANOSECONDS.toMillis(deadline - System.nanoTime())
            if (remainingMs <= 0) return "TCP 超时"
            Socket().use { socket ->
                sockets.add(socket)
                try {
                    if (closed) return "TCP 未测"
                    val start = System.nanoTime()
                    socket.connect(InetSocketAddress(address, port), remainingMs.toInt())
                    if (System.nanoTime() > deadline) return "TCP 超时"
                    return "TCP ${TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - start)} ms"
                } finally { sockets.remove(socket) }
            }
        } catch (_: TimeoutException) { return "TCP 超时"
        } catch (_: SocketTimeoutException) { return "TCP 超时"
        } catch (_: RejectedExecutionException) { return "TCP 失败（DNS 忙）"
        } catch (_: InterruptedException) {
            Thread.currentThread().interrupt()
            return "TCP 未测"
        } catch (_: Exception) { return "TCP 失败"
        } finally { lookup?.cancel(true) }
    }

    @Synchronized override fun close() {
        closed = true
        workers.shutdownNow()
        sockets.forEach { runCatching { it.close() } }
        sockets.clear()
        running = false
    }

    companion object {
        private val dns = ThreadPoolExecutor(0, 4, 30, TimeUnit.SECONDS,
            SynchronousQueue(), ThreadFactory { r -> Thread(r, "tcp-dns").apply { isDaemon = true } })
    }
}
