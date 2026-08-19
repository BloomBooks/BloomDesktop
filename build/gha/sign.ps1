# Signs a file using Microsoft Trusted Signing (Azure), the same service and SIL account
# that BloomBooks/bloompub-viewer signs with (via sillsdev/codesign/trusted-signing-action).
#
# This exists so that GitHub Actions runners have a `sign <file>` command on PATH, which is
# what build/Bloom.proj's signing targets (SignExesIfPossible, SignIfPossible, SignInstaller)
# invoke; on TeamCity that command is provided by the build agents. The workflow
# (.github/workflows/release-installer.yml) decodes the TRUSTED_SIGNING_CREDENTIALS secret
# into the environment variables below and installs the TrustedSigning PowerShell module
# before msbuild runs.
#
# The Invoke-TrustedSigning parameters here mirror what azure/trusted-signing-action@v0.5.1
# (wrapped by sillsdev/codesign/trusted-signing-action@v3) passes.
param([Parameter(Mandatory = $true)][string]$File)

$ErrorActionPreference = 'Stop'

foreach ($name in 'AZURE_TENANT_ID', 'AZURE_CLIENT_ID', 'AZURE_CLIENT_SECRET',
    'TRUSTED_SIGNING_ENDPOINT', 'TRUSTED_SIGNING_ACCOUNT') {
    if (-not (Test-Path "env:$name")) {
        throw "sign: environment variable $name is not set; cannot sign $File"
    }
}
if (-not (Test-Path $File)) {
    throw "sign: file not found: $File"
}

Write-Host "sign: signing $File with Trusted Signing account $env:TRUSTED_SIGNING_ACCOUNT"
# Authentication is picked up from the AZURE_* environment variables (EnvironmentCredential).
Invoke-TrustedSigning `
    -Endpoint $env:TRUSTED_SIGNING_ENDPOINT `
    -CodeSigningAccountName $env:TRUSTED_SIGNING_ACCOUNT `
    -CertificateProfileName 'sil-codesign-production' `
    -Files $File `
    -FileDigest SHA256 `
    -TimestampRfc3161 'http://timestamp.acs.microsoft.com' `
    -TimestampDigest SHA256 `
    -Description 'Bloom' `
    -DescriptionUrl 'https://bloomlibrary.org'
