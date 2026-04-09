param(
    [string[]]$SourcePaths = @(
        "C:\Users\hatto\Downloads\bloom-ai-9n6bi2m.png",
        "C:\Users\hatto\Downloads\bloom-ai-zc7in7e.png"
    ),
    [string]$OutputRoot = "output\copilot-verify\appbuilder-icon-samples",
    [int]$CanvasSize = 512,
    [int]$Margin = 32,
    [int]$MaxIconsPerSource = 2,
    [int]$StartNumber = 1,
    [byte]$BackgroundThreshold = 245,
    [byte]$AlphaThreshold = 8,
    [int]$MinRegionWidth = 40,
    [int]$MinRegionHeight = 40,
    [int]$BandGapTolerance = 2,
    [int]$BoxPadding = 4
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$workspaceRoot = (Get-Location).Path
$outputDirectory = Join-Path $workspaceRoot $OutputRoot

function Test-IsBackgroundColor([System.Drawing.Color]$pixel, [byte]$threshold) {
    if ($pixel.A -lt 16) {
        return $true
    }

    return $pixel.R -ge $threshold -and $pixel.G -ge $threshold -and $pixel.B -ge $threshold
}

function Get-Bands([bool[]]$flags, [int]$gapTolerance) {
    $bands = [System.Collections.Generic.List[object]]::new()
    $start = -1
    $gapCount = 0

    for ($index = 0; $index -lt $flags.Length; $index++) {
        if ($flags[$index]) {
            if ($start -lt 0) {
                $start = $index
            }

            $gapCount = 0
            continue
        }

        if ($start -lt 0) {
            continue
        }

        $gapCount += 1
        if ($gapCount -gt $gapTolerance) {
            $bands.Add([PSCustomObject]@{
                    Start = $start
                    End = $index - $gapCount
                })
            $start = -1
            $gapCount = 0
        }
    }

    if ($start -ge 0) {
        $bands.Add([PSCustomObject]@{
                Start = $start
                End = $flags.Length - 1 - $gapCount
            })
    }

    return $bands
}

function Get-VisibleBounds([System.Drawing.Bitmap]$bitmap, [byte]$threshold) {
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
        throw "No visible pixels found after background removal."
    }

    return [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)
}

function Remove-EdgeBackground([System.Drawing.Bitmap]$bitmap, [byte]$threshold) {
    $visited = [bool[]]::new($bitmap.Width * $bitmap.Height)
    $queue = [System.Collections.Generic.Queue[System.Drawing.Point]]::new()
    $transparent = [System.Drawing.Color]::FromArgb(0, 255, 255, 255)

    function Enqueue-IfBackground([int]$x, [int]$y) {
        if ($x -lt 0 -or $y -lt 0 -or $x -ge $bitmap.Width -or $y -ge $bitmap.Height) {
            return
        }

        $offset = ($y * $bitmap.Width) + $x
        if ($visited[$offset]) {
            return
        }

        $visited[$offset] = $true
        if (-not (Test-IsBackgroundColor -pixel $bitmap.GetPixel($x, $y) -threshold $threshold)) {
            return
        }

        $queue.Enqueue([System.Drawing.Point]::new($x, $y))
    }

    for ($x = 0; $x -lt $bitmap.Width; $x++) {
        Enqueue-IfBackground -x $x -y 0
        Enqueue-IfBackground -x $x -y ($bitmap.Height - 1)
    }

    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        Enqueue-IfBackground -x 0 -y $y
        Enqueue-IfBackground -x ($bitmap.Width - 1) -y $y
    }

    while ($queue.Count -gt 0) {
        $point = $queue.Dequeue()
        $bitmap.SetPixel($point.X, $point.Y, $transparent)

        Enqueue-IfBackground -x ($point.X - 1) -y $point.Y
        Enqueue-IfBackground -x ($point.X + 1) -y $point.Y
        Enqueue-IfBackground -x $point.X -y ($point.Y - 1)
        Enqueue-IfBackground -x $point.X -y ($point.Y + 1)
    }
}

