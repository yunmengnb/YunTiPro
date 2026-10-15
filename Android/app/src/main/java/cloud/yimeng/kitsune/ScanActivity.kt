package cloud.yimeng.kitsune

import android.Manifest
import android.app.Activity
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.Color
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.util.Size
import android.view.Gravity
import android.view.View
import android.widget.Button
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Toast
import androidx.camera.core.CameraSelector
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import androidx.camera.core.Preview
import androidx.camera.core.resolutionselector.ResolutionSelector
import androidx.camera.core.resolutionselector.ResolutionStrategy
import androidx.camera.lifecycle.ProcessCameraProvider
import androidx.camera.view.PreviewView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.PlanarYUVLuminanceSource
import com.google.zxing.common.HybridBinarizer
import java.util.concurrent.Executor
import java.util.concurrent.Executors

/** 扫码添加节点：相机实时识别 + 相册选图识别，识别到有效节点链接后把原文带回 MainActivity 保存。 */
class ScanActivity : Activity(), LifecycleOwner {
    private val registry = LifecycleRegistry(this)
    override fun getLifecycle(): Lifecycle = registry
    private val mainHandler = Handler(Looper.getMainLooper())
    private val mainExecutor = Executor { r -> mainHandler.post(r) }
    private val analyzerExecutor = Executors.newSingleThreadExecutor()
    private val reader = MultiFormatReader().apply {
        setHints(mapOf(DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE)))
    }
    private var handled = false
    private var lastInvalid: String? = null
    private var lastInvalidAt = 0L

    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
    private fun toast(value: String) { Toast.makeText(this, value, Toast.LENGTH_LONG).show() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        registry.currentState = Lifecycle.State.CREATED
        val root = FrameLayout(this).apply { setBackgroundColor(Color.BLACK) }
        root.setOnApplyWindowInsetsListener { v, insets ->
            v.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom); insets
        }
        val previewView = PreviewView(this)
        root.addView(previewView, FrameLayout.LayoutParams(-1, -1))
        // 取景框
        root.addView(View(this).apply {
            background = GradientDrawable().apply { setColor(Color.TRANSPARENT); setStroke(dp(2), Color.WHITE); cornerRadius = dp(16).toFloat() }
        }, FrameLayout.LayoutParams(dp(240), dp(240), Gravity.CENTER))
        // 顶部标题栏：标题 + 关闭按钮
        val topBar = LinearLayout(this).apply {
            gravity = Gravity.CENTER_VERTICAL
            setBackgroundColor(Color.argb(0x80, 0, 0, 0))
            setPadding(dp(8), 0, dp(8), 0)
        }
        topBar.addView(TextView(this).apply {
            text = "扫码添加节点"; textSize = 17f; setTextColor(Color.WHITE); setTypeface(null, android.graphics.Typeface.BOLD)
            setPadding(dp(12), 0, 0, 0)
        }, LinearLayout.LayoutParams(0, -2, 1f))
        topBar.addView(Button(this).apply {
            text = "✕"; textSize = 20f; setTextColor(Color.WHITE); contentDescription = "关闭扫码"
            background = null; setOnClickListener { finish() }
        }, LinearLayout.LayoutParams(dp(48), dp(48)))
        root.addView(topBar, FrameLayout.LayoutParams(-1, dp(56), Gravity.TOP))
        // 右下角相册按钮
        root.addView(Button(this).apply {
            text = "相册"; textSize = 14f; setTextColor(Color.WHITE); isAllCaps = false
            contentDescription = "从相册选择二维码图片"
            background = GradientDrawable().apply { setColor(Color.argb(0xB3, 15, 23, 42)); cornerRadius = dp(14).toFloat() }
            setPadding(dp(20), 0, dp(20), 0)
            setOnClickListener { pickImage() }
        }, FrameLayout.LayoutParams(-2, dp(48), Gravity.BOTTOM or Gravity.END).apply { rightMargin = dp(24); bottomMargin = dp(24) })
        setContentView(root)
        this.previewView = previewView
        if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) startCamera()
        else requestPermissions(arrayOf(Manifest.permission.CAMERA), RC_CAMERA)
    }

    private lateinit var previewView: PreviewView

    override fun onStart() { super.onStart(); registry.currentState = Lifecycle.State.STARTED }
    override fun onResume() { super.onResume(); registry.currentState = Lifecycle.State.RESUMED }
    override fun onPause() { registry.currentState = Lifecycle.State.STARTED; super.onPause() }
    override fun onStop() { registry.currentState = Lifecycle.State.CREATED; super.onStop() }
    override fun onDestroy() {
        registry.currentState = Lifecycle.State.DESTROYED
        analyzerExecutor.shutdown()
        super.onDestroy()
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == RC_CAMERA) {
            if (grantResults.firstOrNull() == PackageManager.PERMISSION_GRANTED) startCamera()
            else { toast("未获得相机权限，无法扫码"); finish() }
        }
    }

    private fun startCamera() {
        val future = ProcessCameraProvider.getInstance(this)
        future.addListener({
            try {
                val provider = future.get()
                val preview = Preview.Builder().build().also { it.setSurfaceProvider(previewView.surfaceProvider) }
                val analysis = ImageAnalysis.Builder()
                    .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                    .setOutputImageFormat(ImageAnalysis.OUTPUT_IMAGE_FORMAT_YUV_420_888)
                    .setResolutionSelector(ResolutionSelector.Builder()
                        .setResolutionStrategy(ResolutionStrategy(Size(1280, 720), ResolutionStrategy.FALLBACK_RULE_CLOSEST_HIGHER_THEN_LOWER))
                        .build())
                    .build()
                analysis.setAnalyzer(analyzerExecutor) { image -> analyze(image) }
                provider.unbindAll()
                provider.bindToLifecycle(this, CameraSelector.DEFAULT_BACK_CAMERA, preview, analysis)
            } catch (e: Exception) {
                toast("无法启动相机"); finish()
            }
        }, mainExecutor)
    }

    /** 取 Y 平面灰度数据，按 rotationDegrees 旋转到正向，保证二维码以正确方向解码。 */
    private fun analyze(image: ImageProxy) {
        try {
            if (handled) return
            val plane = image.planes[0]
            if (plane.pixelStride != 1) return
            val w = image.width; val h = image.height; val stride = plane.rowStride
            val raw = ByteArray(stride * h)
            val buffer = plane.buffer.duplicate()
            buffer.clear()
            buffer.get(raw, 0, minOf(buffer.remaining(), raw.size))
            var lum = raw; var lw = w; var lh = h; var lstride = stride
            when (image.imageInfo.rotationDegrees) {
                90 -> {
                    lum = ByteArray(w * h)
                    for (y in 0 until w) for (x in 0 until h) lum[y * h + x] = raw[(h - 1 - x) * stride + y]
                    lw = h; lh = w; lstride = h
                }
                180 -> {
                    lum = ByteArray(w * h)
                    for (y in 0 until h) for (x in 0 until w) lum[y * w + x] = raw[(h - 1 - y) * stride + (w - 1 - x)]
                    lstride = w
                }
                270 -> {
                    lum = ByteArray(w * h)
                    for (y in 0 until w) for (x in 0 until h) lum[y * h + x] = raw[x * stride + (w - 1 - y)]
                    lw = h; lh = w; lstride = h
                }
            }
            val source = PlanarYUVLuminanceSource(lum, lstride, lh, 0, 0, lw, lh, false)
            val text = try {
                reader.decodeWithState(BinaryBitmap(HybridBinarizer(source)))?.text
            } catch (_: Exception) { null } finally { reader.reset() }
            if (!text.isNullOrEmpty()) mainHandler.post { onScanned(text) }
        } finally {
            image.close()
        }
    }

    /** 识别到二维码后的统一处理：有效则带回保存，无效则提示并继续扫（防抖避免连弹）。 */
    private fun onScanned(text: String) {
        if (handled || isFinishing) return
        try {
            LinkImporter.parse(text)
        } catch (e: Exception) {
            SessionState.log("扫码内容不是有效节点链接：" + text.take(60) + " / " + (e.message ?: "").take(80))
            val now = SystemClock.uptimeMillis()
            if (text != lastInvalid || now - lastInvalidAt > 2500) {
                lastInvalid = text; lastInvalidAt = now
                toast("二维码不是有效的节点链接")
            }
            return
        }
        handled = true
        SessionState.log("扫码识别成功，带回节点链接")
        setResult(RESULT_OK, Intent().putExtra(EXTRA_LINK, text))
        finish()
    }

    private fun pickImage() {
        startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).apply {
            addCategory(Intent.CATEGORY_OPENABLE); type = "image/*"
        }, RC_PICK)
    }

    @Deprecated("Platform activity result API")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data)
        if (requestCode == RC_PICK) {
            val uri = data?.data
            if (resultCode == RESULT_OK && uri != null) decodePicked(uri)
            else SessionState.log("相册选图未返回图片：resultCode=$resultCode")
        }
    }

    private fun decodePicked(uri: Uri) {
        SessionState.log("相册选图：$uri")
        Thread({
            val result = runCatching {
                val bitmap = loadScaled(uri, 2048)
                if (bitmap == null) "load:null"
                else try {
                    SessionState.log("相册图片加载成功：${bitmap.width}x${bitmap.height}")
                    QrCode.decode(bitmap) ?: "decode:null"
                } finally { bitmap.recycle() }
            }.getOrElse { "error:" + it.javaClass.simpleName + ":" + (it.message ?: "").take(80) }
            mainHandler.post {
                if (result == "load:null") { SessionState.log("相册识别失败：无法加载所选图片"); toast("无法读取所选图片") }
                else if (result == "decode:null") { SessionState.log("相册识别失败：图片中未找到二维码"); toast("未在图片中识别到节点二维码") }
                else if (result.startsWith("error:")) { SessionState.log("相册识别异常：$result"); toast("图片识别出错") }
                else onScanned(result)
            }
        }, "qr-decode").apply { isDaemon = true }.start()
    }

    /** 两级解码加载图片，采样到最长边不超过 maxEdge，避免大图 OOM。 */
    private fun loadScaled(uri: Uri, maxEdge: Int): Bitmap? {
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        // 注意：inJustDecodeBounds 模式下 decodeStream 恒返回 null，不能把 use 的返回值当流是否打开成功
        val probe = contentResolver.openInputStream(uri) ?: run {
            SessionState.log("相册图片流打开为空"); return null
        }
        probe.use { BitmapFactory.decodeStream(it, null, bounds) }
        SessionState.log("相册图片尺寸：${bounds.outWidth}x${bounds.outHeight} ${bounds.outMimeType}")
        if (bounds.outWidth <= 0 || bounds.outHeight <= 0) return null
        var sample = 1
        while (maxOf(bounds.outWidth, bounds.outHeight) / sample > maxEdge) sample *= 2
        val options = BitmapFactory.Options().apply {
            inSampleSize = sample
            // 固定全彩输出：低端机默认可能解码为 RGB_565，损失二维码边缘细节
            inPreferredConfig = Bitmap.Config.ARGB_8888
            // 禁止按屏幕密度缩放，避免二维码被重采样模糊
            inScaled = false
        }
        return contentResolver.openInputStream(uri)?.use { BitmapFactory.decodeStream(it, null, options) }
    }

    companion object {
        const val EXTRA_LINK = "link"
        private const val RC_CAMERA = 1
        private const val RC_PICK = 2
    }
}
