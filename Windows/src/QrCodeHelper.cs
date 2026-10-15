using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using QRCoder;
using ZXing;

namespace YunTiPro
{
    /// <summary>
    /// 二维码生成与识别。生成依赖 QRCoder（纯托管），识别依赖 ZXing.Net。
    /// 仅使用 System.Drawing，控制台测试与 WPF 界面可共用。
    /// </summary>
    public static class QrCodeHelper
    {
        /// <summary>识别前图片最长边上限，超出则等比压缩以控制耗时</summary>
        public const int MaxDecodeEdge = 2000;

        /// <summary>生成白底黑块二维码位图，边长控制在约 300px（调用方负责 Dispose）</summary>
        public static Bitmap GenerateQrBitmap(string content)
        {
            if (string.IsNullOrEmpty(content)) throw new ArgumentException("二维码内容为空", "content");
            using (var gen = new QRCodeGenerator())
            using (var data = gen.CreateQrCode(content, QRCodeGenerator.ECCLevel.M))
            using (var qr = new QRCode(data))
            {
                // ModuleMatrix 含四周静区；按模块数反推每模块像素，使总边长接近 300
                int modules = data.ModuleMatrix.Count;
                int ppm = Math.Max(1, 300 / modules);
                return qr.GetGraphic(ppm);
            }
        }

        /// <summary>从位图识别二维码文本；识别不到返回 null，不抛异常</summary>
        public static string DecodeFromImage(Bitmap bmp)
        {
            if (bmp == null) throw new ArgumentNullException("bmp");
            Bitmap work = DownscaleIfNeeded(bmp, MaxDecodeEdge);
            bool owns = !ReferenceEquals(work, bmp);
            try
            {
                var reader = new BarcodeReader
                {
                    AutoRotate = true,
                    Options = new ZXing.Common.DecodingOptions
                    {
                        TryHarder = true,
                        PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                    }
                };
                var result = reader.Decode(work);
                return result != null ? result.Text : null;
            }
            finally
            {
                if (owns) work.Dispose();
            }
        }

        /// <summary>最长边超过 maxEdge 时返回等比缩小的新位图，否则返回原图引用</summary>
        public static Bitmap DownscaleIfNeeded(Bitmap bmp, int maxEdge)
        {
            if (bmp == null) throw new ArgumentNullException("bmp");
            int w = bmp.Width, h = bmp.Height;
            int max = Math.Max(w, h);
            if (max <= maxEdge || max <= 0) return bmp;
            double scale = (double)maxEdge / max;
            int nw = Math.Max(1, (int)Math.Round(w * scale));
            int nh = Math.Max(1, (int)Math.Round(h * scale));
            var dst = new Bitmap(nw, nh);
            using (var g = Graphics.FromImage(dst))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(bmp, 0, 0, nw, nh);
            }
            return dst;
        }
    }
}
