# 生成应用图标 assets\app.ico：与安卓端一致的蓝色狐狸头（#2563EB），多尺寸 PNG 压缩 ICO（Win7+ 兼容）
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$ProjectDir = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$AssetsDir = Join-Path $ProjectDir "assets"
New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null

function Render-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # 与安卓端 ic_fox.xml 相同的矢量图形（48x48 视口，按比例缩放）
    $k = $size / 48.0
    function Pt([double]$x, [double]$y) { New-Object System.Drawing.PointF(($x * $k), ($y * $k)) }
    function Poly($points) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $p.AddPolygon([System.Drawing.PointF[]]($points | ForEach-Object { Pt $_[0] $_[1] }))
        return $p
    }

    # 蓝色狐狸头轮廓
    $blue = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 0x25, 0x63, 0xEB))
    $g.FillPath($blue, (Poly @(@(4,4),@(16,12),@(32,12),@(44,4),@(40,32),@(24,44),@(8,32))))

    # 白色眼部与鼻部
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $g.FillPath($white, (Poly @(@(12,21),@(21,26),@(18,30))))
    $g.FillPath($white, (Poly @(@(36,21),@(27,26),@(30,30))))
    $g.FillPath($white, (Poly @(@(20,34),@(28,34),@(24,38))))
    $g.Dispose()
    return $bmp
}

# 逐尺寸渲染并转 PNG 字节
$sizes = @(16, 32, 48, 256)
$pngs = @()
foreach ($s in $sizes) {
    $bmp = Render-Icon $s
    $png = New-Object System.IO.MemoryStream
    $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += ,$png.ToArray()
    $bmp.Dispose()
}

# 组装 ICO 文件
$icoPath = Join-Path $AssetsDir "app.ico"
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0)              # 保留
$bw.Write([uint16]1)              # 类型：图标
$bw.Write([uint16]$sizes.Count)   # 图像数量
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $pngs[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # 宽（0 表示 256）
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # 高
    $bw.Write([byte]0)              # 调色板
    $bw.Write([byte]0)              # 保留
    $bw.Write([uint16]1)            # 平面数
    $bw.Write([uint16]32)           # 位深
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($data in $pngs) { $bw.Write($data) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()
Write-Host "图标已生成: $icoPath ($sizes 尺寸)"
