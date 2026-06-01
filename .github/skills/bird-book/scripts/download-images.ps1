# download-images.ps1
# Downloads each bird's photo (from birds.json), resizes to <= $MaxEdge px (no upscaling),
# saves as bNNN.jpg into the book folder, and writes a manifest with final dimensions.
#
# RESUMABLE + POLITE: Wikimedia rate-limits bulk requests (HTTP 429). This sleeps
# between requests and backs off on 429, and SKIPS any image already on disk, so you
# can re-run it until everything succeeds. Give a descriptive, contactable User-Agent
# (Wikimedia policy: https://meta.wikimedia.org/wiki/User-Agent_policy).
#
# Usage (Windows PowerShell or pwsh):
#   ./download-images.ps1 -Book "<book folder>" -BirdsJson "<birds.json>" -ManifestOut "<manifest.json>"

param(
  [string]$Book        = "C:\Users\hatto\OneDrive\Documents\Bloom\Wildlife Identifier Books\Identification de la faune",
  [string]$BirdsJson   = "$env:TEMP\birds.json",
  [string]$ManifestOut = "$env:TEMP\manifest.json",
  [int]$MaxEdge        = 1200,
  [int]$DelayMs        = 2500,
  [string]$UserAgent   = "BloomBookBuilder/1.0 (contact: you@example.org) educational wildlife book"
)

Add-Type -AssemblyName System.Drawing
$birds = Get-Content $BirdsJson -Raw | ConvertFrom-Json
$tmp = "$env:TEMP\bird_dl"; New-Item -ItemType Directory -Force $tmp | Out-Null

$jpeg = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality, [long]85)

function Save-Resized($srcPath, $destPath) {
  $img = [System.Drawing.Image]::FromFile($srcPath)
  try {
    $w = $img.Width; $h = $img.Height
    $scale = [Math]::Min(1.0, $script:MaxEdge / [Math]::Max($w, $h))
    $nw = [Math]::Max(1, [int][Math]::Round($w * $scale))
    $nh = [Math]::Max(1, [int][Math]::Round($h * $scale))
    $bmp = New-Object System.Drawing.Bitmap $nw, $nh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, 0, 0, $nw, $nh)
    $g.Dispose()
    $bmp.Save($destPath, $script:jpeg, $script:ep)
    $bmp.Dispose()
    return @($nw, $nh)
  } finally { $img.Dispose() }
}

$manifest = @()
foreach ($b in $birds) {
  $row = [int]$b.row
  $file = "b{0:D3}.jpg" -f ($row - 1)
  $dest = Join-Path $Book $file
  $entry = [ordered]@{ row = $row; file = $file; ok = $false; w = 0; h = 0; err = "" }

  if (-not $b.url) { $entry.err = "no url"; $manifest += [pscustomobject]$entry; continue }
  if (Test-Path $dest) {                       # resume: already downloaded
    $img = [System.Drawing.Image]::FromFile($dest)
    $entry.ok = $true; $entry.w = $img.Width; $entry.h = $img.Height; $img.Dispose()
    $manifest += [pscustomobject]$entry; continue
  }

  $src = Join-Path $tmp ("src_{0}" -f $row)
  for ($attempt = 1; $attempt -le 4 -and -not $entry.ok; $attempt++) {
    Start-Sleep -Milliseconds $DelayMs
    try {
      Invoke-WebRequest -Uri $b.url -OutFile $src -UserAgent $UserAgent -MaximumRedirection 5 -TimeoutSec 90 -ErrorAction Stop
      $dims = Save-Resized $src $dest
      $entry.ok = $true; $entry.w = $dims[0]; $entry.h = $dims[1]; $entry.err = ""
      Write-Host ("OK   row {0} -> {1} {2}x{3}" -f $row, $file, $dims[0], $dims[1])
    } catch {
      $msg = $_.Exception.Message; $entry.err = $msg
      if ($msg -match '429') { Write-Host ("429  row {0} attempt {1}; backing off 30s" -f $row, $attempt); Start-Sleep -Seconds 30 }
      else { Write-Host ("FAIL row {0}: {1}" -f $row, $msg); break }
    }
  }
  $manifest += [pscustomobject]$entry
}

$manifest | ConvertTo-Json | Set-Content $ManifestOut -Encoding UTF8
$ok = ($manifest | Where-Object { $_.ok }).Count
Write-Host ("DONE: {0}/{1} images saved -> {2}" -f $ok, $manifest.Count, $ManifestOut)