function Get-Regions([System.Drawing.Bitmap]$bitmap, [byte]$threshold, [int]$gapTolerance, [int]$minWidth, [int]$minHeight, [int]$padding) {
    $rowContent = [bool[]]::new($bitmap.Height)
    for ($y = 0; $y -lt $bitmap.Height; $y++) {
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            if (-not (Test-IsBackgroundColor -pixel $bitmap.GetPixel($x, $y) -threshold $threshold)) {
                $rowContent[$y] = $true
                break
            }
        }
    }

    $regions = [System.Collections.Generic.List[System.Drawing.Rectangle]]::new()
    foreach ($rowBand in Get-Bands -flags $rowContent -gapTolerance $gapTolerance) {
        $bandHeight = $rowBand.End - $rowBand.Start + 1
        if ($bandHeight -lt $minHeight) {
            continue
        }

        $columnContent = [bool[]]::new($bitmap.Width)
        for ($x = 0; $x -lt $bitmap.Width; $x++) {
            for ($y = $rowBand.Start; $y -le $rowBand.End; $y++) {
                if (-not (Test-IsBackgroundColor -pixel $bitmap.GetPixel($x, $y) -threshold $threshold)) {
                    $columnContent[$x] = $true
                    break
                }
            }
        }

        foreach ($columnBand in Get-Bands -flags $columnContent -gapTolerance $gapTolerance) {
            $bandWidth = $columnBand.End - $columnBand.Start + 1
            if ($bandWidth -lt $minWidth) {
                continue
            }

            $left = [Math]::Max(0, $columnBand.Start - $padding)
            $top = [Math]::Max(0, $rowBand.Start - $padding)
            $right = [Math]::Min($bitmap.Width - 1, $columnBand.End + $padding)
            $bottom = [Math]::Min($bitmap.Height - 1, $rowBand.End + $padding)
            $regions.Add([System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1))
        }
    }

    return $regions
}

function Save-NormalizedIcon([System.Drawing.Bitmap]$sourceBitmap, [System.Drawing.Rectangle]$region, [string]$outputPath, [byte]$backgroundThreshold, [byte]$alphaThreshold, [int]$canvasSize, [int]$margin) {
    $croppedBitmap = $sourceBitmap.Clone($region, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        Remove-EdgeBackground -bitmap $croppedBitmap -threshold $backgroundThreshold
        $visibleBounds = Get-VisibleBounds -bitmap $croppedBitmap -threshold $alphaThreshold
        $visibleBitmap = $croppedBitmap.Clone($visibleBounds, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $outputBitmap = [System.Drawing.Bitmap]::new($canvasSize, $canvasSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $graphics = [System.Drawing.Graphics]::FromImage($outputBitmap)
                try {
                    $graphics.Clear([System.Drawing.Color]::Transparent)
                    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality

                    $availableSize = $canvasSize - ($margin * 2)
                    $scale = [Math]::Min($availableSize / $visibleBitmap.Width, $availableSize / $visibleBitmap.Height)
                    $targetWidth = [Math]::Max(1, [int][Math]::Round($visibleBitmap.Width * $scale))
                    $targetHeight = [Math]::Max(1, [int][Math]::Round($visibleBitmap.Height * $scale))
                    $targetX = [int][Math]::Floor(($canvasSize - $targetWidth) / 2)
                    $targetY = [int][Math]::Floor(($canvasSize - $targetHeight) / 2)

                    $graphics.DrawImage($visibleBitmap, $targetX, $targetY, $targetWidth, $targetHeight)
                }
                finally {
                    $graphics.Dispose()
                }

                $outputBitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $outputBitmap.Dispose()
            }
        }
        finally {
            $visibleBitmap.Dispose()
        }
    }
    finally {
        $croppedBitmap.Dispose()
    }
}

if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$nextNumber = $StartNumber
$writtenPaths = [System.Collections.Generic.List[string]]::new()

foreach ($sourcePath in $SourcePaths) {
    if (-not (Test-Path $sourcePath)) {
        throw "Source image not found: $sourcePath"
    }

    $sourceBitmap = [System.Drawing.Bitmap]::new($sourcePath)
    try {
        $regions = Get-Regions -bitmap $sourceBitmap -threshold $BackgroundThreshold -gapTolerance $BandGapTolerance -minWidth $MinRegionWidth -minHeight $MinRegionHeight -padding $BoxPadding
        if ($regions.Count -eq 0) {
            throw "No icon regions found in $sourcePath"
        }

        $iconCount = [Math]::Min($MaxIconsPerSource, $regions.Count)
        for ($index = 0; $index -lt $iconCount; $index++) {
            $outputPath = Join-Path $outputDirectory ("bloom-app-icon-{0}.png" -f $nextNumber)
            Save-NormalizedIcon -sourceBitmap $sourceBitmap -region $regions[$index] -outputPath $outputPath -backgroundThreshold $BackgroundThreshold -alphaThreshold $AlphaThreshold -canvasSize $CanvasSize -margin $Margin
            $writtenPaths.Add($outputPath)
            Write-Output ("Created bloom-app-icon-{0}.png from {1} region {2}." -f $nextNumber, [System.IO.Path]::GetFileName($sourcePath), ($index + 1))
            $nextNumber += 1
        }
    }
    finally {
        $sourceBitmap.Dispose()
    }
}

Write-Output ("Wrote {0} extracted icon samples to {1}." -f $writtenPaths.Count, $outputDirectory)