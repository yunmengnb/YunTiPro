package cloud.yimeng.kitsune

import org.junit.Assert.*
import org.junit.Test
import java.net.InetAddress
import java.net.ServerSocket
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicInteger

class TcpLatencyTest {
    @Test fun localhostConnectClosesWithoutSendingCredentials() {
        ServerSocket(0, 8, InetAddress.getLoopbackAddress()).use { server ->
            TcpLatency().use { tester ->
                assertTrue(tester.measure("127.0.0.1", server.localPort).matches(Regex("TCP \\d+ ms")))
                server.accept().use { peer -> peer.soTimeout = 1000; assertEquals(-1, peer.getInputStream().read()) }
            }
        }
    }
    @Test fun refusedLocalPortIsFailure() {
        val port = ServerSocket(0).use { it.localPort }
        TcpLatency().use { assertEquals("TCP 失败", it.measure("127.0.0.1", port)) }
    }
    @Test fun dnsDeadlineIsBounded() {
        TcpLatency(50) { Thread.sleep(1000); InetAddress.getLoopbackAddress() }.use {
            val start = System.nanoTime()
            assertEquals("TCP 超时", it.measure("synthetic", 1234))
            assertTrue(TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - start) < 900)
        }
    }
    @Test fun boundedWorkersRejectDuplicateAndSkipInbound() {
        val entered = CountDownLatch(4)
        val release = CountDownLatch(1)
        val done = CountDownLatch(1)
        val active = AtomicInteger()
        val maximum = AtomicInteger()
        val results = AtomicInteger()
        TcpLatency(2000) {
            val count = active.incrementAndGet()
            maximum.updateAndGet { previous -> maxOf(previous, count) }
            entered.countDown()
            try { release.await(); InetAddress.getLoopbackAddress() } finally { active.decrementAndGet() }
        }.use { tester ->
            val nodes = (1..20).map { Node(host = "synthetic", port = 0) } + Node(protocol = "dokodemo-door")
            assertTrue(tester.start(nodes, { n, value ->
                assertNotEquals("dokodemo-door", n.protocol)
                if (value != "TCP 测试中") results.incrementAndGet()
            }, { done.countDown() }))
            assertTrue(entered.await(1, TimeUnit.SECONDS))
            assertFalse(tester.start(nodes, { _, _ -> fail() }, {}))
            release.countDown()
            assertTrue(done.await(5, TimeUnit.SECONDS))
            assertTrue(maximum.get() <= 4)
            assertEquals(20, results.get())
            assertFalse(tester.running)
        }
    }
    @Test fun closeCancelsAndPreventsRestart() {
        val entered = CountDownLatch(1)
        val tester = TcpLatency(2000) { entered.countDown(); Thread.sleep(1000); InetAddress.getLoopbackAddress() }
        val completed = AtomicInteger()
        tester.start(listOf(Node(host = "synthetic")), { _, value -> if (value != "TCP 测试中") completed.incrementAndGet() }, { completed.incrementAndGet() })
        assertTrue(entered.await(1, TimeUnit.SECONDS))
        tester.close()
        Thread.sleep(100)
        assertEquals(0, completed.get())
        assertFalse(tester.start(listOf(Node()), { _, _ -> }, {}))
    }
    @Test fun inboundOnlyDoesNotStart() {
        TcpLatency().use { assertFalse(it.start(listOf(Node(protocol = "dokodemo-door")), { _, _ -> fail() }, { fail() })) }
    }
}
