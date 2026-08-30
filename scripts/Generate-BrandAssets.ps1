param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\src\NexusLauncher.App\Assets")
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

try {
    Add-Type -AssemblyName System.Drawing.Common -ErrorAction Stop
}
catch {
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
}

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-NexusMark {
    param(
        [System.Drawing.Graphics]$Graphics,
        [float]$CenterX,
        [float]$CenterY,
        [float]$Span
    )

    $radius = $Span * 0.43
    $points = [System.Drawing.PointF[]]@(
        [System.Drawing.PointF]::new($CenterX, $CenterY - $radius),
        [System.Drawing.PointF]::new($CenterX + ($radius * 0.866), $CenterY - ($radius * 0.5)),
        [System.Drawing.PointF]::new($CenterX + ($radius * 0.866), $CenterY + ($radius * 0.5)),
        [System.Drawing.PointF]::new($CenterX, $CenterY + $radius),
        [System.Drawing.PointF]::new($CenterX - ($radius * 0.866), $CenterY + ($radius * 0.5)),
        [System.Drawing.PointF]::new($CenterX - ($radius * 0.866), $CenterY - ($radius * 0.5))
    )

    $fill = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(235, 31, 39, 58))
    $accent = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 123, 108, 255), [Math]::Max(1.4, $Span * 0.065))
    $accent.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $line = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 247, 249, 255), [Math]::Max(1.2, $Span * 0.052))
    $line.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $line.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $nodeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(255, 247, 249, 255))

    try {
        $Graphics.FillPolygon($fill, $points)
        $Graphics.DrawPolygon($accent, $points)

        $left = $CenterX - ($Span * 0.235)
        $right = $CenterX + ($Span * 0.235)
        $top = $CenterY - ($Span * 0.215)
        $bottom = $CenterY + ($Span * 0.215)
        $Graphics.DrawLine($line, $left, $top, $right, $bottom)
        $Graphics.DrawLine($line, $right, $top, $left, $bottom)

        $nodeRadius = [Math]::Max(1.4, $Span * 0.054)
        foreach ($point in @(
            @($left, $top), @($right, $top), @($left, $bottom), @($right, $bottom)
        )) {
            $Graphics.FillEllipse(
                $nodeBrush,
                [float]$point[0] - $nodeRadius,
                [float]$point[1] - $nodeRadius,
                $nodeRadius * 2,
                $nodeRadius * 2)
        }
    }
    finally {
        $fill.Dispose()
        $accent.Dispose()
        $line.Dispose()
        $nodeBrush.Dispose()
    }
}

function New-NexusIconBitmap {
    param([int]$Size)

    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)

        $padding = [Math]::Max(1, $Size * 0.045)
        $tilePath = New-RoundedRectanglePath $padding $padding ($Size - (2 * $padding)) ($Size - (2 * $padding)) ($Size * 0.21)
        $tileBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.RectangleF]::new(0, 0, $Size, $Size),
            [System.Drawing.Color]::FromArgb(255, 25, 31, 46),
            [System.Drawing.Color]::FromArgb(255, 8, 12, 20),
            [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
        $border = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(255, 52, 64, 90), [Math]::Max(1, $Size * 0.014))
        try {
            $graphics.FillPath($tileBrush, $tilePath)
            $graphics.DrawPath($border, $tilePath)
        }
        finally {
            $border.Dispose()
            $tileBrush.Dispose()
            $tilePath.Dispose()
        }

        Draw-NexusMark $graphics ($Size / 2) ($Size / 2) ($Size * 0.72)
        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

function Convert-BitmapToPngBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $stream = [System.IO.MemoryStream]::new()
    try {
        $Bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        return $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}

function Write-MultiResolutionIcon {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $frames = foreach ($size in $Sizes) {
        $bitmap = New-NexusIconBitmap $size
        try {
            [pscustomobject]@{ Size = $size; Bytes = Convert-BitmapToPngBytes $bitmap }
        }
        finally {
            $bitmap.Dispose()
        }
    }

    $stream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + (16 * $frames.Count)

        foreach ($frame in $frames) {
            $writer.Write([byte]$(if ($frame.Size -ge 256) { 0 } else { $frame.Size }))
            $writer.Write([byte]$(if ($frame.Size -ge 256) { 0 } else { $frame.Size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write([byte[]]$frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

function Write-CoverFallback {
    param([string]$Path)

    $width = 420
    $height = 588
    $bitmap = [System.Drawing.Bitmap]::new($width, $height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $background = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
            [System.Drawing.Rectangle]::new(0, 0, $width, $height),
            [System.Drawing.Color]::FromArgb(255, 24, 30, 46),
            [System.Drawing.Color]::FromArgb(255, 8, 12, 21),
            [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
        try {
            $graphics.FillRectangle($background, 0, 0, $width, $height)
        }
        finally {
            $background.Dispose()
        }

        $violet = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(72, 123, 108, 255), 2)
        $cyan = [System.Drawing.Pen]::new([System.Drawing.Color]::FromArgb(46, 77, 191, 232), 2)
        try {
            for ($index = -2; $index -lt 8; $index++) {
                $y = 58 + ($index * 72)
                $graphics.DrawLine($violet, -40, $y, $width + 40, $y + 170)
                $graphics.DrawLine($cyan, -40, $y + 210, $width + 40, $y + 30)
            }
        }
        finally {
            $violet.Dispose()
            $cyan.Dispose()
        }

        $halo = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(34, 123, 108, 255))
        try {
            $graphics.FillEllipse($halo, 55, 134, 310, 310)
        }
        finally {
            $halo.Dispose()
        }

        Draw-NexusMark $graphics ($width / 2) 286 238
        $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

Write-MultiResolutionIcon (Join-Path $resolvedOutput "NexusLauncher.ico") @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)

$logo = New-NexusIconBitmap 512
try {
    $logo.Save((Join-Path $resolvedOutput "nexus-logo-512.png"), [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $logo.Dispose()
}

Write-CoverFallback (Join-Path $resolvedOutput "nexus-cover-fallback.png")

Write-Host "Generated Nexus brand assets in $resolvedOutput"
