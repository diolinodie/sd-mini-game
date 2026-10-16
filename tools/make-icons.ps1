# 產生分類圖示、動作清單圖示、外掛圖示，以及 Marketplace 的 288 商品圖示
param([string]$Root = (Split-Path $PSScriptRoot -Parent))
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$img = Join-Path $Root "mini_game\Images"
$actions = Join-Path $img "actions"
$market = Join-Path $Root "marketplace"
New-Item -ItemType Directory -Force $actions, $market | Out-Null

$White = [System.Drawing.Color]::White
$Clear = [System.Drawing.Color]::Transparent

function New-Canvas([int]$size, [scriptblock]$draw, [string]$path, $bg = $null) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear($Clear)
    if ($bg) { & $bg $g $size }
    $g.ScaleTransform($size / 40.0, $size / 40.0)   # 所有圖都在 40x40 的座標裡畫
    & $draw $g
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function Pen([double]$w) {
    $p = New-Object System.Drawing.Pen $White, ([single]$w)
    $p.StartCap = 'Round'; $p.EndCap = 'Round'; $p.LineJoin = 'Round'
    $p
}
function Brush($c) { New-Object System.Drawing.SolidBrush $c }

function RoundRect([double]$x, [double]$y, [double]$w, [double]$h, [double]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($r * 2)
    $p.AddArc([single]$x, [single]$y, $d, $d, 180, 90)
    $p.AddArc([single]($x + $w - $d), [single]$y, $d, $d, 270, 90)
    $p.AddArc([single]($x + $w - $d), [single]($y + $h - $d), $d, $d, 0, 90)
    $p.AddArc([single]$x, [single]($y + $h - $d), $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}

# 手把：機身 + 兩個握把，十字鍵和兩顆按鈕挖空或上色
function Draw-Pad($g, $body, $cross, $btnA, $btnB) {
    $path = RoundRect 3 11 34 15 7.5
    $path.AddEllipse(3, 15, 13, 15)
    $path.AddEllipse(24, 15, 13, 15)
    $path.FillMode = 'Winding'
    $g.FillPath((Brush $body), $path)

    $old = $g.CompositingMode
    if ($cross -eq $Clear) { $g.CompositingMode = 'SourceCopy' }
    $g.FillRectangle((Brush $cross), 8.5, 15.5, 2.6, 9)
    $g.FillRectangle((Brush $cross), 5.3, 18.7, 9, 2.6)
    $g.FillEllipse((Brush $btnA), 26.5, 14.5, 4.6, 4.6)
    $g.FillEllipse((Brush $btnB), 29.5, 19.5, 4.6, 4.6)
    $g.CompositingMode = $old
}

$icons = @{
    categoryIcon = { param($g) Draw-Pad $g $White $Clear $Clear $Clear }

    breakout = { param($g)
        $b = Brush $White
        foreach ($x in 3, 15, 27) { $g.FillRectangle($b, [single]$x, 5, 10, 5) }
        foreach ($x in 9, 21) { $g.FillRectangle($b, [single]$x, 12, 10, 5) }
        $g.FillEllipse($b, 21, 23, 5, 5)
        $g.FillRectangle($b, 11, 32, 18, 4)
    }

    snake = { param($g)
        $p = Pen 5
        $p.StartCap = 'Square'; $p.EndCap = 'Square'; $p.LineJoin = 'Miter'
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF 6, 33), (New-Object System.Drawing.PointF 19, 33),
            (New-Object System.Drawing.PointF 19, 21), (New-Object System.Drawing.PointF 31, 21),
            (New-Object System.Drawing.PointF 31, 12))
        $g.DrawLines($p, $pts)
        $g.FillRectangle((Brush $White), 6, 6, 6, 6)
    }

    foosball = { param($g)
        $p = Pen 2.5
        $g.DrawRectangle($p, 3, 8, 34, 24)
        $g.DrawLine($p, 20, 8, 20, 32)
        $g.DrawEllipse($p, 14.5, 14.5, 11, 11)
    }

    racer = { param($g)
        # 方向盤
        $p = Pen 3.5
        $g.DrawEllipse($p, 4, 4, 32, 32)
        $g.DrawLine($p, 5.5, 18, 15, 20)
        $g.DrawLine($p, 34.5, 18, 25, 20)
        $g.DrawLine($p, 20, 25, 20, 34)
        $g.FillEllipse((Brush $White), 14, 14, 12, 12)
    }

    racer4p = { param($g)
        # 方格旗
        $g.DrawLine((Pen 3), 8, 5, 8, 36)
        $b = Brush $White
        for ($r = 0; $r -lt 3; $r++) {
            for ($c = 0; $c -lt 4; $c++) {
                if (($r + $c) % 2 -eq 0) { $g.FillRectangle($b, [single](9.5 + $c * 6.5), [single](6 + $r * 5.5), 6.5, 5.5) }
            }
        }
        $g.DrawRectangle((Pen 1.5), 9.5, 6, 26, 16.5)
    }

    racermap = { param($g)
        # 路線 + 起點 + 終點圖釘
        $p = Pen 3
        $g.DrawBezier($p, 8, 33, 30, 33, 6, 18, 26, 15)
        $g.FillEllipse((Brush $White), 4, 29, 8, 8)
        $g.FillPolygon((Brush $White), [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF 24.2, 11.5), (New-Object System.Drawing.PointF 33.8, 11.5),
            (New-Object System.Drawing.PointF 29, 19.5)))
        $g.DrawEllipse((Pen 3), 24.5, 4.5, 9, 9)
    }
}

# 分類圖示 28 / 56
New-Canvas 28 $icons.categoryIcon (Join-Path $img "categoryIcon.png")
New-Canvas 56 $icons.categoryIcon (Join-Path $img "categoryIcon@2x.png")

# 動作清單圖示 20 / 40
foreach ($name in 'breakout', 'snake', 'foosball', 'racer', 'racer4p', 'racermap') {
    New-Canvas 20 $icons[$name] (Join-Path $actions "$name.png")
    New-Canvas 40 $icons[$name] (Join-Path $actions "$name@2x.png")
}

# 外掛圖示 256 / 512（彩色，跟原本的手把同色）
$Blue = [System.Drawing.Color]::FromArgb(0x43, 0xC9, 0xFF)
$Red = [System.Drawing.Color]::FromArgb(0xFF, 0x4C, 0x4C)
$Green = [System.Drawing.Color]::FromArgb(0x5A, 0xD6, 0x5A)
$colorPad = { param($g) Draw-Pad $g $Blue $White $Red $Green }
New-Canvas 256 $colorPad (Join-Path $img "pluginIcon.png")
New-Canvas 512 $colorPad (Join-Path $img "pluginIcon@2x.png")

# Marketplace 商品圖示 288（深色底）
$navy = { param($g, $size)
    $g.FillRectangle((New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.Point 0, 0), (New-Object System.Drawing.Point 0, $size), ([System.Drawing.Color]::FromArgb(0x2A, 0x2F, 0x5A)), ([System.Drawing.Color]::FromArgb(0x14, 0x17, 0x30))), 0, 0, $size, $size)
}
New-Canvas 288 { param($g) $g.TranslateTransform(2, 1.5); $g.ScaleTransform(0.9, 0.9); & $colorPad $g } (Join-Path $market "icon-288.png") $navy

"done"
