# Bloom ships three .NET apps - Bloom.exe, BloomPdfMaker.exe and BloomFreezeDoctor.exe - in ONE
# folder. A folder holds one copy of any given DLL, so wherever two of them depend on the same
# assembly they must depend on the SAME VERSION of it. Whichever builds last wins the file, and the
# losers are then running against a version their own .deps.json never named.
#
# That is not a theoretical tidiness problem. The .NET host binds by simple name; it accepts a
# HIGHER version than the one asked for, but rejects a lower one. So the app left with a file
# below what it asked for dies with a FileNotFoundException naming a version that is sitting right
# there in the folder. In BL-16867 the Freeze Doctor's Microsoft.Diagnostics.Runtime pulled in
# Microsoft.Extensions.* 6.0.0, under Bloom's 8.x/9.x; the Doctor builds last; and every PDF in
# Bloom 6.5 failed inside PdfSharp, which wants Logging.Abstractions 8. It shipped to BetaInternal.
#
# This script is what makes the rule enforceable rather than a thing to remember. It reads the
# .deps.json beside each app - the authoritative record of what that app believes it is loading -
# and fails the build if any two disagree about an assembly.
#
# Note that it can only run where the apps have been built side by side, which is build/Bloom.proj's
# BuildInternal (CI and makeWindowsInstaller.bat), not an ordinary Visual Studio or ./go.sh build.
# That is enough to stop a mismatch reaching an installer, which is the thing that hurt.

[CmdletBinding()]
param(
	# The output folder the apps were built into, e.g. output\Release\x64.
	[Parameter(Mandatory = $true)]
	[string]$OutputFolder
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $OutputFolder)) {
	Write-Error "check-dependency-versions: no such folder '$OutputFolder'. It should be the folder the apps were just built into."
	exit 1
}

$depsFiles = @(Get-ChildItem -LiteralPath $OutputFolder -Filter "*.deps.json" -File)
if ($depsFiles.Count -eq 0) {
	# Being handed an empty or wrong folder must not pass silently: a check that cannot see anything
	# is indistinguishable from a check that found nothing wrong, and this one exists precisely
	# because nobody was looking.
	Write-Error "check-dependency-versions: found no .deps.json files in '$OutputFolder'. Did the build put the apps somewhere else?"
	exit 1
}

# assembly file name -> assembly version -> list of "app (package/version)" that claim it
$claims = @{}

foreach ($depsFile in $depsFiles) {
	$app = $depsFile.Name -replace "\.deps\.json$", ""
	$deps = Get-Content -LiteralPath $depsFile.FullName -Raw | ConvertFrom-Json

	foreach ($target in $deps.targets.PSObject.Properties) {
		foreach ($library in $target.Value.PSObject.Properties) {
			$runtime = $library.Value.runtime
			if ($null -eq $runtime) { continue }

			foreach ($asset in $runtime.PSObject.Properties) {
				$fileName = Split-Path -Leaf $asset.Name
				if ($fileName -eq "_._") { continue }

				# An app's own assemblies are listed with no assemblyVersion. Nothing to compare,
				# and they are not a conflict: each app is the only one that ships its own output.
				$version = $asset.Value.assemblyVersion
				if ([string]::IsNullOrEmpty($version)) { continue }

				if (-not $claims.ContainsKey($fileName)) { $claims[$fileName] = @{} }
				if (-not $claims[$fileName].ContainsKey($version)) { $claims[$fileName][$version] = @() }
				$claims[$fileName][$version] += "$app (from $($library.Name))"
			}
		}
	}
}

$conflicts = $claims.Keys | Where-Object { $claims[$_].Keys.Count -gt 1 } | Sort-Object

if ($conflicts.Count -eq 0) {
	Write-Host "check-dependency-versions: $($depsFiles.Count) co-located apps agree on every shared assembly."
	exit 0
}

Write-Host ""
Write-Host "*** Co-located apps disagree about $($conflicts.Count) assembly version(s) in $OutputFolder"
Write-Host ""
foreach ($fileName in $conflicts) {
	Write-Host "  $fileName"
	foreach ($version in ($claims[$fileName].Keys | Sort-Object)) {
		foreach ($claimant in ($claims[$fileName][$version] | Sort-Object)) {
			Write-Host "      $version  <- $claimant"
		}
	}
}
Write-Host ""
Write-Host "These apps all install into the same folder, which can hold only one copy of each DLL,"
Write-Host "so the last one built wins and the others run against a version they never asked for."
Write-Host "Whoever ends up below the version it asked for will fail at run time with a"
Write-Host "FileNotFoundException naming a file that is present (BL-16867)."
Write-Host ""
Write-Host "Fix it by pinning the lower side up to match, with an explicit PackageReference in the"
Write-Host "project that is behind - even for a package it never references itself and only gets"
Write-Host "transitively. See the note above the PackageReferences in"
Write-Host "src/BloomFreezeDoctor.Core/BloomFreezeDoctor.Core.csproj."
Write-Host ""
exit 1
