package cloud.yimeng.kitsune

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Intent
import android.content.pm.PackageManager
import android.net.VpnService
import android.os.ParcelFileDescriptor
import java.io.File
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket
import java.util.UUID
import java.util.concurrent.Executors
import java.util.concurrent.ScheduledFuture
import java.util.concurrent.TimeUnit

class ProxyVpnService : VpnService() {
    private val worker = Executors.newSingleThreadScheduledExecutor()
    private var tun: ParcelFileDescriptor? = null
    private var bridge: NativeBridge? = null
    private var tunnelConfig: File? = null
    private var monitor: ScheduledFuture<*>? = null
    private var ownerSessionId = 0L
    private var ownerRequest: SessionSwitchCoordinator.Request? = null
    @Volatile private var destroyed = false

    override fun onCreate() {
        super.onCreate()
        getSystemService(NotificationManager::class.java).createNotificationChannel(NotificationChannel("vpn", "代理连接", NotificationManager.IMPORTANCE_LOW))
    }

    private fun notification(text: String): Notification {
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE)
        val stop = PendingIntent.getService(this, 1, Intent(this, ProxyVpnService::class.java).setAction(ACTION_STOP), PendingIntent.FLAG_IMMUTABLE)
        return Notification.Builder(this, "vpn").setSmallIcon(R.drawable.ic_fox).setContentTitle("云梯pro").setContentText(text).setContentIntent(open).setOngoing(true).addAction(Notification.Action.Builder(null, "断开", stop).build()).build()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_STOP -> SessionState.requestStop()
            ACTION_CONNECT -> intent.getStringExtra(EXTRA_NODE_ID)?.takeIf { it.isNotBlank() }?.let { SessionState.request(it) }
            else -> Store(this).selected.takeIf { it.isNotBlank() }?.let { SessionState.request(it) }
        }
        startForeground(1, notification(SessionState.state))
        worker.execute { reconcile() }
        return START_NOT_STICKY
    }

    /** Serialized on [worker]: releases the old core/TUN completely before starting the latest request. */
    private fun reconcile() {
        if (destroyed) return
        var request = SessionState.latestRequest()
        if (ownerSessionId != 0L || tun != null || bridge != null) {
            if (ownerRequest == request && SessionState.active) return
            releaseSession(if (request.nodeId == null) "已断开" else "正在切换节点")
            request = SessionState.latestRequest()
        }
        if (request.nodeId == null) {
            SessionState.stopped("已断开")
            stopForeground(STOP_FOREGROUND_REMOVE)
            stopSelf()
            return
        }
        val sessionId = SessionState.begin(request) ?: return
        ownerSessionId = sessionId
        ownerRequest = request
        getSystemService(NotificationManager::class.java).notify(1, notification("正在初始化"))
        connect(request, sessionId)
    }

    private fun connect(request: SessionSwitchCoordinator.Request, sessionId: Long) {
        try {
            val native = NativeBridge()
            try { native.check() } catch (_: Throwable) { fail(sessionId, "原生核心未安装或不兼容，未建立代理连接"); return }
            bridge = native
            val store = Store(this)
            val nodes = store.load()
            val node = nodes.firstOrNull { it.id == request.nodeId && it.protocol != "dokodemo-door" } ?: error("No selected node")
            check(store.selected == request.nodeId && SessionState.isLatest(request))
            // #region debug-point A:outbound-capability
            SessionState.log("[DEBUG] sessionId=game-login-proxy runId=post-fix hypothesisId=A location=ProxyVpnService.connect outboundProtocol=${node.protocol} transport=${node.network} udpInbound=true")
            // #endregion
            val port = ServerSocket(0, 1, java.net.InetAddress.getByName("127.0.0.1")).use { it.localPort }
            val secret = UUID.randomUUID().toString().replace("-", "")
            native.start(XrayConfig.build(node, nodes, port, secret), filesDir.absolutePath)
            Socket().use { socket ->
                socket.connect(InetSocketAddress("127.0.0.1", port), 3000); socket.soTimeout = 3000
                val out = socket.getOutputStream(); val input = socket.getInputStream()
                out.write(byteArrayOf(5, 1, 2)); out.flush()
                check(input.read() == 5 && input.read() == 2)
                val user = "kitsune".toByteArray(); val pass = secret.toByteArray()
                out.write(byteArrayOf(1, user.size.toByte()) + user + byteArrayOf(pass.size.toByte()) + pass); out.flush()
                check(input.read() == 1 && input.read() == 0)
            }
            check(SessionState.isLatest(request))
            val tunnelMtu = 1400
            val builder = Builder().setSession("云梯pro").setMtu(tunnelMtu)
                .addAddress("198.18.0.1", 30).addAddress("fc00::1", 126)
                .addRoute("0.0.0.0", 0).addRoute("::", 0)
                .addDnsServer("1.1.1.1")
            // 应用分流。系统 API 互斥：白名单（addAllowed）与黑名单（addDisallowed）不能混用。
            // 白名单模式下本应用不在允许列表即自动直连，无需再 Disallow；黑名单/关闭时需显式排除自身。
            // 名单内已卸载的包会抛 NameNotFoundException，逐个跳过。
            val filterMode = AppFilter.modeOf(store.preferences.getString(AppFilter.KEY_MODE, null))
            val filterList = AppFilter.parsePackageList(store.preferences.getString(AppFilter.KEY_LIST, "")) - packageName
            // #region debug-point D:app-scope
            SessionState.log("[DEBUG] sessionId=game-login-proxy runId=post-fix hypothesisId=D location=ProxyVpnService.connect filterMode=$filterMode selectedPackages=${filterList.size}")
            // #endregion
            when (filterMode) {
                AppFilter.MODE_WHITELIST -> {
                    var applied = 0
                    filterList.forEach { pkg -> try { builder.addAllowedApplication(pkg); applied++ } catch (_: PackageManager.NameNotFoundException) {} }
                    if (applied == 0) {
                        // 空白名单无法表达"全部不走代理"（空允许列表=不限制），退化为不限制并提示
                        try { builder.addDisallowedApplication(packageName) } catch (_: Exception) {}
                        SessionState.log("白名单为空：全部应用走代理（本应用直连）")
                    } else SessionState.log("分流已启用：白名单 $applied 个应用走代理，其余直连")
                }
                AppFilter.MODE_BLACKLIST -> {
                    try { builder.addDisallowedApplication(packageName) } catch (_: Exception) {}
                    var applied = 0
                    filterList.forEach { pkg -> try { builder.addDisallowedApplication(pkg); applied++ } catch (_: PackageManager.NameNotFoundException) {} }
                    SessionState.log("分流已启用：黑名单 $applied 个应用直连")
                }
                else -> try { builder.addDisallowedApplication(packageName) } catch (_: Exception) {}
            }
            if (android.os.Build.VERSION.SDK_INT >= 29) builder.setMetered(false)
            // #region debug-point B-C-E:tun-network
            SessionState.log("[DEBUG] sessionId=game-login-proxy runId=post-fix hypothesisId=B,C,E location=ProxyVpnService.connect dns=1.1.1.1 ipv4Route=0.0.0.0/0 ipv6Route=::/0 mtu=$tunnelMtu")
            // #endregion
            tun = builder.establish() ?: error("VPN permission revoked")
            val yml = """
                tunnel:
                  mtu: $tunnelMtu
                  ipv4: 198.18.0.1
                socks5:
                  address: 127.0.0.1
                  port: $port
                  username: 'kitsune'
                  password: '$secret'
                  udp: 'udp'
                mapdns:
                  address: 1.1.1.1
                  port: 53
                  network: 100.64.0.0
                  netmask: 255.192.0.0
                  cache-size: 4096
                misc:
                  # region debug-point A-B:hev-packet-log
                  log-file: /data/user/0/cloud.yimeng.kitsune/files/hev-game-debug.log
                  log-level: debug
            """.trimIndent()
            tunnelConfig = File(filesDir, "tunnel.yml").apply { writeText(yml) }
            // #region debug-point A-B-E:tunnel-config
            SessionState.log("[DEBUG] sessionId=game-login-proxy runId=post-fix hypothesisId=A,B,E location=ProxyVpnService.connect socksUdp=udp mapDns=1.1.1.1:53 tunnelMtu=$tunnelMtu tunnelIpv6=fc00::1")
            // #endregion
            check(native.startTunnel(tunnelConfig!!.absolutePath, tun!!.fd))
            Thread.sleep(200)
            check(native.running() && SessionState.isLatest(request))
            SessionState.update(sessionId, "已建立隧道 · 正在验证远端…")
            getSystemService(NotificationManager::class.java).notify(1, notification("已建立隧道，正在验证远端连通性"))
            startProbe(request, sessionId, port, secret)
            monitor = worker.scheduleWithFixedDelay({
                if (ownerSessionId == sessionId && !runCatching { native.running() }.getOrDefault(false)) fail(sessionId, "隧道已异常退出")
            }, 2, 2, TimeUnit.SECONDS)
        } catch (t: Throwable) {
            // 记录异常类型与摘要到应用内日志（不含节点地址/凭证），便于定位失败环节
            SessionState.log("连接异常: " + t.javaClass.simpleName + " / " + (t.message ?: "").take(120))
            if (!SessionState.isLatest(request)) {
                releaseSession("正在切换节点")
                worker.execute { reconcile() }
            } else fail(sessionId, "连接失败：请检查节点、端口、权限和原生核心；可点击重试")
        }
    }

    /** Real remote probe off the serialized worker; guards prevent stale sessions from writing state. */
    private fun startProbe(request: SessionSwitchCoordinator.Request, sessionId: Long, port: Int, secret: String) {
        Thread({
            Thread.sleep(800)
            var latency: Long? = null
            var attempt = 0
            while (attempt < 3 && latency == null && ownerSessionId == sessionId && !destroyed) {
                if (attempt > 0) Thread.sleep(4000)
                latency = RemoteProbe.probe(port, secret)
                attempt++
            }
            if (ownerSessionId != sessionId || !SessionState.isLatest(request)) return@Thread
            val message = if (latency != null) "已连接 · 远端正常 ${latency} ms" else "已连接 · 远端验证失败，节点可能不可用或网络受限"
            SessionState.update(sessionId, message)
            runCatching { getSystemService(NotificationManager::class.java).notify(1, notification(message)) }
        }, "remote-probe").apply { isDaemon = true }.start()
    }

    private fun fail(sessionId: Long, message: String) {
        if (ownerSessionId != sessionId) return
        releaseSession(message)
        stopForeground(STOP_FOREGROUND_REMOVE)
        stopSelf()
    }

    private fun releaseSession(message: String) {
        val releasedSessionId = ownerSessionId
        monitor?.cancel(false); monitor = null
        bridge?.stop(); bridge = null
        runCatching { tun?.close() }; tun = null
        tunnelConfig?.delete(); tunnelConfig = null
        ownerSessionId = 0L
        ownerRequest = null
        if (releasedSessionId != 0L) SessionState.finish(releasedSessionId, message)
    }

    override fun onRevoke() {
        SessionState.requestStop()
        worker.execute { releaseSession("系统已撤销连接权限"); stopForeground(STOP_FOREGROUND_REMOVE); stopSelf() }
        super.onRevoke()
    }

    override fun onDestroy() {
        destroyed = true
        worker.execute { releaseSession(if (SessionState.latestRequest().nodeId == null) "已断开" else "连接已停止") }
        worker.shutdown()
        super.onDestroy()
    }

    companion object {
        const val ACTION_CONNECT = "cloud.yimeng.kitsune.CONNECT"
        const val ACTION_STOP = "cloud.yimeng.kitsune.STOP"
        const val EXTRA_NODE_ID = "nodeId"
    }
}
