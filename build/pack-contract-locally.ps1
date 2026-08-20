# Builds BloomFreezeDoctor.Contract from a clone of the Doctor's repo into ./localpackages, so this
# branch can be built before the package is published anywhere.
#
# TEMPORARY. This exists only because nothing is published yet: a push to a public feed cannot be
# undone, so we wanted to review the shape of the change first. When the package is published for
# real, delete this script, ./localpackages, and the NuGet.Config at the repository root. See BL-16719.

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repoRoot "localpackages"

# Where the Doctor's repo is. Beside this one by default, which is how our checkouts are usually laid
# out; override with BLOOM_FREEZE_DOCTOR_REPO if yours is somewhere else.
$doctorRepo = if ($env:BLOOM_FREEZE_DOCTOR_REPO) {
    $env:BLOOM_FREEZE_DOCTOR_REPO
} else {
    Join-Path (Split-Path -Parent $repoRoot) "bloom-freeze-doctor"
}

$project = Join-Path $doctorRepo "src\BloomFreezeDoctor.Contract\BloomFreezeDoctor.Contract.csproj"

if (-not (Test-Path $project)) {
    Write-Host "Could not find the contract project at:" -ForegroundColor Yellow
    Write-Host "  $project"
    Write-Host ""
    Write-Host "Clone it beside this repository:"
    Write-Host "  git clone https://github.com/BloomBooks/bloom-freeze-doctor"
    Write-Host ""
    Write-Host "or point BLOOM_FREEZE_DOCTOR_REPO at an existing clone. Note that the contract project"
    Write-Host "only exists on that repo's contract-package branch until it is merged."
    exit 1
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

Write-Host "Packing $project"
Write-Host "     to $output"
dotnet pack $project --configuration Release --output $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Done. Now build Bloom as usual; it will restore the package from ./localpackages."
Write-Host "If you have built this branch before, you may need to clear the cached copy first:"
Write-Host "  dotnet nuget locals http-cache --clear"
Write-Host "  Remove-Item -Recurse -Force ~/.nuget/packages/bloomfreezedoctor.contract"
