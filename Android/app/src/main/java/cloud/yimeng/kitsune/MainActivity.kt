package cloud.yimeng.kitsune

import android.app.Activity
import android.app.AlertDialog
import android.app.Dialog
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.VpnService
import android.os.Build
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.text.Editable
import android.text.InputType
import android.text.TextWatcher
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import android.widget.*

class MainActivity : Activity() {
    private val blue = Color.rgb(37, 99, 235)
    private val ink = Color.rgb(15, 23, 42)
    private val muted = Color.rgb(100, 116, 139)
    private lateinit var store: Store
    private var nodes = mutableListOf<Node>()
    private lateinit var content: LinearLayout
    private lateinit var status: TextView
    private lateinit var connect: Button
    private var page = "节点"
    private var loadFailed = false
    private var latency = TcpLatency()
    private var measurementGeneration = 0
    private val measurements = mutableMapOf<String, String>()
    private val latencyLabels = mutableMapOf<String, TextView>()
    private lateinit var refresh: Button
    private var measurementToast: Toast? = null
    private fun showMeasurementNotice(value: String) {
        measurementToast?.cancel()
        measurementToast = Toast.makeText(this, value, Toast.LENGTH_SHORT).also {
            it.setGravity(Gravity.BOTTOM or Gravity.CENTER_HORIZONTAL, 0, dp(72))
            it.show()
        }
    }
    /** 延迟按结果着色：成功绿、失败红、其余灰 */
    private fun latencyColor(status: String) = when {
        status.endsWith("ms") -> Color.rgb(22, 163, 74)
        status.contains("失败") -> Color.rgb(220, 38, 38)
        else -> muted
    }
    private fun refreshLatencies() {
        if (loadFailed || latency.running) return
        val generation = measurementGeneration
        nodes.filter { it.protocol != "dokodemo-door" }.forEach { measurements[it.id] = "TCP 等待中" }
        showMeasurementNotice("正在测试 TCP 端口连接耗时（不代表代理可用）")
        val started = latency.start(nodes.toList(), { node, value ->
            handler.post {
                if (generation == measurementGeneration && !isDestroyed && nodes.any { it == node }) {
                    measurements[node.id] = value
                    latencyLabels[node.id]?.let { it.text = value; it.setTextColor(latencyColor(value)) }
                }
            }
        }, { handler.post {
            if (generation == measurementGeneration && !isDestroyed) {
                updateRefreshUi()
                showMeasurementNotice("TCP 测速完成")
            }
        } })
        updateRefreshUi()
        latencyLabels.forEach { (id, label) -> val s = measurements[id] ?: "不适用"; label.text = s; label.setTextColor(latencyColor(s)) }
        if (!started) {
            measurementToast?.cancel()
            showMeasurementNotice("没有可测试的出口节点")
        }
    }
    private fun updateRefreshUi() {
        if (!::refresh.isInitialized) return
        refresh.isEnabled = !loadFailed && !latency.running
        refresh.text = if (latency.running) "…" else "↻"
        refresh.contentDescription = if (latency.running) "正在测试 TCP 连通性" else "刷新节点 TCP 连通性延迟"
    }
    private fun cancelMeasurements() {
        measurementGeneration++
        measurementToast?.cancel()
        measurementToast = null
        latency.close()
        measurements.replaceAll { _, value -> if (value == "TCP 等待中" || value == "TCP 测试中") "TCP 未测" else value }
    }
    private val handler = Handler(Looper.getMainLooper())
    private val ticker = object : Runnable { override fun run() { if(page == "节点" && ::status.isInitialized && ::connect.isInitialized) updateConnectionUi(); handler.postDelayed(this, 800) } }
    private fun updateConnectionUi() {
        status.text = SessionState.state
        connect.text = if(SessionState.active || SessionState.busy) "停止" else "启动"
        connect.contentDescription = if(SessionState.active || SessionState.busy) "停止代理连接" else "启动代理连接"
    }
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        store = Store(this)
        if (store.preferences.getBoolean("secureScreen", true)) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        try { nodes = store.load() } catch (_: Exception) { loadFailed = true; message("无法解密节点数据", "为避免覆盖原数据，写入已禁用。请确认设备密钥未被清除。") }
        render()
        if (!store.preferences.getBoolean("disclaimerAccepted", false)) showDisclaimerConsent()
        if(Build.VERSION.SDK_INT >= 33 && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 7)
    }
    override fun onResume() { super.onResume(); handler.post(ticker) }
    override fun onPause() { handler.removeCallbacks(ticker); super.onPause() }
    override fun onStop() {
        cancelMeasurements()
        latency = TcpLatency()
        updateRefreshUi()
        latencyLabels.forEach { (id, label) -> label.text = measurements[id] ?: "不适用" }
        super.onStop()
    }
    override fun onDestroy() {
        cancelMeasurements()
        handler.removeCallbacksAndMessages(null)
        latencyLabels.clear()
        super.onDestroy()
    }
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
    private fun shape(color: Int, border: Int = Color.TRANSPARENT) = GradientDrawable().apply { setColor(color); cornerRadius = dp(18).toFloat(); setStroke(dp(1), border) }
    private fun text(value: String, size: Float = 15f, color: Int = ink, bold: Boolean = false) = TextView(this).apply {
        text = value; textSize = size; setTextColor(color); if(bold) setTypeface(null, Typeface.BOLD); setPadding(0, dp(6), 0, dp(6))
    }
    private fun column() = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
    private fun button(label: String, primary: Boolean = false, action: () -> Unit) = Button(this).apply {
        text = label; isAllCaps = false; textSize = 14f; minHeight = dp(48)
        setTextColor(if(primary) Color.WHITE else blue); background = shape(if(primary) blue else Color.rgb(239,246,255)); setOnClickListener { action() }
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(8); bottomMargin = dp(4) }
    }
    private fun card(): LinearLayout = column().apply {
        background = shape(Color.WHITE, Color.rgb(226,232,240)); setPadding(dp(18), dp(14), dp(18), dp(14))
        layoutParams = LinearLayout.LayoutParams(-1, -2).apply { bottomMargin = dp(14) }
    }
    private fun render() {
        val root = column().apply { setBackgroundColor(Color.rgb(248,250,252)) }
        root.setOnApplyWindowInsetsListener { v, insets ->
            v.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom); insets
        }
        val toolbar = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL; setPadding(dp(12), dp(4), dp(16), dp(4)); setBackgroundColor(Color.WHITE) }
        toolbar.addView(Button(this).apply { text = "☰"; contentDescription = "打开导航抽屉"; textSize = 24f; setTextColor(ink); background = shape(Color.WHITE); setOnClickListener { drawer() } }, LinearLayout.LayoutParams(dp(52), dp(56)))
        toolbar.addView(text("云梯pro", 20f, ink, true), LinearLayout.LayoutParams(0, -2, 1f))
        refresh = Button(this).apply {
            textSize = 24f; setTextColor(blue); background = shape(Color.WHITE)
            visibility = if (page == "节点") View.VISIBLE else View.GONE
            setPadding(0, 0, 0, 0); setOnClickListener { refreshLatencies() }
        }
        toolbar.addView(refresh, LinearLayout.LayoutParams(dp(48), dp(48)))
        updateRefreshUi()
        toolbar.addView(text("v" + BuildConfig.VERSION_NAME, 12f, muted))
        latencyLabels.clear()
        root.addView(toolbar)
        content = column().apply { setPadding(dp(20), dp(20), dp(20), dp(28)) }
        val scroll = ScrollView(this).apply { isFillViewport = true; addView(content) }
        root.addView(scroll, LinearLayout.LayoutParams(-1, 0, 1f))
        setContentView(root)
        when(page) { "分流" -> appFilter(); "日志" -> logs(); "设置" -> settings(); "关于" -> about(); else -> home() }
    }
    private fun home() {
        status = text(SessionState.state, 14f, muted).apply {
            gravity = Gravity.CENTER
            accessibilityLiveRegion = View.ACCESSIBILITY_LIVE_REGION_POLITE
        }
        content.addView(status)
        connect = button("启动", true) { toggle() }.apply {
            textSize = 20f
            setTypeface(null, Typeface.BOLD)
            gravity = Gravity.CENTER
            setPadding(dp(8), dp(8), dp(8), dp(8))
            val circle = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(blue) }
            background = android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(Color.argb(60, 255, 255, 255)), circle, null)
            layoutParams = LinearLayout.LayoutParams(dp(112), dp(112)).apply {
                gravity = Gravity.CENTER_HORIZONTAL; topMargin = dp(20); bottomMargin = dp(24)
            }
        }
        updateConnectionUi()
        content.addView(connect)
        if(!NativeBridge.available()) content.addView(text("原生核心未安装或不兼容，连接已禁用", 12f, muted))
        val actions = LinearLayout(this)
        actions.addView(button("＋ 新建节点") { editor(null) }, LinearLayout.LayoutParams(0, dp(52), 1f).apply { rightMargin=dp(8) })
        actions.addView(button("导入节点参数") { importChooser() }, LinearLayout.LayoutParams(0, dp(52), 1f))
        content.addView(actions)
        content.addView(text("我的节点  ${nodes.count { it.protocol != "dokodemo-door" }}", 20f, ink, true))
        if(nodes.isEmpty()) {
            content.addView(card().apply {
                addView(text("还没有节点", 18f, ink, true)); addView(text("添加你的服务器，或粘贴分享链接。\n支持 VMess、VLESS、Trojan、SS、SOCKS 和 HTTP。", 14f, muted)); addView(text("dokodemo-door 仅用作本机入站转发。", 12f, muted))
            })
        }
        nodes.filter { it.protocol != "dokodemo-door" }.forEach { nodeCard(it) }
        if(nodes.any { it.protocol == "dokodemo-door" }) {
            content.addView(text("本机入站转发", 20f, ink, true))
            content.addView(text("只监听 127.0.0.1；随代理启动，通过所选出口转发。", 12f, muted))
            nodes.filter { it.protocol == "dokodemo-door" }.forEach { nodeCard(it) }
        }
    }
    private fun nodeCard(n: Node) {
        val selected = store.selected == n.id
        val c = column().apply {
            setPadding(dp(12), dp(10), dp(12), dp(4))
            layoutParams = LinearLayout.LayoutParams(-1, -2).apply { bottomMargin = dp(8) }
            background = shape(if (selected) Color.rgb(239,246,255) else Color.WHITE, if (selected) blue else Color.rgb(226,232,240)).apply { cornerRadius = dp(12).toFloat() }
        }
        val top = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL }
        top.addView(View(this).apply {
            background = GradientDrawable().apply { shape = GradientDrawable.OVAL; setColor(if (selected) blue else Color.rgb(203,213,225)) }
        }, LinearLayout.LayoutParams(dp(8), dp(8)).apply { rightMargin = dp(8) })
        top.addView(text(n.name, 16f, ink, true).apply {
            setPadding(0, 0, 0, 0); maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END
        }, LinearLayout.LayoutParams(0, -2, 1f))
        c.addView(top)
        if (n.protocol == "dokodemo-door") {
            c.addView(text(if (n.enabled) "已启用 · 本机入站转发" else "已禁用 · 本机入站转发", 11f, muted).apply { setPadding(dp(16), dp(1), 0, dp(1)) })
        }
        if(n.protocol != "dokodemo-door") {
            c.isFocusable = true
            c.isClickable = true
            c.isSelected = selected
            c.contentDescription = "${n.name}，${n.protocol.uppercase()}，${if (selected) "已选择" else "点击选择并连接"}"
            c.foreground = android.graphics.drawable.RippleDrawable(android.content.res.ColorStateList.valueOf(Color.argb(28, 37, 99, 235)), null, null)
            c.setOnClickListener { selectNode(n) }
        }
        val row = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL }
        val status = if(n.protocol == "dokodemo-door") "不适用" else measurements[n.id] ?: "TCP 未测"
        val label = text(status, 11f, latencyColor(status)).apply { setPadding(dp(16), 0, 0, 0) }
        latencyLabels[n.id] = label
        row.addView(label, LinearLayout.LayoutParams(0, -2, 1f))
        fun smallButton(title: String, action: () -> Unit) = button(title, action = action).apply {
            textSize = 12f; minWidth = 0; minimumWidth = 0; minHeight = dp(48); setPadding(dp(4), 0, dp(4), 0)
            contentDescription = "$title 节点 ${n.name}"
            background = android.graphics.drawable.InsetDrawable(shape(Color.rgb(239,246,255)).apply { cornerRadius = dp(10).toFloat() }, 0, dp(6), 0, dp(6))
        }
        if (n.protocol != "dokodemo-door") row.addView(smallButton("分享") { shareChooser(n) }, LinearLayout.LayoutParams(dp(52), dp(48)).apply { leftMargin=dp(4) })
        row.addView(smallButton("编辑") { if(editable()) editor(n) }, LinearLayout.LayoutParams(dp(52), dp(48)).apply { leftMargin=dp(4) })
        row.addView(smallButton("删除") {
            if(editable()) AlertDialog.Builder(this).setTitle("删除节点？").setMessage("此操作不能撤销。").setNegativeButton("取消", null).setPositiveButton("删除") { _, _ ->
                if(editable() && persist(nodes.filter { it.id != n.id })) {
                    if(store.selected == n.id) store.selected = ""
                    render()
                }
            }.show()
        }, LinearLayout.LayoutParams(dp(52), dp(48)).apply { leftMargin=dp(2) })
        c.addView(row); content.addView(c)
    }
    private fun shareNode(n: Node) {
        val url = try { ShareExporter.export(n) } catch (t: Throwable) { toast(t.message ?: "无法分享"); return }
        val view = TextView(this).apply {
            text = url; textSize = 13f; setTextColor(ink); setTextIsSelectable(true)
            setPadding(dp(16), dp(12), dp(16), dp(12)); setBackgroundColor(Color.rgb(248,250,252))
        }
        val scroll = ScrollView(this).apply { addView(view) }
        val wrap = FrameLayout(this).apply { setPadding(dp(16), dp(8), dp(16), 0); addView(scroll, FrameLayout.LayoutParams(-1, dp(160))) }
        AlertDialog.Builder(this).setTitle("分享节点「${n.name}」").setView(wrap)
            .setPositiveButton("复制链接") { _, _ ->
                (getSystemService(CLIPBOARD_SERVICE) as android.content.ClipboardManager)
                    .setPrimaryClip(android.content.ClipData.newPlainText("节点链接", url))
                toast("已复制，可在其他设备的导入节点参数中粘贴")
            }
            .setNegativeButton("关闭", null).show()
    }
    private fun shareChooser(n: Node) {
        AlertDialog.Builder(this).setTitle("分享节点「${n.name}」")
            .setItems(arrayOf("分享节点参数", "分享节点二维码")) { _, which ->
                when (which) {
                    0 -> shareNode(n)
                    1 -> shareQr(n)
                }
            }
            .setNegativeButton("取消", null).show()
    }
    private fun shareQr(n: Node) {
        val url = try { ShareExporter.export(n) } catch (t: Throwable) { toast(t.message ?: "无法分享"); return }
        val bitmap = try { QrCode.encode(url, dp(260)) } catch (t: Throwable) { toast("生成二维码失败"); return }
        val image = ImageView(this).apply {
            setImageBitmap(bitmap); contentDescription = "节点 ${n.name} 的导入二维码"
        }
        val wrap = FrameLayout(this).apply {
            setPadding(dp(16), dp(16), dp(16), 0)
            addView(image, FrameLayout.LayoutParams(dp(260), dp(260), Gravity.CENTER))
        }
        AlertDialog.Builder(this).setTitle("扫码导入「${n.name}」").setView(wrap)
            .setPositiveButton("关闭", null).show()
    }
    private var lastSelectNodeId: String? = null
    private var lastSelectAt = 0L
    private fun selectNode(node: Node) {
        if (loadFailed) { toast("数据读取失败，禁止覆盖"); return }
        if (SessionState.active || SessionState.busy) {
            // Session alive: a card tap means "switch to this node". The
            // coordinator treats only the newest request as authoritative, so
            // repeated taps on the same target are dropped to avoid churn.
            if (SessionState.latestRequest().nodeId == node.id && store.selected == node.id) return
            val now = System.currentTimeMillis()
            if (lastSelectNodeId == node.id && now - lastSelectAt < 600) return
            lastSelectNodeId = node.id; lastSelectAt = now
            try { store.selected = node.id } catch (_: Exception) { toast("保存所选节点失败，未切换"); return }
            startVpn(node.id)
            render()
        } else {
            if (store.selected == node.id) return
            try { store.selected = node.id } catch (_: Exception) { toast("保存所选节点失败，未切换"); return }
            render()
        }
    }
    private fun editable(): Boolean {
        if(loadFailed) { toast("数据读取失败，禁止覆盖"); return false }
        if(SessionState.active || SessionState.busy) { toast("请先停止代理连接再修改配置"); return false }
        return true
    }
    private fun persist(updated: List<Node>): Boolean = try {
        check(!loadFailed && !SessionState.active && !SessionState.busy)
        store.save(updated)
        cancelMeasurements()
        latency = TcpLatency()
        val unchanged = nodes.filter { it in updated }.map { it.id }.toSet()
        measurements.keys.retainAll(unchanged)
        nodes = updated.toMutableList()
        updateRefreshUi()
        SessionState.log("节点列表已更新（不记录凭证）"); true
    } catch (_: Exception) { toast("保存失败，原数据未更改"); false }
    private fun editor(existing: Node?) {
        if(!editable()) return
        val n = existing ?: Node()
        val form = column().apply { setPadding(dp(20), dp(8), dp(20), dp(20)) }
        fun field(label: String, value: String, secret: Boolean = false): EditText {
            form.addView(text(label, 12f, muted, true))
            val edit = EditText(this).apply {
                setText(value); textSize=15f; setSingleLine(true)
                inputType = if(secret) InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD else InputType.TYPE_CLASS_TEXT
                importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            }
            if (!secret) {
                form.addView(edit, LinearLayout.LayoutParams(-1, dp(48)))
                return edit
            }
            // 密码字段：右侧加明文/密文切换按钮
            val pwd = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD
            val vis = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD
            val toggle = TextView(this).apply {
                text = "显示"; textSize = 13f; setTextColor(blue); gravity = Gravity.CENTER
                setPadding(dp(12), 0, dp(8), 0)
                setOnClickListener {
                    edit.inputType = if (edit.inputType == pwd) vis else pwd
                    edit.setSelection(edit.text.length)
                    text = if (edit.inputType == pwd) "显示" else "隐藏"
                }
            }
            form.addView(LinearLayout(this).apply {
                orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL
                addView(edit, LinearLayout.LayoutParams(0, dp(48), 1f))
                addView(toggle, LinearLayout.LayoutParams(-2, dp(48)))
            }, LinearLayout.LayoutParams(-1, -2))
            return edit
        }
        val name = field("名称", n.name)
        form.addView(text("协议", 12f, muted, true))
        val protocol = Spinner(this).apply { adapter=ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, protocols); setSelection(protocols.indexOf(n.protocol)) }; form.addView(protocol)
        val host = field("服务器 / 转发目标", n.host)
        val port = field("远端端口", n.port.toString()).apply { inputType=InputType.TYPE_CLASS_NUMBER }
        val credential = field("UUID / 密码（SOCKS、HTTP 无认证时留空）", n.credential, true)
        val username = field("SOCKS / HTTP 用户名", n.username)
        val method = field("SS 加密方式 / VMess security", n.method)
        form.addView(text("传输协议", 12f, muted, true))
        val networks = listOf("tcp", "ws", "grpc")
        val network = Spinner(this).apply { adapter=ArrayAdapter(this@MainActivity, android.R.layout.simple_spinner_dropdown_item, networks); setSelection(networks.indexOf(n.network)) }; form.addView(network)
        val tls = Switch(this).apply { text="TLS · 始终验证证书"; isChecked=n.tls; minHeight=dp(48) }; form.addView(tls)
        val sni = field("TLS SNI（空则使用服务器地址）", n.sni)
        val path = field("WebSocket 路径 / gRPC serviceName", n.path)
        val wsHost = field("WebSocket Host", n.wsHost)
        val flow = field("VLESS flow（可选 xtls-rprx-vision）", n.flow)
        val listenPort = field("仅 dokodemo-door：本机监听端口", n.listenPort.toString()).apply { inputType=InputType.TYPE_CLASS_NUMBER }
        val enabled = Switch(this).apply { text="启用 dokodemo-door 转发"; isChecked=n.enabled; minHeight=dp(48) }; form.addView(enabled)
        form.addView(text("暂不支持 Reality、SS 插件、QUIC、TCP 伪装头。无 TLS 的协议请自行确认传输安全性。", 12f, muted))
        val scroll = ScrollView(this).apply { addView(form) }
        val dialog = AlertDialog.Builder(this).setTitle(if(existing == null) "新建节点" else "编辑节点").setView(scroll).setNegativeButton("取消", null).setPositiveButton("保存", null).create()
        dialog.window?.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        dialog.setOnShowListener { dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
            try {
                val updated = Node(id=n.id, name=name.text.toString().trim(), protocol=protocol.selectedItem.toString(), host=host.text.toString().trim(), port=port.text.toString().toIntOrNull() ?: 0, credential=credential.text.toString(), username=username.text.toString(), method=method.text.toString().trim(), network=network.selectedItem.toString(), tls=tls.isChecked, sni=sni.text.toString().trim(), path=path.text.toString(), wsHost=wsHost.text.toString().trim(), flow=flow.text.toString().trim(), listenPort=listenPort.text.toString().toIntOrNull() ?: 0, enabled=enabled.isChecked).validate()
                if(persist(nodes.filter { it.id != updated.id } + updated)) {
                    if(store.selected.isEmpty() && updated.protocol != "dokodemo-door") store.selected = updated.id
                    dialog.dismiss(); render()
                }
            } catch (e: IllegalArgumentException) { toast(e.message ?: "字段错误") }
        } }
        dialog.show(); dialog.window?.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE)
    }
    private fun importChooser() {
        AlertDialog.Builder(this).setTitle("导入节点参数")
            .setItems(arrayOf("导入节点链接参数", "扫码添加")) { _, which ->
                when (which) {
                    0 -> importLinks()
                    1 -> if (editable()) startActivityForResult(Intent(this, ScanActivity::class.java), RC_SCAN)
                }
            }
            .setNegativeButton("取消", null).show()
    }
    private fun importLinks() {
        if(!editable()) return
        val input = EditText(this).apply { hint="每行一条分享链接"; minLines=4; maxLines=9; textSize=14f; inputType=InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_FLAG_MULTI_LINE; importantForAutofill=View.IMPORTANT_FOR_AUTOFILL_NO }
        val dialog = AlertDialog.Builder(this).setTitle("导入节点参数").setMessage("每行粘贴一条节点分享链接。支持 vmess / vless / trojan / ss / socks / http(s)；不支持的参数会拒绝导入，不静默丢弃。不会自动读取剪贴板。").setView(input).setNegativeButton("取消", null).setPositiveButton("导入", null).create()
        dialog.window?.addFlags(WindowManager.LayoutParams.FLAG_SECURE)
        dialog.setOnShowListener { dialog.getButton(AlertDialog.BUTTON_POSITIVE).setOnClickListener {
            try {
                val imported = LinkImporter.batch(input.text.toString())
                if(persist(nodes + imported)) { if(store.selected.isEmpty()) store.selected=imported.first().id; dialog.dismiss(); render(); toast("已导入 ${imported.size} 个节点") }
            } catch (e: IllegalArgumentException) { toast(e.message ?: "导入失败") }
        } }; dialog.show()
    }
    private fun toggle() {
        if(SessionState.active || SessionState.busy) {
            startService(Intent(this, ProxyVpnService::class.java).setAction(ProxyVpnService.ACTION_STOP))
            return
        }
        if(!NativeBridge.available()) { message("缺少原生依赖", "当前 APK 未安装兼容的 Xray / hev 核心。请运行项目的 Integrate-Native.ps1 并重新构建。本次不会建立代理连接，也不会显示假连接。"); return }
        val selected = store.selected
        if(nodes.none { it.id == selected && it.protocol != "dokodemo-door" }) { toast("请先选择出口节点"); return }
        val intent = VpnService.prepare(this)
        if(intent != null) startActivityForResult(intent, 42) else startVpn(selected)
    }
    private fun startVpn(nodeId: String) {
        startForegroundService(Intent(this, ProxyVpnService::class.java).setAction(ProxyVpnService.ACTION_CONNECT).putExtra(ProxyVpnService.EXTRA_NODE_ID, nodeId))
    }
    @Deprecated("Platform activity result API")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if(requestCode == RC_SCAN) {
            if(resultCode != RESULT_OK) return
            val link = data?.getStringExtra(ScanActivity.EXTRA_LINK).orEmpty()
            try {
                val node = LinkImporter.parse(link)
                if(persist(nodes + node)) {
                    if(store.selected.isEmpty()) store.selected = node.id
                    render(); toast("已添加节点 ${node.name}")
                }
            } catch (e: Exception) { toast(e.message ?: "二维码不是有效的节点链接") }
            return
        }
        if(requestCode == 42) { if(resultCode == RESULT_OK) startVpn(store.selected) else toast("未获得系统连接授权") }
    }
    private fun drawer() {
        val dialog = Dialog(this)
        val panel = column().apply { setPadding(dp(24), dp(40), dp(24), dp(24)); setBackgroundColor(Color.WHITE) }
        panel.addView(text("云梯pro", 30f, blue, true)); panel.addView(text("轻量 · 原生 · 自主连接", 13f, muted))
        listOf("节点", "分流", "日志", "设置", "关于").forEach { item -> panel.addView(button(item, item == page) { page=item; dialog.dismiss(); render() }) }
        dialog.setContentView(panel); dialog.window?.apply { setBackgroundDrawableResource(android.R.color.transparent); setLayout(minOf(dp(320), resources.displayMetrics.widthPixels-dp(40)), -1); setGravity(Gravity.START) }; dialog.show()
    }
    private var appFilterQuery = ""
    private var appFilterGeneration = 0
    private fun toggleFilterPackage(pkg: String, checked: Boolean) {
        val current = AppFilter.parsePackageList(store.preferences.getString(AppFilter.KEY_LIST, "")).toMutableSet()
        if (checked) current.add(pkg) else current.remove(pkg)
        store.preferences.edit().putString(AppFilter.KEY_LIST, AppFilter.serializePackageList(current)).apply()
    }
    private fun appRow(app: AppInfo, checked: Boolean): View {
        val row = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL; minimumHeight = dp(56); setPadding(dp(4), dp(2), dp(4), dp(2)) }
        row.addView(ImageView(this).apply { setImageDrawable(app.icon); contentDescription = app.label }, LinearLayout.LayoutParams(dp(40), dp(40)))
        val texts = column()
        texts.addView(text(app.label, 14f, ink).apply { setPadding(0, 0, 0, 0); maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END })
        texts.addView(text(app.packageName, 11f, muted).apply { setPadding(0, 0, 0, 0); maxLines = 1; ellipsize = android.text.TextUtils.TruncateAt.END })
        row.addView(texts, LinearLayout.LayoutParams(0, -2, 1f).apply { leftMargin = dp(12) })
        val box = CheckBox(this).apply { isChecked = checked; contentDescription = "选择 ${app.label}" }
        row.setOnClickListener { box.isChecked = !box.isChecked }
        box.setOnCheckedChangeListener { _, isChecked -> toggleFilterPackage(app.packageName, isChecked) }
        row.addView(box)
        return row
    }
    private fun appFilter() {
        val generation = ++appFilterGeneration
        content.addView(text("应用分流", 26f, ink, true))
        content.addView(card().apply {
            addView(text("按应用选择走代理的流量", 18f, ink, true))
            addView(text("白名单：仅勾选的应用走代理\n黑名单：勾选的应用直连，其余走代理\n更改模式或名单后需重新连接才会生效", 14f, muted))
        })
        if (SessionState.active || SessionState.busy) content.addView(text("代理连接中：更改将在重新连接后生效", 13f, blue, true))
        val mode = AppFilter.modeOf(store.preferences.getString(AppFilter.KEY_MODE, null))
        val modeRow = LinearLayout(this)
        listOf(AppFilter.MODE_OFF to "不启用", AppFilter.MODE_WHITELIST to "白名单", AppFilter.MODE_BLACKLIST to "黑名单").forEachIndexed { index, (value, label) ->
            modeRow.addView(button(label, mode == value) {
                store.preferences.edit().putString(AppFilter.KEY_MODE, value).apply()
                SessionState.log("分流模式已设置为「$label」")
                render()
            }, LinearLayout.LayoutParams(0, dp(52), 1f).apply { if (index > 0) leftMargin = dp(8) })
        }
        content.addView(modeRow)
        if (mode == AppFilter.MODE_OFF) {
            content.addView(card().apply {
                addView(text("应用分流未启用", 17f, ink, true))
                addView(text("当前所有应用按默认规则连接，不读取或显示设备应用列表。", 13f, muted))
            })
            return
        }
        val selectedCount = AppFilter.parsePackageList(
            store.preferences.getString(AppFilter.KEY_LIST, "")
        ).size
        content.addView(card().apply {
            addView(text(if (mode == AppFilter.MODE_WHITELIST) "白名单模式" else "黑名单模式", 17f, ink, true))
            addView(text(
                if (mode == AppFilter.MODE_WHITELIST)
                    "已选择 $selectedCount 个应用，仅这些应用走代理。游戏登录依赖的服务应用也需一并勾选。"
                else
                    "已选择 $selectedCount 个应用直连，其余应用走代理。",
                13f,
                muted
            ))
        })
        val rows = column()
        var loaded = false
        var apps = listOf<AppInfo>()
        fun rebuildRows() {
            rows.removeAllViews()
            if (!loaded) { rows.addView(text("正在读取应用列表…", 13f, muted)); return }
            val query = appFilterQuery.trim().lowercase()
            val selected = AppFilter.parsePackageList(store.preferences.getString(AppFilter.KEY_LIST, ""))
            apps.filter { query.isEmpty() || it.label.lowercase().contains(query) || it.packageName.lowercase().contains(query) }
                .forEach { rows.addView(appRow(it, it.packageName in selected)) }
            if (rows.childCount == 0) rows.addView(text("没有匹配的应用", 13f, muted))
        }
        val search = EditText(this).apply {
            hint = "搜索应用名或包名"; textSize = 14f; setSingleLine(true)
            inputType = InputType.TYPE_CLASS_TEXT
            importantForAutofill = View.IMPORTANT_FOR_AUTOFILL_NO
            setText(appFilterQuery); setSelection(appFilterQuery.length)
            addTextChangedListener(object : TextWatcher {
                override fun afterTextChanged(s: Editable?) { appFilterQuery = s?.toString().orEmpty(); rebuildRows() }
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) {}
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) {}
            })
        }
        content.addView(search, LinearLayout.LayoutParams(-1, dp(48)))
        content.addView(rows)
        rebuildRows()
        Thread({
            val inventory = AppInventory(this).installedApps()
            handler.post {
                if (isDestroyed || page != "分流" || generation != appFilterGeneration) return@post
                apps = inventory; loaded = true
                rebuildRows()
            }
        }, "app-inventory").apply { isDaemon = true }.start()
    }
    private fun logs() {
        content.addView(text("运行日志", 26f, ink, true)); content.addView(text("仅保留本次进程最近 200 条事件；不记录节点地址、密码、UUID 或原始核心错误。", 13f, muted))
        content.addView(button("刷新") { render() }); content.addView(button("清空日志") { SessionState.clear(); render() })
        content.addView(card().apply { addView(text(SessionState.logs().ifEmpty { "暂无事件" }, 13f, muted).apply { typeface=Typeface.MONOSPACE }) })
    }
    private fun settings() {
        content.addView(text("连接设置", 26f, ink, true))
        content.addView(card().apply {
            addView(text("安全默认值", 18f, ink, true))
            addView(text("TLS 证书验证始终开启\nIPv4 全局代理 / IPv6 阻断\nDNS 由 hev 映射并经 SOCKS 传递域名\n随机本机 SOCKS 端口 + 每次随机认证\n本应用直连，不经过代理以避免回环\n关闭备份与截图，凭证加密持久化", 14f, muted))
        })
        val protect = Switch(this).apply {
            text="禁止截图与最近任务预览"; minHeight=dp(56); isChecked=store.preferences.getBoolean("secureScreen", true)
            setOnCheckedChangeListener { _, checked -> store.preferences.edit().putBoolean("secureScreen", checked).apply(); if(checked) window.addFlags(WindowManager.LayoutParams.FLAG_SECURE) else window.clearFlags(WindowManager.LayoutParams.FLAG_SECURE) }
        }; content.addView(protect)
        content.addView(text("关闭截图保护可能暴露节点信息。连接中请先断开再编辑。HTTP 出口不支持通用 UDP。无自动重连；网络变化后可手动重连。", 13f, muted))
        content.addView(button("检查原生核心") { message("原生核心检查", NativeBridge.availability()) })
    }
    private fun about() {
        content.addView(text("云梯pro", 28f, ink, true))
        content.addView(text("Kotlin 原生 Android 代理工具", 14f, muted))
        content.addView(card().apply { addView(text("免责协议", 18f, ink, true)); addView(text(disclaimerText, 14f, muted)) })
        content.addView(card().apply { addView(text("独立实现 UI 与业务代码", 18f, ink, true)); addView(text("交互参考 Kitsunebi，不复制其未知许可源码。\nXray-core：MPL-2.0\nAndroidLibXrayLite：LGPL-3.0\nhev-socks5-tunnel：MIT\n\n原生依赖安装后请随发布保留许可证、对应源码获取渠道及 LGPL 重链接条件。", 14f, muted)) })
        listOf("AndroidLibXrayLite.txt", "hev.txt", "Xray.txt").forEach { file -> content.addView(button("查看 $file") { val license = runCatching { assets.open("licenses/$file").bufferedReader().use { it.readText() } }.getOrDefault("尚未下载许可证，请运行原生依赖集成脚本。"); message("开源许可", license) }) }
        content.addView(text("隧道运行不等于远端节点可用。请仅连接你有权使用的服务器。", 13f, muted))
    }
    private fun showDisclaimerConsent() {
        AlertDialog.Builder(this).setTitle("免责协议").setMessage(disclaimerText)
            .setPositiveButton("同意并继续") { _, _ -> store.preferences.edit().putBoolean("disclaimerAccepted", true).apply() }
            .setNegativeButton("退出") { _, _ -> finish() }
            .setCancelable(false).show()
    }
    companion object {
        private const val RC_SCAN = 43
        private const val disclaimerText = "本软件为网络代理工具，仅供学习、研究及合法网络访问使用。\n\n一、用户须遵守所在国家或地区的法律法规，严禁将本软件用于任何违法违规用途。\n\n二、本软件不内置、不提供任何服务器节点，用户自行配置节点并对其来源与合法性负责。\n\n三、使用本软件产生的一切行为及后果由用户自行承担，与本软件作者无关；作者不承担任何直接或间接责任。\n\n四、继续使用本软件即表示已阅读并同意以上全部条款。"
    }
    private fun message(title: String, body: String) { AlertDialog.Builder(this).setTitle(title).setMessage(body).setPositiveButton("知道了", null).show() }
    private fun toast(value: String) { Toast.makeText(this, value, Toast.LENGTH_LONG).show() }
}
