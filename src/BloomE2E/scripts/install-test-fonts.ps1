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

# The registry value name Windows uses is the font's full name plus the format in brackets,
# e.g. "Alef Regular (TrueType)". GDI+ does not read the name, only the file, but matching
# Windows keeps Settings > Fonts happy about the entry.
Add-Type -AssemblyName PresentationCore
function Get-FontFullName([string] $path) {
    $face = New-Object System.Windows.Media.GlyphTypeface ([Uri] $path)
    $name = $face.Win32FamilyNames["en-us"]
    if (-not $name) { $name = ($face.Win32FamilyNames.Values | Select-Object -First 1) }
    $style = $face.Win32FaceNames["en-us"]
    if (-not $style) { $style = ($face.Win32FaceNames.Values | Select-Object -First 1) }
    return "$name $style".Trim()
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
        $format = if ($file.Extension -ieq ".otf") { "OpenType" } else { "TrueType" }
        $valueName = "$(Get-FontFullName $target) ($format)"
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
Add-Type -AssemblyName System.Drawing
$families = (New-Object System.Drawing.Text.InstalledFontCollection).Families | ForEach-Object { $_.Name }
$missing = @()
foreach ($file in $files) {
    $family = (New-Object System.Windows.Media.GlyphTypeface ([Uri] $file.FullName)).Win32FamilyNames.Values | Select-Object -First 1
    if ($families -notcontains $family) { $missing += "$family ($($file.Name))" }
}
if ($missing.Count -gt 0) {
    throw "Installed the files, but GDI+ does not list: $($missing -join ', '). Bloom will not offer them."
}
Write-Host "$installed font file(s) newly installed; all $($files.Count) are listed by GDI+."
