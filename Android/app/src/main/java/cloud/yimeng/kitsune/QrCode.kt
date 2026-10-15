package cloud.yimeng.kitsune

import android.graphics.Bitmap
import android.graphics.Color
import com.google.zxing.BarcodeFormat
import com.google.zxing.BinaryBitmap
import com.google.zxing.DecodeHintType
import com.google.zxing.EncodeHintType
import com.google.zxing.MultiFormatReader
import com.google.zxing.RGBLuminanceSource
import com.google.zxing.common.HybridBinarizer
import com.google.zxing.qrcode.QRCodeWriter
import com.google.zxing.qrcode.decoder.ErrorCorrectionLevel

/** 二维码生成与识别，分享导出与扫码导入共用。 */
object QrCode {
    private val qrOnly = mapOf(DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE))

    /** 把文本编码为 QR 位图（容错等级 M，含静区），边长 sizePx。 */
    fun encode(text: String, sizePx: Int): Bitmap {
        require(text.isNotEmpty() && sizePx > 0) { "二维码参数错误" }
        val matrix = QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, sizePx, sizePx,
            mapOf(EncodeHintType.ERROR_CORRECTION to ErrorCorrectionLevel.M))
        val pixels = IntArray(sizePx * sizePx)
        for (y in 0 until sizePx) {
            for (x in 0 until sizePx) pixels[y * sizePx + x] = if (matrix[x, y]) Color.BLACK else Color.WHITE
        }
        return Bitmap.createBitmap(pixels, sizePx, sizePx, Bitmap.Config.ARGB_8888)
    }

    /** 识别位图中的 QR 内容，未识别到返回 null。 */
    fun decode(bitmap: Bitmap): String? {
        val width = bitmap.width
        val height = bitmap.height
        val pixels = IntArray(width * height)
        bitmap.getPixels(pixels, 0, width, 0, 0, width, height)
        return decodePixels(pixels, width, height)
    }

    /** 像素级识别核心（纯计算，单元测试直接覆盖）。多策略级联以覆盖相册实拍/截图场景。 */
    fun decodePixels(pixels: IntArray, width: Int, height: Int): String? {
        val hints = mapOf(
            DecodeHintType.POSSIBLE_FORMATS to listOf(BarcodeFormat.QR_CODE),
            DecodeHintType.TRY_HARDER to true
        )
        val sources = mutableListOf<RGBLuminanceSource>()
        sources += RGBLuminanceSource(width, height, pixels)
        // 二维码在画面中占比过小时，中心裁切后再识别（截图/实拍常见）
        if (width >= 800 || height >= 800) {
            val cw = width * 3 / 4; val ch = height * 3 / 4
            val cx = (width - cw) / 2; val cy = (height - ch) / 2
            val crop = IntArray(cw * ch)
            for (y in 0 until ch) System.arraycopy(pixels, (cy + y) * width + cx, crop, y * cw, cw)
            sources += RGBLuminanceSource(cw, ch, crop)
        }
        for (src in sources) {
            // 依次尝试：自适应二值化 → 全局直方图 → 反色（深色背景截图）
            for (bitmap2 in listOf(
                BinaryBitmap(HybridBinarizer(src)),
                BinaryBitmap(com.google.zxing.common.GlobalHistogramBinarizer(src)),
                BinaryBitmap(HybridBinarizer(src.invert()))
            )) {
                try {
                    return MultiFormatReader().apply { setHints(hints) }.decode(bitmap2).text
                } catch (_: Exception) { }
            }
        }
        // 压缩噪点场景：2x2 块平均降采样，单像素噪点被邻居平均吸收
        var dw = width; var dh = height; var dp = pixels
        repeat(2) {
            if (dw < 400 || dh < 400) return null
            val nw = dw / 2; val nh = dh / 2
            val np = IntArray(nw * nh)
            for (y in 0 until nh) for (x in 0 until nw) {
                var sum = 0
                for (dy in 0..1) for (dx in 0..1) sum += dp[(y * 2 + dy) * dw + (x * 2 + dx)] and 0xFF
                val g = sum / 4
                np[y * nw + x] = 0xFF000000.toInt() or (g shl 16) or (g shl 8) or g
            }
            try {
                val src = RGBLuminanceSource(nw, nh, np)
                return MultiFormatReader().apply { setHints(hints) }
                    .decode(BinaryBitmap(HybridBinarizer(src))).text
            } catch (_: Exception) { }
            dw = nw; dh = nh; dp = np
        }
        return null
    }
}
