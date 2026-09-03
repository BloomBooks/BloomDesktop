<#
.SYNOPSIS
Installs, for the current user, every font under a folder, so that Bloom lists it.

.DESCRIPTION
The e2e suite needs some fonts a clean machine does not have: font-chooser.spec.ts wants one
installed font of each verdict Bloom gives (usable, unsuitable, unknown), and a GitHub Actions
windows-latest runner has only Microsoft fonts. The bloom-testing-inputs repository carries the
missing ones under fonts/ (see its fonts/README.md); the nightly workflow runs this script on
that folder before the suite, and a developer can run it on the same folder.

Bloom finds a font two ways, and both have to be satisfied. Its font list comes from GDI+
(InstalledFontCollection), which knows a font once it is registered with the session
(AddFontResource) and, to survive a logoff, listed in the registry. Its font *files* come from
FontFileFinder, which on Windows reads C:\Windows\Fonts and the per-user
%LOCALAPPDATA%\Microsoft\Windows\Fonts. So this copies each file to the per-user folder, writes
the per-user registry entry Windows itself writes for a per-user install, and registers the
file with the session. No administrator rights are needed.

Idempotent: a font already installed (same file name in the per-user folder) is skipped.

.PARAMETER FontsDir
The folder to search, recursively, for .ttf and .otf files. Default: the bloom-testing-inputs
fonts folder that build/get-testing-inputs.mjs fetches, output/testing-inputs/fonts.

.EXAMPLE
pwsh src/BloomE2E/scripts/install-test-fonts.ps1
#>
param(
    [string] $FontsDir = (Join-Path $PSScriptRoot "..\..\..\output\testing-inputs\fonts")
)

$ErrorActionPreference = "Stop"

$FontsDir = (Resolve-Path $FontsDir).Path
$userFontsDir = Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Fonts"
$registryKey = "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts"
New-Item -ItemType Directory -Force $userFontsDir | Out-Null
if (-not (Test-Path $registryKey)) { New-Item -Path $registryKey -Force | Out-Null }

# AddFontResourceW tells the session about a font file at once; WM_FONTCHANGE tells running
# programs the font table changed. Without the first, only a new logon would pick the font up.
Add-Type -Namespace TestFonts -Name Native -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("gdi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern int AddFontResourceW(string lpFileName);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, uint Msg, System.IntPtr wParam, System.IntPtr lParam);
'@

# The font's family name, read with GDI+ (the same library Bloom lists fonts with). Not WPF's
# GlyphTypeface: its constructor fails intermittently in a non-interactive runner session.
Add-Type -AssemblyName System.Drawing
function Get-FontFamilyName([string] $path) {
    $collection = New-Object System.Drawing.Text.PrivateFontCollection
    try {
        $collection.AddFontFile($path)
        if ($collection.Families.Count -eq 0) { throw "GDI+ reads no font family from $path" }
        return $collection.Families[0].Name
    } finally {
        $collection.Dispose()
    }
}

# The registry value name Windows writes for an installed font is its full name plus the format
# in brackets, e.g. "Alef Bold (TrueType)". GDI+ does not read the name, only the file path, so
# the style part need only keep the names of a family's files apart: it is taken from the file
# name after the hyphen ("Alef-Bold" -> "Bold"), or "Regular".
function Get-RegistryValueName([System.IO.FileInfo] $file) {
    $family = Get-FontFamilyName $file.FullName
    $base = $file.BaseName
    $style = if ($base.Contains("-")) { $base.Substring($base.IndexOf("-") + 1) } else { "Regular" }
    $format = if ($file.Extension -ieq ".otf") { "OpenType" } else { "TrueType" }
    return "$family $style ($format)"
}

$files = Get-ChildItem -Path $FontsDir -Recurse -File -Include *.ttf, *.otf
if ($files.Count -eq 0) { throw "No .ttf or .otf files under $FontsDir" }

$installed = 0
foreach ($file in $files) {
    $target = Join-Path $userFontsDir $file.Name
    if (Test-Path $target) {
        Write-Host "already installed: $($file.Name)"
    } else {
        Copy-Item $file.FullName $target
        $valueName = Get-RegistryValueName $file
        New-ItemProperty -Path $registryKey -Name $valueName -Value $target -PropertyType String -Force | Out-Null
        $installed++
        Write-Host "installed: $($file.Name) as '$valueName'"
    }
    if ([TestFonts.Native]::AddFontResourceW($target) -eq 0) {
        throw "AddFontResource rejected $target"
    }
}
# PostMessage, not SendMessage: a broadcast SendMessage waits for every top-level window to
# answer, and one hung window (there is usually one) stalls this script for good.
$HWND_BROADCAST = [IntPtr] 0xffff
$WM_FONTCHANGE = 0x001D
[TestFonts.Native]::PostMessage($HWND_BROADCAST, $WM_FONTCHANGE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null

# Prove it the way Bloom will see it: GDI+'s installed-font families.
$families = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$missing = @()
foreach ($file in $files) {
    $family = Get-FontFamilyName $file.FullName
    if ($families -notcontains $family) { $missing += "$family ($($file.Name))" }
}
if ($missing.Count -gt 0) {
    throw "Installed the files, but GDI+ does not list: $($missing -join ', '). Bloom will not offer them."
}
Write-Host "$installed font file(s) newly installed; all $($files.Count) are listed by GDI+."
