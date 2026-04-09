param(
    [string[]]$Paths = @(
        "DistFiles\appbuilder-icons\bloom-ai2-01-open-book-stars.png",
        "DistFiles\appbuilder-icons\bloom-ai2-19-rocket-book.png"
    ),
    [int]$CanvasSize = 512,
    [int]$Margin = 32,
    [byte]$AlphaThreshold = 8
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$workspaceRoot = (Get-Location).Path

function Get-Bounds([System.Drawing.Bitmap]$bitmap, [byte]$threshold) {
    $left = $bitmap.Width
    $top = $bitmap.Height
    $right = -1
    $bottom = -1

    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            if ($pixel.A -gt $threshold) {
                if ($x -lt $left) { $left = $x }
                if ($y -lt $top) { $top = $y }
                if ($x -gt $right) { $right = $x }
                if ($y -gt $bottom) { $bottom = $y }
            }
        }
    }

    if ($right -lt 0 -or $bottom -lt 0) {
        throw "No visible pixels found."
    }

    return [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)
}

foreach ($relativePath in $Paths) {
    $fullPath = Join-Path $workspaceRoot $relativePath
    if (-not (Test-Path $fullPath)) {
        throw "File not found: $fullPath"
    }

    $tempPath = "$fullPath.cropped"
    if (Test-Path $tempPath) {
        Remove-Item $tempPath -Force
    }

    $sourceBitmap = [System.Drawing.Bitmap]::new($fullPath)
    try {
        $bounds = Get-Bounds -bitmap $sourceBitmap -threshold $AlphaThreshold
        $croppedBitmap = $sourceBitmap.Clone($bounds, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $outputBitmap = [System.Drawing.Bitmap]::new($CanvasSize, $CanvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($outputBitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                    $availableSize = $CanvasSize - ($Margin * 2)
                    $scale = [Math]::Min($availableSize / $croppedBitmap.Width, $availableSize / $croppedBitmap.Height)
                    $targetWidth = [Math]::Max(1, [int][Math]::Round($croppedBitmap.Width * $scale))
                    $targetHeight = [Math]::Max(1, [int][Math]::Round($croppedBitmap.Height * $scale))
                    $targetX = [int][Math]::Floor(($CanvasSize - $targetWidth) / 2)
                    $targetY = [int][Math]::Floor(($CanvasSize - $targetHeight) / 2)

                    $graphics.DrawImage($croppedBitmap, $targetX, $targetY, $targetWidth, $targetHeight)
                }
                finally {
                    $graphics.Dispose()
                }

                $outputBitmap.Save($tempPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $outputBitmap.Dispose()
            }
        }
        finally {
            $croppedBitmap.Dispose()
        }

        Write-Output "Cropped $relativePath to visible bounds $($bounds.Width)x$($bounds.Height) and repadded to ${CanvasSize}x${CanvasSize}."
    }
    finally {
        $sourceBitmap.Dispose()
    }

    if (Test-Path $fullPath) {
        Remove-Item $fullPath -Force
    }

    Move-Item $tempPath $fullPath
}