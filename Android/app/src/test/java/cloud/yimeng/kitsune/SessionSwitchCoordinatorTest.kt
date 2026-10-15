package cloud.yimeng.kitsune

import org.junit.Assert.*
import org.junit.Test

class SessionSwitchCoordinatorTest {
    @Test fun requestIncrementsGenerationAndBecomesLatest() {
        val c = SessionSwitchCoordinator()
        val first = c.request("node-a")
        assertEquals("node-a", first.nodeId)
        assertTrue(c.isLatest(first))
        val second = c.request("node-b")
        assertTrue(second.generation > first.generation)
        assertEquals(second, c.latest())
    }
    @Test fun rapidSwitchKeepsOnlyNewestAuthoritative() {
        val c = SessionSwitchCoordinator()
        val stale = c.request("node-a")
        c.request("node-b")
        val newest = c.request("node-c")
        assertFalse("旧回调不得覆盖新状态", c.isLatest(stale))
        assertTrue(c.isLatest(newest))
        assertEquals("node-c", c.latest().nodeId)
    }
    @Test fun stopInvalidatesPendingSwitchRequests() {
        val c = SessionSwitchCoordinator()
        val pending = c.request("node-a")
        val stop = c.requestStop()
        assertNull(stop.nodeId)
        assertFalse(c.isLatest(pending))
        assertTrue(c.isLatest(stop))
        assertNull(c.latest().nodeId)
    }
    @Test fun requestAfterStopSupersedesStop() {
        val c = SessionSwitchCoordinator()
        val stop = c.requestStop()
        val reconnect = c.request("node-a")
        assertFalse(c.isLatest(stop))
        assertTrue(c.isLatest(reconnect))
    }
    @Test fun repeatedRequestSameNodeStillAdvancesAuthority() {
        val c = SessionSwitchCoordinator()
        val old = c.request("node-a")
        val fresh = c.request("node-a")
        assertFalse(c.isLatest(old))
        assertTrue(c.isLatest(fresh))
    }
    @Test(expected = IllegalArgumentException::class) fun blankNodeIdRejected() {
        SessionSwitchCoordinator().request("  ")
    }
}
