<#
.SYNOPSIS
  Fails if any exe or dll in a Velopack package, or the installer itself, lacks a valid Authenticode signature.

.DESCRIPTION
  Smart App Control, on by default on new Windows 11 machines, can refuse to load any unsigned exe or dll that
  Microsoft does not consider reputable, and Bloom then crashes during install or startup (BL-16913). So after
  Velopack has signed the package, this checks that it really did sign everything we ship: it looks inside the
  full .nupkg (which is exactly what gets installed, including Velopack's own Update.exe) and at Setup.exe.

  Files that were already signed by Microsoft or another vendor keep their own signatures, which is fine; the
  signer summary shows who signed what.

.PARAMETER Package
  The full .nupkg that vpk pack produced.

.PARAMETER Installer
  The Setup.exe (or its renamed copy) that vpk pack produced.

.PARAMETER AcceptThumbprint
  Only for trying out signing locally with a self-signed certificate, which Windows does not trust and so reports
  as invalid. Signatures made by the certificate with this thumbprint are accepted anyway.
#>
param(
	[Parameter(Mandatory = $true)][string]$Package,
	[Parameter(Mandatory = $true)][string]$Installer,
	[string]$AcceptThumbprint = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Extract just the exes and dlls, to a short path, since some package paths are deep.
$extractFolder = Join-Path ([System.IO.Path]::GetTempPath()) ("bloomsig-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Path $extractFolder | Out-Null
try {
	$zip = [System.IO.Compression.ZipFile]::OpenRead($Package)
	try {
		$index = 0
		$filesToCheck = @()
		foreach ($entry in $zip.Entries) {
			$extension = [System.IO.Path]::GetExtension($entry.FullName).ToLowerInvariant()
			if ($extension -ne ".exe" -and $extension -ne ".dll") {
				continue
			}
			# Each file gets its own folder so that files with the same name in different places don't collide.
			$index++
			$folder = Join-Path $extractFolder $index
			New-Item -ItemType Directory -Path $folder | Out-Null
			$path = Join-Path $folder $entry.Name
			[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path)
			$filesToCheck += [pscustomobject]@{ Name = [System.Uri]::UnescapeDataString($entry.FullName); Path = $path }
		}
	}
	finally {
		$zip.Dispose()
	}
	$filesToCheck += [pscustomobject]@{ Name = (Split-Path $Installer -Leaf); Path = $Installer }

	if ($filesToCheck.Count -lt 2) {
		throw "Found no exe or dll files in $Package; something is wrong with the package or with this script."
	}

	$problems = @()
	$signers = @{}
	$notImages = @()
	foreach ($file in $filesToCheck) {
		# Not everything named .dll is a program: connections.dll is a text file. Windows never loads those as
		# code, so Smart App Control doesn't care about them, and they can't be signed anyway. Real ones start "MZ".
		$stream = [System.IO.File]::OpenRead($file.Path)
		try {
			$isImage = $stream.ReadByte() -eq 0x4D -and $stream.ReadByte() -eq 0x5A
		}
		finally {
			$stream.Dispose()
		}
		if (-not $isImage) {
			$notImages += $file.Name
			continue
		}
		$signature = Get-AuthenticodeSignature -LiteralPath $file.Path
		$certificate = $signature.SignerCertificate
		$ok = $signature.Status -eq "Valid"
		if (-not $ok -and $AcceptThumbprint -and $certificate -and $certificate.Thumbprint -eq $AcceptThumbprint) {
			$ok = $true
		}
		if ($ok) {
			$signer = $certificate.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
			$signers[$signer] = 1 + [int]$signers[$signer]
		}
		else {
			$problems += "  $($signature.Status): $($file.Name)"
		}
	}

	if ($notImages.Count -gt 0) {
		Write-Host "Skipped $($notImages.Count) files that are not really programs: $($notImages -join ', ')"
	}
	Write-Host "Checked the signatures of $($filesToCheck.Count - $notImages.Count) exe and dll files. Signed by:"
	foreach ($signer in ($signers.Keys | Sort-Object)) {
		Write-Host "  $($signers[$signer]) by $signer"
	}
	if ($problems.Count -gt 0) {
		Write-Host "$($problems.Count) files are not validly signed:"
		$problems | ForEach-Object { Write-Host $_ }
		exit 1
	}
	Write-Host "All are validly signed."
}
finally {
	Remove-Item -LiteralPath $extractFolder -Recurse -Force
}
