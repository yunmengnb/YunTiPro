package cloud.yimeng.kitsune

import org.junit.Assert.*
import org.junit.Test

class AppFilterTest {
    @Test fun defaultModeIsOff() {
        assertEquals(AppFilter.MODE_OFF, AppFilter.modeOf(null))
        assertEquals(AppFilter.MODE_OFF, AppFilter.modeOf(""))
        assertEquals(AppFilter.MODE_OFF, AppFilter.modeOf("unknown"))
        assertEquals(AppFilter.MODE_OFF, AppFilter.modeOf(" whitelist "))
    }
    @Test fun validModesAccepted() {
        assertEquals(AppFilter.MODE_OFF, AppFilter.modeOf("off"))
        assertEquals(AppFilter.MODE_WHITELIST, AppFilter.modeOf("whitelist"))
        assertEquals(AppFilter.MODE_BLACKLIST, AppFilter.modeOf("blacklist"))
    }
    @Test fun parsePackageListHandlesEmpty() {
        assertTrue(AppFilter.parsePackageList(null).isEmpty())
        assertTrue(AppFilter.parsePackageList("").isEmpty())
        assertTrue(AppFilter.parsePackageList(" , ,, ").isEmpty())
    }
    @Test fun parsePackageListTrimsAndDeduplicates() {
        val parsed = AppFilter.parsePackageList(" com.a ,com.b,, com.a ,")
        assertEquals(setOf("com.a", "com.b"), parsed)
    }
    @Test fun serializeIsDeterministicAndRoundTrips() {
        val packages = setOf("com.zeta", "com.alpha", "org.mid")
        val serialized = AppFilter.serializePackageList(packages)
        assertEquals("com.alpha,com.zeta,org.mid", serialized)
        assertEquals(packages, AppFilter.parsePackageList(serialized))
    }
    @Test fun serializeEmptySetIsEmptyString() {
        assertEquals("", AppFilter.serializePackageList(emptySet()))
    }
}
