package cloud.yimeng.kitsune

import java.lang.reflect.Proxy

/** Reflection permits a usable node editor build without distributing fake native stubs. */
class NativeBridge {
    private var core: Any? = null
    private var hev: Class<*>? = null
    fun check() {
        val lib = Class.forName("libv2ray.Libv2ray")
        check((lib.getMethod("checkVersionX").invoke(null) as String).isNotBlank())
        lib.getMethod("initCoreEnv", String::class.java, String::class.java)
        lib.getMethod("newCoreController", Class.forName("libv2ray.CoreCallbackHandler"))
        val c = Class.forName("libv2ray.CoreController")
        c.getMethod("startLoop", String::class.java, Int::class.javaPrimitiveType)
        c.getMethod("stopLoop")
        hev = Class.forName("hev.htproxy.TProxyService").also {
            it.getMethod("TProxyStartService", String::class.java, Int::class.javaPrimitiveType)
            it.getMethod("TProxyStopService")
            it.getMethod("TProxyIsRunning").invoke(null)
        }
    }
    fun start(config: String, directory: String) {
        val lib = Class.forName("libv2ray.Libv2ray")
        lib.getMethod("initCoreEnv", String::class.java, String::class.java).invoke(null, directory, "")
        val callback = Class.forName("libv2ray.CoreCallbackHandler")
        val handler = Proxy.newProxyInstance(callback.classLoader, arrayOf(callback)) { proxy, method, args ->
            when(method.name) {
                "toString" -> "KitsuneCoreCallback"
                "hashCode" -> System.identityHashCode(proxy)
                "equals" -> proxy === args?.firstOrNull()
                else -> if(method.returnType == Long::class.javaPrimitiveType) 0L else 0
            }
        }
        core = lib.getMethod("newCoreController", callback).invoke(null, handler)
        core!!.javaClass.getMethod("startLoop", String::class.java, Int::class.javaPrimitiveType).invoke(core, config, 0)
    }
    fun startTunnel(path: String, fd: Int) = hev!!.getMethod("TProxyStartService", String::class.java, Int::class.javaPrimitiveType).invoke(null, path, fd) as Boolean
    fun running() = hev?.getMethod("TProxyIsRunning")?.invoke(null) as? Boolean ?: false
    fun stop() {
        runCatching { hev?.getMethod("TProxyStopService")?.invoke(null) }
        runCatching { core?.javaClass?.getMethod("stopLoop")?.invoke(core) }
        core = null
    }
    companion object {
        fun availability(): String = try { NativeBridge().check(); "核心检查通过，可尝试连接；远端连通性尚未验证" } catch (_: Throwable) { "原生核心未安装或 ABI / 接口不兼容，连接已禁用" }
        fun available() = runCatching { NativeBridge().check() }.isSuccess
    }
}
