#Requires -Version 5.1
<#
.SYNOPSIS
    Bloom Cloud Team Collections — local dev stack smoke test.

.DESCRIPTION
    Verifies the local dev stack is up and functional by executing three
    representative operations:
      1. Sign up a random user via local GoTrue (auth smoke).
      2. Put a versioned object via the parity-check tool (MinIO smoke).
      3. Call the download-start edge function with a valid JWT (function smoke).

    All three must pass for the script to exit 0.

.PARAMETER SupabaseUrl
    Local Supabase API URL.  Defaults to http://localhost:54321.

.PARAMETER AnonKey
    Supabase anon key.  If not supplied, the script calls `supabase status` to
    retrieve it (requires the Supabase CLI to be on PATH).

.PARAMETER MinioEndpoint
    MinIO S3 API URL.  Defaults to http://localhost:9000.

.NOTES
    Requirements:
      - Docker Desktop running, MinIO started:
            docker compose -f server/dev/docker-compose.yml up -d
      - Supabase started:
            supabase start
      - Edge functions running:
            supabase functions serve
      - dotnet 10 on PATH (for parity-check tool)

    *** AUTHORED-BUT-UNRUN ***
    Docker Desktop, Supabase CLI, and Deno are not yet installed on the
    authoring machine.  This script is ready to execute once those are
    available.  Mark the smoke-script checkbox in the task file as run after
    a successful first execution.
#>

[CmdletBinding()]
param(
    [string]$SupabaseUrl    = "http://localhost:54321",
    [string]$AnonKey        = "",
    [string]$MinioEndpoint  = "http://localhost:9000"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Colour helpers
# ---------------------------------------------------------------------------
function Write-Pass([string]$msg) { Write-Host "  [PASS] $msg" -ForegroundColor Green  }
function Write-Fail([string]$msg) { Write-Host "  [FAIL] $msg" -ForegroundColor Red    }
function Write-Step([string]$msg) { Write-Host "`n--- $msg ---" -ForegroundColor Cyan   }

$script:Failures = 0

function Invoke-Check([string]$label, [scriptblock]$body) {
    try {
        & $body
        Write-Pass $label
    } catch {
        Write-Fail "$label : $_"
        $script:Failures++
    }
}

# ---------------------------------------------------------------------------
# Resolve anon key
# ---------------------------------------------------------------------------
if (-not $AnonKey) {
    Write-Host "AnonKey not supplied — querying `supabase status`..."
    try {
        $statusOutput = supabase status 2>&1
        $anonLine = $statusOutput | Select-String "anon key:"
        if ($anonLine) {
            $AnonKey = ($anonLine -split ":\s+")[1].Trim()
            Write-Host "  Retrieved anon key: $($AnonKey.Substring(0,12))..."
        } else {
            throw "Could not parse anon key from supabase status output."
        }
    } catch {
        Write-Error "Cannot resolve anon key: $_`nPass -AnonKey explicitly or run 'supabase start' first."
        exit 1
    }
}

# ---------------------------------------------------------------------------
# Step 1 — Auth smoke: sign up a random user via GoTrue
# ---------------------------------------------------------------------------
Write-Step "1. Auth smoke — sign up a random user via local GoTrue"

$randomEmail    = "smoke-$(([System.Guid]::NewGuid().ToString('N').Substring(0,8)))@test.local"
$randomPassword = "BloomDev123!"

Invoke-Check "POST /auth/v1/signup returns 200 and access_token" {
    $body = @{
        email    = $randomEmail
        password = $randomPassword
    } | ConvertTo-Json

    $response = Invoke-RestMethod `
        -Method  POST `
        -Uri     "$SupabaseUrl/auth/v1/signup" `
        -Headers @{ "apikey" = $AnonKey; "Content-Type" = "application/json" } `
        -Body    $body

    if (-not $response.access_token) {
        throw "No access_token in signup response."
    }
    $script:TestJwt = $response.access_token
    Write-Host "  Signed up $randomEmail — JWT received (${($script:TestJwt.Length)} chars)."
}

# ---------------------------------------------------------------------------
# Step 2 — MinIO smoke: put a versioned object
# ---------------------------------------------------------------------------
Write-Step "2. MinIO smoke — put a versioned object via parity-check tool"

# Locate the parity-check binary (build it if needed).
$parityProj = Join-Path $PSScriptRoot "parity-check\ParityCheck.csproj"
if (-not (Test-Path $parityProj)) {
    Write-Error "parity-check project not found at $parityProj"
    exit 1
}

Invoke-Check "parity-check builds" {
    $buildResult = dotnet build $parityProj --nologo -v quiet 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed:`n$buildResult"
    }
}

Invoke-Check "parity-check runs against MinIO (all 4 checks pass)" {
    $env:MINIO_ENDPOINT   = $MinioEndpoint
    $env:MINIO_ACCESS_KEY = "minioadmin"
    $env:MINIO_SECRET_KEY = "minioadmin"
    $env:MINIO_BUCKET     = "bloom-teams-local"

    $runResult = dotnet run --project $parityProj --no-build 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "parity-check exited with code $LASTEXITCODE.`n$runResult"
    }
    Write-Host "  $($runResult | Select-String 'passed')".Trim()
}

# ---------------------------------------------------------------------------
# Step 3 — Edge function smoke: call download-start
# ---------------------------------------------------------------------------
Write-Step "3. Edge function smoke — call download-start with a valid JWT"

# We need a collection-id to call download-start.  Use a well-known dev UUID.
$testCollectionId = "00000000-aaaa-bbbb-cccc-000000000001"

Invoke-Check "POST /functions/v1/download-start returns 200 or 403/404 (function reachable)" {
    if (-not $script:TestJwt) {
        throw "No JWT from step 1 — cannot test edge function."
    }

    $body = @{ collectionId = $testCollectionId } | ConvertTo-Json

    try {
        $response = Invoke-RestMethod `
            -Method  POST `
            -Uri     "$SupabaseUrl/functions/v1/download-start" `
            -Headers @{
                "Authorization" = "Bearer $($script:TestJwt)"
                "Content-Type"  = "application/json"
                "apikey"        = $AnonKey
            } `
            -Body $body
        # 200 with s3 credentials = full pass
        if ($response.s3.credentials.accessKeyId) {
            Write-Host "  Function returned S3 credentials (accessKeyId present) — dev credential mode working."
        } else {
            Write-Host "  Function returned 200 but no credentials — check task 02 implementation."
        }
    } catch {
        # Accept 403/404 as "function is reachable but the collection does not exist yet."
        # A connection-refused error means the function is not running.
        $statusCode = $_.Exception.Response.StatusCode.value__
        if ($statusCode -in @(403, 404, 401)) {
            Write-Host "  HTTP $statusCode — function is reachable; collection not seeded (expected)."
        } else {
            throw "Unexpected error calling download-start: $_"
        }
    }
}

# ---------------------------------------------------------------------------
# Final summary
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=================================="
if ($script:Failures -eq 0) {
    Write-Host "  SMOKE TEST PASSED" -ForegroundColor Green
    Write-Host "  All three checks green."
} else {
    Write-Host "  SMOKE TEST FAILED — $($script:Failures) check(s) failed" -ForegroundColor Red
    Write-Host "  Review output above for details."
}
Write-Host "=================================="
Write-Host ""

exit $script:Failures
