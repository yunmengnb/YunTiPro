# 生成应用图标：蓝色圆角方块 + 白云 + 上升箭头，输出多尺寸 ICO（PNG 压缩，Win7+ 兼容）
$ErrorActionPreference = "Stop"
$PSScriptRootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $PSScriptRootDir
$AssetsDir = Join-Path $ProjectDir "assets"
New-Item -ItemType Directory -Force -Path $AssetsDir | Out-Null

Add-Type -AssemblyName System.Drawing

function New-RoundedRectPath($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon($size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # 背景：蓝色渐变圆角方块
    $bgPath = New-RoundedRectPath (8 * $s) (8 * $s) (240 * $s) (240 * $s) (54 * $s)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, (256 * $s))),
        [System.Drawing.Color]::FromArgb(0x3B, 0x82, 0xF6),
        [System.Drawing.Color]::FromArgb(0x1D, 0x4E, 0xD8))
    $g.FillPath($bgBrush, $bgPath)

    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)

    # 上升箭头：从云中向上
    $arrow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cx = 128 * $s
    $half = 13 * $s          # 箭杆半宽
    $headHalf = 40 * $s      # 箭头半宽
    $top = 52 * $s
    $neck = 106 * $s
    $bottom = 172 * $s
    $arrow.AddPolygon([System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF($cx, $top)),
        (New-Object System.Drawing.PointF($cx + $headHalf, $neck)),
        (New-Object System.Drawing.PointF($cx + $half, $neck)),
        (New-Object System.Drawing.PointF($cx + $half, $bottom)),
        (New-Object System.Drawing.PointF($cx - $half, $bottom)),
        (New-Object System.Drawing.PointF($cx - $half, $neck)),
        (New-Object System.Drawing.PointF($cx - $headHalf, $neck))
    ))
    $g.FillPath($white, $arrow)

    # 白云：底部云团遮住箭杆下段
    $cloud = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cloud.AddEllipse((56 * $s), (150 * $s), (72 * $s), (56 * $s))
    $cloud.AddEllipse((104 * $s), (128 * $s), (88 * $s), (72 * $s))
    $cloud.AddEllipse((152 * $s), (152 * $s), (56 * $s), (50 * $s))
    $cloud.AddRectangle((60 * $s), (168 * $s), (144 * $s), (38 * $s))
    $g.FillPath($white, $cloud)

    $g.Dispose()
    $bgBrush.Dispose(); $white.Dispose(); $bgPath.Dispose(); $arrow.Dispose(); $cloud.Dispose()
    return $bmp
}

# 生成各尺寸 PNG 字节
$sizes = @(256, 64, 48, 32, 24, 16)
$pngs = @{}
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs[$sz] = $ms.ToArray()
    $bmp.Dispose(); $ms.Dispose()
    if ($sz -eq 256) { [System.IO.File]::WriteAllBytes((Join-Path $AssetsDir "app.png"), $pngs[$sz]) }
}

# 打包 ICO（全部 PNG 压缩条目）
$icoPath = Join-Path $AssetsDir "app.ico"
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($sz in $sizes) {
    $data = $pngs[$sz]
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$data.Length); $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($sz in $sizes) { $bw.Write($pngs[$sz]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$bw.Dispose(); $ms.Dispose()

Write-Host "图标已生成: $icoPath 与 assets\app.png"
