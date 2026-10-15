package cloud.yimeng.kitsune

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.AtomicFile
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

class Store(private val context: Context) {
    private val file = AtomicFile(File(context.filesDir, "nodes.enc"))
    val preferences = context.getSharedPreferences("settings", Context.MODE_PRIVATE)
    var selected: String
        get() = preferences.getString("selected", "").orEmpty()
        set(v) {
            check(preferences.edit().putString("selected", v).commit()) { "无法持久化所选节点" }
        }
    private fun key(): SecretKey {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey("kitsune.nodes", null) as? SecretKey)?.let { return it }
        return KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").apply {
            init(KeyGenParameterSpec.Builder("kitsune.nodes", KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).build())
        }.generateKey()
    }
    fun load(): MutableList<Node> {
        if (!file.baseFile.exists()) return mutableListOf()
        val bytes = file.readFully()
        require(bytes.size > 28) { "节点文件损坏" }
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, bytes.copyOfRange(0, 12))) }
        val j = JSONObject(String(cipher.doFinal(bytes.copyOfRange(12, bytes.size)), Charsets.UTF_8))
        require(j.getInt("version") == 1)
        val a = j.getJSONArray("nodes")
        return (0 until a.length()).map { Node.from(a.getJSONObject(it)) }.toMutableList()
    }
    fun save(nodes: List<Node>) {
        require(nodes.size <= 1000) { "最多保存 1000 个节点" }
        val a = JSONArray().apply { nodes.forEach { put(it.validate().json()) } }
        val bytes = JSONObject().put("version", 1).put("nodes", a).toString().toByteArray(Charsets.UTF_8)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding").apply { init(Cipher.ENCRYPT_MODE, key()) }
        val stream = file.startWrite()
        try { stream.write(cipher.iv + cipher.doFinal(bytes)); file.finishWrite(stream) } catch (e: Exception) { file.failWrite(stream); throw e }
    }
}

object SessionState {
    @Volatile var state = "未连接"
        private set
    @Volatile var active = false
        private set
    @Volatile var sessionId = 0L
        private set
    @Volatile var busy = false
        private set
    private val logs = ArrayDeque<String>()
    private val coordinator = SessionSwitchCoordinator()

    @Synchronized fun request(nodeId: String): SessionSwitchCoordinator.Request {
        val current = coordinator.latest()
        if (active && current.nodeId == nodeId) return current
        val request = coordinator.request(nodeId)
        busy = true
        state = if (active) "正在切换节点" else "正在连接"
        log(state)
        return request
    }
    @Synchronized fun requestStop(): SessionSwitchCoordinator.Request {
        val request = coordinator.requestStop()
        busy = active || busy
        state = if (busy) "正在停止" else "未连接"
        log(state)
        return request
    }
    @Synchronized fun latestRequest() = coordinator.latest()
    @Synchronized fun isLatest(request: SessionSwitchCoordinator.Request) = coordinator.isLatest(request)
    @Synchronized fun begin(request: SessionSwitchCoordinator.Request): Long? {
        if (!coordinator.isLatest(request) || request.nodeId == null || active) return null
        sessionId++
        active = true
        busy = false
        state = "正在初始化"
        log(state)
        return sessionId
    }
    @Synchronized fun update(ownerSessionId: Long, message: String) {
        if (ownerSessionId != sessionId || !active) return
        state = message
        log(message)
    }
    @Synchronized fun finish(ownerSessionId: Long, message: String) {
        if (ownerSessionId != sessionId) return
        active = false
        busy = false
        state = message
        log(message)
    }
    @Synchronized fun stopped(message: String) {
        active = false
        busy = false
        state = message
        log(message)
    }
    @Synchronized fun log(message: String) {
        val time = java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.ROOT).format(java.util.Date())
        logs.addFirst("$time  $message"); while(logs.size > 200) logs.removeLast()
        android.util.Log.d("YunTiPro", message)
    }
    @Synchronized fun logs() = logs.joinToString("\n\n")
    @Synchronized fun clear() = logs.clear()
}
