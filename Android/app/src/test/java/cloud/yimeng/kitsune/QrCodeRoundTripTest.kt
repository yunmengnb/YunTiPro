package cloud.yimeng.kitsune

import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.common.BitMatrix
import com.google.zxing.DecodeHintType
import com.google.zxing.EncodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.RGBLuminanceSource
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel
import org.junit.Assert.*
import org.junit.Test

/**
 * 二维码生成-识别回环：ShareExporter 导出链接 → QRCodeWriter 生成矩阵 →
 * 手写 int 像素数组（JVM 无 android.graphics.Bitmap / java.awt）→ RGBLuminanceSource
 * 解码 → LinkImporter 解析，断言节点字段一致。
 */
class QrCodeRoundTripTest {
    private val uuid = "4b418bb7-9037-433a-d7f0-c34330d73da7"

    private fun encodeMatrix(text: String, size: Int): BitMatrix =
        QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, size, size,
            mapOf(EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M))

    private fun decodeMatrix(matrix: BitMatrix): String {
        val w = matrix.width; val h = matrix.height
        val pixels = IntArray(w * h) { i -> if (matrix[i % w, i / w]) 0xFF000000.toInt() else 0xFFFFFFFF.toInt() }
        val source = RGBLuminanceSource(w, h, pixels)
        return MultiFormatReader().apply {
            setHints(mapOf(DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE)))
        }.decode(BinaryBitmap(HybridBinarizer(source))).text
    }

    private fun qrRoundTrip(n: Node): Node {
        val url = ShareExporter.export(n)
        val decoded = decodeMatrix(encodeMatrix(url, 512))
        assertEquals("二维码解码内容应与导出链接一致", url, decoded)
        return LinkImporter.parse(decoded)
    }

    @Test fun vlessQrRoundTrip() {
        val n = Node(name = "QR 专线+01", protocol = "vless", host = "qr.example.com", port = 443,
            credential = uuid, network = "ws", tls = true, sni = "s.example.com",
            path = "/ray", wsHost = "cdn.example.com", flow = "")
        val m = qrRoundTrip(n)
        assertEquals(n.name, m.name); assertEquals("vless", m.protocol)
        assertEquals(n.host, m.host); assertEquals(443, m.port)
        assertEquals(uuid, m.credential); assertEquals("ws", m.network)
        assertTrue(m.tls); assertEquals("s.example.com", m.sni)
        assertEquals("/ray", m.path); assertEquals("cdn.example.com", m.wsHost)
    }

    @Test fun vmessQrRoundTrip() {
        val n = Node(name = "VMess QR", protocol = "vmess", host = "203.0.113.7", port = 10086,
            credential = uuid, network = "tcp", tls = true, sni = "v.example.com")
        val m = qrRoundTrip(n)
        assertEquals(n.name, m.name); assertEquals("vmess", m.protocol)
        assertEquals(n.host, m.host); assertEquals(10086, m.port)
        assertEquals(uuid, m.credential); assertEquals("tcp", m.network); assertTrue(m.tls)
    }

    @Test fun shadowsocksQrRoundTrip() {
        val n = Node(name = "SS 二维码", protocol = "shadowsocks", host = "ss.example.com", port = 8388,
            credential = "pass+word/1", method = "aes-256-gcm", tls = false)
        val m = qrRoundTrip(n)
        assertEquals(n.name, m.name); assertEquals("shadowsocks", m.protocol)
        assertEquals("aes-256-gcm", m.method); assertEquals("pass+word/1", m.credential)
        assertEquals(8388, m.port)
    }

    // ---------- 相册实拍/截图场景：大图小二维码、降采样、反色 ----------

    private fun matrixPixels(matrix: BitMatrix): IntArray {
        val w = matrix.width; val h = matrix.height
        return IntArray(w * h) { i -> if (matrix[i % w, i / w]) 0xFF000000.toInt() else 0xFFFFFFFF.toInt() }
    }

    @Test fun smallQrOnLargeCanvasDecodes() {
        // 模拟截图：二维码只占 2000x3000 画面中央一小块
        val url = ShareExporter.export(Node(name = "小码", protocol = "vless", host = "a.com", port = 443,
            credential = uuid, network = "tcp", tls = true))
        val m = encodeMatrix(url, 512)
        val cw = 2000; val ch = 3000
        val big = IntArray(cw * ch) { 0xFFFFFFFF.toInt() }
        val ox = (cw - 512) / 2; val oy = (ch - 512) / 2
        val px = matrixPixels(m)
        for (y in 0 until 512) System.arraycopy(px, y * 512, big, (oy + y) * cw + ox, 512)
        // 再按 2 倍降采样模拟相册大图加载
        val sw = cw / 2; val sh = ch / 2
        val small = IntArray(sw * sh) { i -> big[(i / sw) * 2 * cw + (i % sw) * 2] }
        val decoded = QrCode.decodePixels(small, sw, sh)
        assertEquals("大图小二维码应能识别", url, decoded)
    }

    @Test fun invertedQrDecodes() {
        // 深色背景截图（白块黑底）
        val url = ShareExporter.export(Node(name = "反色", protocol = "trojan", host = "t.com", port = 443, credential = "p", tls = true))
        val inv = matrixPixels(encodeMatrix(url, 512)).map { p -> if (p == 0xFF000000.toInt()) 0xFFFFFFFF.toInt() else 0xFF000000.toInt() }.toIntArray()
        assertEquals("反色二维码应能识别", url, QrCode.decodePixels(inv, 512, 512))
    }

    @Test fun jpegNoiseQrDecodes() {
        // 模拟 JPEG 压缩痕迹：模块边缘混入 2% 灰噪点
        val url = ShareExporter.export(Node(name = "噪点", protocol = "shadowsocks", host = "s.com", port = 8388,
            credential = "pw", method = "aes-256-gcm", tls = false))
        val px = matrixPixels(encodeMatrix(url, 512))
        val rnd = java.util.Random(42)
        for (i in px.indices) if (rnd.nextInt(50) == 0) px[i] = 0xFF808080.toInt()
        assertEquals("带噪点二维码应能识别", url, QrCode.decodePixels(px, 512, 512))
    }

    @Test fun trojanGrpcQrRoundTrip() {
        val n = Node(name = "T-gRPC", protocol = "trojan", host = "t.example.com", port = 443,
            credential = "p@ss:w+rd", tls = true, network = "grpc", path = "svc")
        val m = qrRoundTrip(n)
        assertEquals("trojan", m.protocol); assertEquals("p@ss:w+rd", m.credential)
        assertEquals("grpc", m.network); assertEquals("svc", m.path); assertTrue(m.tls)
    }
}
