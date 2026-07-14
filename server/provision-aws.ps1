#Requires -Version 5.1
<#
.SYNOPSIS
    Bloom Cloud Team Collections - idempotent AWS provisioning for production/sandbox.

.DESCRIPTION
    Creates (or verifies, if they already exist) everything the checked-in edge
    functions (supabase/functions/**) need to run against real AWS S3 instead of the
    local MinIO dev stack (server/dev/):

      1. One S3 bucket per environment (default: bloom-teams-production,
         bloom-teams-sandbox) - versioning ON, all public access blocked, lifecycle
         rules (abort incomplete multipart uploads after 7 days; expire noncurrent
         object versions after 7 days under the "tc/" prefix). Mirrors
         server/dev/docker-compose.yml's MinIO init job, which sets the identical
         values for local dev - see that file's `minio-init` service.
      2. An IAM role `bloom-teams-broker` that `checkin-start`/`download-start`/
         `collection-files-start` assume (via STS AssumeRole) to mint short-lived,
         per-request, per-book-prefix-scoped S3 credentials for the CLIENT. Its own
         permission policy is the ceiling (PutObject/GetObject/GetObjectVersion/
         AbortMultipartUpload/ListMultipartUploadParts on both buckets); the edge
         function narrows further per call via AssumeRole's `Policy` parameter (see
         supabase/functions/_shared/s3.ts's `buildSessionPolicy`).
      3. An "assume-only" IAM user `bloom-teams-broker-caller` whose ONLY permission
         is `sts:AssumeRole` on that one role - if its access key ever leaks, the
         attacker can only mint scoped-down session credentials via the broker role,
         never touch S3 directly. Its access key becomes the edge functions'
         AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY secrets.
      4. An IAM user `bloom-teams-admin` with direct (non-assumed) S3 permissions on
         both buckets - this backs `adminS3Client()` in _shared/s3.ts, used ONLY
         server-side for HeadObject checksum/version-id verification and the
         `.manifest.json` backup PUT (never handed to a client). This is the
         production counterpart of MinIO's root credentials in dev mode.

    Every step checks for the existing resource first (head-bucket / get-role /
    get-user / list-access-keys) before creating anything, so re-running this script
    after a partial failure - or just to confirm the current state matches - is safe.

.PARAMETER Environments
    Which environment bucket(s)/short names to provision. Default: production and
    sandbox, matching the task file's "buckets bloom-teams-production|sandbox".
    Bucket names are `bloom-teams-<environment>`.

.PARAMETER Region
    AWS region for the buckets and IAM (IAM is technically global, but the access
    key's region-scoped S3 endpoint is derived from this). Default: us-east-1,
    matching supabase/functions/_shared/env.ts's `prodBrokerConfig`/`s3Env` defaults.

.PARAMETER AwsProfile
    Named AWS CLI profile to use (`aws --profile <name> ...`). Leave unset to use
    the environment's default credential chain.

.PARAMETER NoncurrentExpireDays
    Days after which a noncurrent (superseded) object version is permanently deleted.
    Default: 7, matching CONTRACTS.md's "S3 layout" section and the local MinIO setup.
    MUST stay strictly greater than the checkin/collection-files transaction lifetime
    (48h - see tc.checkin_transactions.expires_at in
    supabase/migrations/20260706000001_tc_schema.sql, and the enforced invariant in
    supabase/functions/_shared/invariants.test.ts) or an in-flight transaction could
    have its referenced object version deleted out from under it.

.PARAMETER AbortMultipartDays
    Days after which an incomplete multipart upload is aborted and its parts
    reclaimed. Default: 7 (CONTRACTS.md).

.PARAMETER WhatIf
    Print what would be done without making any AWS API calls that create/modify
    resources (read-only "does this already exist" checks still run, so you can see
    current state).

.NOTES
    STATUS: written and reviewed as part of task 02 (2026-07-06); NOT YET RUN against
    a real AWS account - no AWS account was available in that environment (see the
    task file's Progress log). Acceptance for task 02 does not require running this;
    it is deferred until real infrastructure work begins (see IMPLEMENTATION.md's
    "Deferred until real infrastructure is available" list). Whoever runs this for
    real should:
      - Review the IAM policy JSON below against current least-privilege guidance.
      - Confirm the AWS CLI version in use (`aws --version`) supports every
        `aws s3api` / `aws iam` subcommand invoked here (developed against the
        v2 CLI's documented syntax; not executed).
      - Capture the two access keys this script prints ONCE (AWS will never show a
        secret access key again) and feed them to `supabase secrets set` per the
        printed command block at the end - do not commit them anywhere.

    Requires: AWS CLI v2 on PATH, authenticated with sufficient IAM/S3 permissions to
    create buckets, roles, users, policies, and access keys.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string[]]$Environments          = @("production", "sandbox"),
    [string]  $Region                = "us-east-1",
    [string]  $AwsProfile            = "",
    [int]     $NoncurrentExpireDays  = 7,
    [int]     $AbortMultipartDays    = 7
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# aws CLI wrapper - centralizes --profile/--region injection and JSON parsing.
# ---------------------------------------------------------------------------
function Invoke-Aws {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [switch]$AllowFailure   # return $null instead of throwing on a non-zero exit
    )
    $fullArgs = @($Arguments) + @("--region", $Region)
    if ($AwsProfile) { $fullArgs += @("--profile", $AwsProfile) }

    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & aws @fullArgs 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $prevEap

    if ($exitCode -ne 0) {
        if ($AllowFailure) { return $null }
        throw "aws $($Arguments -join ' ') failed (exit $exitCode):`n$output"
    }
    $joined = ($output -join "`n").Trim()
    if (-not $joined) { return $null }
    try { return $joined | ConvertFrom-Json } catch { return $joined }
}

function Write-Step([string]$msg) { Write-Host "`n--- $msg ---" -ForegroundColor Cyan }
function Write-Ok([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Made([string]$msg) { Write-Host "  [CREATED] $msg" -ForegroundColor Yellow }

# ===========================================================================
# 1. S3 buckets
# ===========================================================================
function Ensure-Bucket {
    param([Parameter(Mandatory)][string]$BucketName)

    # head-bucket prints nothing on success; its exit code is the existence signal
    # (AllowFailure suppresses the throw so a 404 "not found" isn't fatal here).
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & aws s3api head-bucket --bucket $BucketName --region $Region 2>$null | Out-Null
    $bucketExists = ($LASTEXITCODE -eq 0)
    $ErrorActionPreference = $prevEap

    if ($bucketExists) {
        Write-Ok "bucket '$BucketName' already exists"
        return
    }

    if ($PSCmdlet.ShouldProcess($BucketName, "create S3 bucket")) {
        if ($Region -eq "us-east-1") {
            # us-east-1 is the one region where CreateBucketConfiguration must be
            # omitted entirely (passing it, even matching the region, errors).
            Invoke-Aws -Arguments @("s3api", "create-bucket", "--bucket", $BucketName) | Out-Null
        } else {
            Invoke-Aws -Arguments @(
                "s3api", "create-bucket", "--bucket", $BucketName,
                "--create-bucket-configuration", "LocationConstraint=$Region"
            ) | Out-Null
        }
        Write-Made "bucket '$BucketName'"
    }
}

function Ensure-BucketVersioning {
    param([Parameter(Mandatory)][string]$BucketName)

    $status = Invoke-Aws -Arguments @("s3api", "get-bucket-versioning", "--bucket", $BucketName)
    if ($status -and $status.Status -eq "Enabled") {
        Write-Ok "versioning already Enabled on '$BucketName'"
        return
    }
    if ($PSCmdlet.ShouldProcess($BucketName, "enable versioning")) {
        Invoke-Aws -Arguments @(
            "s3api", "put-bucket-versioning", "--bucket", $BucketName,
            "--versioning-configuration", "Status=Enabled"
        ) | Out-Null
        Write-Made "versioning enabled on '$BucketName'"
    }
}

function Ensure-PublicAccessBlocked {
    param([Parameter(Mandatory)][string]$BucketName)

    $config = "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"
    $current = Invoke-Aws -AllowFailure -Arguments @("s3api", "get-public-access-block", "--bucket", $BucketName)
    $alreadyBlocked = $current -and $current.PublicAccessBlockConfiguration -and
        $current.PublicAccessBlockConfiguration.BlockPublicAcls -and
        $current.PublicAccessBlockConfiguration.IgnorePublicAcls -and
        $current.PublicAccessBlockConfiguration.BlockPublicPolicy -and
        $current.PublicAccessBlockConfiguration.RestrictPublicBuckets

    if ($alreadyBlocked) {
        Write-Ok "public access already fully blocked on '$BucketName'"
        return
    }
    if ($PSCmdlet.ShouldProcess($BucketName, "block all public access")) {
        Invoke-Aws -Arguments @(
            "s3api", "put-public-access-block", "--bucket", $BucketName,
            "--public-access-block-configuration", $config
        ) | Out-Null
        Write-Made "public access blocked on '$BucketName'"
    }
}

function Ensure-LifecycleRules {
    param([Parameter(Mandatory)][string]$BucketName)

    # Mirrors server/dev/docker-compose.yml's minio-init job (which sets an equivalent
    # noncurrent-version-expiry rule via `mc ilm rule add --noncurrent-expire-days 7
    # --prefix tc/`). Unlike MinIO, AWS S3's lifecycle API supports
    # AbortIncompleteMultipartUpload directly (the local docker-compose.yml has a
    # documented gap here - it relies on MinIO's built-in stale-upload cleanup
    # instead), so this is the one place dev and prod genuinely differ in mechanism
    # while matching in outcome/timing.
    $lifecycleConfig = @{
        Rules = @(
            @{
                ID     = "tc-noncurrent-version-expiry"
                Status = "Enabled"
                Filter = @{ Prefix = "tc/" }
                NoncurrentVersionExpiration = @{ NoncurrentDays = $NoncurrentExpireDays }
            },
            @{
                ID     = "tc-abort-incomplete-multipart-uploads"
                Status = "Enabled"
                Filter = @{ Prefix = "tc/" }
                AbortIncompleteMultipartUpload = @{ DaysAfterInitiation = $AbortMultipartDays }
            }
        )
    }
    $json = $lifecycleConfig | ConvertTo-Json -Depth 10 -Compress

    if ($PSCmdlet.ShouldProcess($BucketName, "set lifecycle configuration (idempotent - always re-applied)")) {
        $tempFile = [System.IO.Path]::GetTempFileName()
        try {
            Set-Content -Path $tempFile -Value $json -Encoding utf8NoBOM
            Invoke-Aws -Arguments @(
                "s3api", "put-bucket-lifecycle-configuration", "--bucket", $BucketName,
                "--lifecycle-configuration", "file://$tempFile"
            ) | Out-Null
            Write-Ok "lifecycle rules applied to '$BucketName' (noncurrent-expiry ${NoncurrentExpireDays}d, abort-multipart ${AbortMultipartDays}d)"
        } finally {
            Remove-Item -Path $tempFile -Force -ErrorAction SilentlyContinue
        }
    }
}

# ===========================================================================
# 2/3/4. IAM: broker role, assume-only caller user, admin user
# ===========================================================================
$BrokerRoleName   = "bloom-teams-broker"
$BrokerCallerName = "bloom-teams-broker-caller"
$AdminUserName    = "bloom-teams-admin"

function Get-AccountId {
    $identity = Invoke-Aws -Arguments @("sts", "get-caller-identity")
    return $identity.Account
}

function Ensure-IamUser {
    param([Parameter(Mandatory)][string]$UserName)

    $existing = Invoke-Aws -AllowFailure -Arguments @("iam", "get-user", "--user-name", $UserName)
    if ($existing) {
        Write-Ok "IAM user '$UserName' already exists"
        return $existing.User.Arn
    }
    if ($PSCmdlet.ShouldProcess($UserName, "create IAM user")) {
        $created = Invoke-Aws -Arguments @("iam", "create-user", "--user-name", $UserName)
        Write-Made "IAM user '$UserName'"
        return $created.User.Arn
    }
    return $null
}

function Ensure-AccessKey {
    param([Parameter(Mandatory)][string]$UserName)

    # Idempotency for access keys is inherently different from everything else here:
    # AWS never re-displays a secret access key after creation, and a user can hold up
    # to 2 keys. So "already exists" means "skip creation and tell the operator to
    # reuse/rotate manually" rather than silently creating a redundant key.
    $existingKeys = Invoke-Aws -Arguments @("iam", "list-access-keys", "--user-name", $UserName)
    if ($existingKeys -and $existingKeys.AccessKeyMetadata -and $existingKeys.AccessKeyMetadata.Count -gt 0) {
        Write-Ok "IAM user '$UserName' already has $($existingKeys.AccessKeyMetadata.Count) access key(s) - not creating another"
        Write-Host "  If you need the secret again, delete the old key (aws iam delete-access-key) and re-run this script," -ForegroundColor DarkYellow
        Write-Host "  or use 'aws iam create-access-key' directly, then update the Supabase secret by hand." -ForegroundColor DarkYellow
        return $null
    }
    if ($PSCmdlet.ShouldProcess($UserName, "create access key")) {
        $key = Invoke-Aws -Arguments @("iam", "create-access-key", "--user-name", $UserName)
        Write-Made "access key for '$UserName' (AccessKeyId: $($key.AccessKey.AccessKeyId))"
        return $key.AccessKey
    }
    return $null
}

function Ensure-BrokerRoleAndCaller {
    param([Parameter(Mandatory)][string[]]$BucketNames)

    $accountId = Get-AccountId
    $callerArn = Ensure-IamUser -UserName $BrokerCallerName
    if (-not $callerArn) {
        # -WhatIf path: fabricate a placeholder ARN so the trust policy JSON below is
        # still well-formed for a dry-run preview.
        $callerArn = "arn:aws:iam::${accountId}:user/$BrokerCallerName"
    }

    # ---- bloom-teams-broker role: trust policy lets ONLY the assume-only caller
    #      user assume it. ------------------------------------------------------
    $trustPolicy = @{
        Version   = "2012-10-17"
        Statement = @(
            @{
                Effect    = "Allow"
                Principal = @{ AWS = $callerArn }
                Action    = "sts:AssumeRole"
            }
        )
    } | ConvertTo-Json -Depth 10 -Compress

    $existingRole = Invoke-Aws -AllowFailure -Arguments @("iam", "get-role", "--role-name", $BrokerRoleName)
    $roleArn = $null
    if ($existingRole) {
        Write-Ok "IAM role '$BrokerRoleName' already exists"
        $roleArn = $existingRole.Role.Arn
        if ($PSCmdlet.ShouldProcess($BrokerRoleName, "refresh trust policy (idempotent)")) {
            $tempFile = [System.IO.Path]::GetTempFileName()
            Set-Content -Path $tempFile -Value $trustPolicy -Encoding utf8NoBOM
            Invoke-Aws -Arguments @(
                "iam", "update-assume-role-policy", "--role-name", $BrokerRoleName,
                "--policy-document", "file://$tempFile"
            ) | Out-Null
            Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        }
    } elseif ($PSCmdlet.ShouldProcess($BrokerRoleName, "create IAM role")) {
        $tempFile = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $tempFile -Value $trustPolicy -Encoding utf8NoBOM
        $created = Invoke-Aws -Arguments @(
            "iam", "create-role", "--role-name", $BrokerRoleName,
            "--assume-role-policy-document", "file://$tempFile",
            "--description", "Bloom Cloud Team Collections - assumed by edge functions to mint per-request, per-book-scoped S3 credentials (see supabase/functions/_shared/s3.ts)"
        )
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        $roleArn = $created.Role.Arn
        Write-Made "IAM role '$BrokerRoleName'"
    }

    # ---- Role's own permission policy: the CEILING of what any minted session
    #      credential can ever do, regardless of the per-request session Policy the
    #      edge function passes to AssumeRole (see s3.ts's buildSessionPolicy - that
    #      narrows further to one book/collection-files prefix; it can never widen
    #      beyond what's granted here). ------------------------------------------
    $bucketArns          = $BucketNames | ForEach-Object { "arn:aws:s3:::$_" }
    $bucketObjectArns    = $BucketNames | ForEach-Object { "arn:aws:s3:::$_/*" }
    $rolePermissionPolicy = @{
        Version   = "2012-10-17"
        Statement = @(
            @{
                Sid      = "BrokerScopedObjectAccess"
                Effect   = "Allow"
                Action   = @(
                    "s3:PutObject", "s3:GetObject", "s3:GetObjectVersion",
                    "s3:AbortMultipartUpload", "s3:ListMultipartUploadParts"
                )
                Resource = $bucketObjectArns
            }
        )
    } | ConvertTo-Json -Depth 10 -Compress

    if ($PSCmdlet.ShouldProcess($BrokerRoleName, "attach/refresh inline permission policy")) {
        $tempFile = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $tempFile -Value $rolePermissionPolicy -Encoding utf8NoBOM
        Invoke-Aws -Arguments @(
            "iam", "put-role-policy", "--role-name", $BrokerRoleName,
            "--policy-name", "bloom-teams-broker-s3-access",
            "--policy-document", "file://$tempFile"
        ) | Out-Null
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        Write-Ok "broker role permission policy set (scoped to: $($bucketObjectArns -join ', '))"
    }

    # ---- Assume-only caller user's policy: sts:AssumeRole on the broker role, and
    #      NOTHING else - this is what makes leaking its access key low-risk. ------
    $callerPolicy = @{
        Version   = "2012-10-17"
        Statement = @(
            @{
                Sid      = "AssumeBrokerRoleOnly"
                Effect   = "Allow"
                Action   = "sts:AssumeRole"
                Resource = "arn:aws:iam::${accountId}:role/$BrokerRoleName"
            }
        )
    } | ConvertTo-Json -Depth 10 -Compress

    if ($PSCmdlet.ShouldProcess($BrokerCallerName, "attach/refresh inline assume-only policy")) {
        $tempFile = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $tempFile -Value $callerPolicy -Encoding utf8NoBOM
        Invoke-Aws -Arguments @(
            "iam", "put-user-policy", "--user-name", $BrokerCallerName,
            "--policy-name", "bloom-teams-assume-broker-only",
            "--policy-document", "file://$tempFile"
        ) | Out-Null
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        Write-Ok "assume-only policy set on '$BrokerCallerName'"
    }

    $callerKey = Ensure-AccessKey -UserName $BrokerCallerName
    return @{ RoleArn = $roleArn; CallerAccessKey = $callerKey }
}

function Ensure-AdminUser {
    param([Parameter(Mandatory)][string[]]$BucketNames)

    Ensure-IamUser -UserName $AdminUserName | Out-Null

    # Direct (non-assumed) permissions for server-side-only verification/backup
    # writes - see _shared/s3.ts's adminS3Client() doc comment. GetObjectAttributes
    # covers the ChecksumMode=ENABLED HeadObject readback verifyUploadedObject()
    # relies on; ListBucket is occasionally needed for diagnostics/tooling, not by
    # the edge functions themselves, but cheap to include for an admin identity.
    $bucketArns       = $BucketNames | ForEach-Object { "arn:aws:s3:::$_" }
    $bucketObjectArns = $BucketNames | ForEach-Object { "arn:aws:s3:::$_/*" }
    $adminPolicy = @{
        Version   = "2012-10-17"
        Statement = @(
            @{
                Sid      = "AdminObjectAccess"
                Effect   = "Allow"
                Action   = @("s3:GetObject", "s3:GetObjectVersion", "s3:GetObjectAttributes", "s3:PutObject")
                Resource = $bucketObjectArns
            },
            @{
                Sid      = "AdminListBuckets"
                Effect   = "Allow"
                Action   = @("s3:ListBucket")
                Resource = $bucketArns
            }
        )
    } | ConvertTo-Json -Depth 10 -Compress

    if ($PSCmdlet.ShouldProcess($AdminUserName, "attach/refresh inline admin policy")) {
        $tempFile = [System.IO.Path]::GetTempFileName()
        Set-Content -Path $tempFile -Value $adminPolicy -Encoding utf8NoBOM
        Invoke-Aws -Arguments @(
            "iam", "put-user-policy", "--user-name", $AdminUserName,
            "--policy-name", "bloom-teams-admin-s3-access",
            "--policy-document", "file://$tempFile"
        ) | Out-Null
        Remove-Item $tempFile -Force -ErrorAction SilentlyContinue
        Write-Ok "admin policy set on '$AdminUserName' (scoped to: $($bucketObjectArns -join ', '))"
    }

    return Ensure-AccessKey -UserName $AdminUserName
}

# ===========================================================================
# Main
# ===========================================================================
$bucketNames = $Environments | ForEach-Object { "bloom-teams-$_" }

Write-Step "S3 buckets: $($bucketNames -join ', ')"
foreach ($bucket in $bucketNames) {
    Ensure-Bucket -BucketName $bucket
    Ensure-BucketVersioning -BucketName $bucket
    Ensure-PublicAccessBlocked -BucketName $bucket
    Ensure-LifecycleRules -BucketName $bucket
}

Write-Step "IAM: broker role + assume-only caller user"
$brokerResult = Ensure-BrokerRoleAndCaller -BucketNames $bucketNames

Write-Step "IAM: admin user (server-side verification + manifest backups)"
$adminKey = Ensure-AdminUser -BucketNames $bucketNames

# ---------------------------------------------------------------------------
# Final summary: exact `supabase secrets set` commands to wire the edge functions
# up to what was just provisioned. Only prints real values for keys created THIS
# run (Ensure-AccessKey returns $null when a key already existed - see its comment).
# ---------------------------------------------------------------------------
Write-Step "Supabase secrets"
Write-Host "Run against the hosted Supabase project (once it exists - see IMPLEMENTATION.md's" -ForegroundColor Cyan
Write-Host "'Deferred until real infrastructure is available' list):" -ForegroundColor Cyan
Write-Host ""
Write-Host "  supabase secrets set BLOOM_CLOUD_LOCAL_MODE=false"
Write-Host "  supabase secrets set BLOOM_TEAMS_BROKER_ROLE_ARN=$($brokerResult.RoleArn)"
Write-Host "  supabase secrets set BLOOM_S3_REGION=$Region"
Write-Host "  supabase secrets set BLOOM_S3_BUCKET=<bloom-teams-production or bloom-teams-sandbox, per project>"
Write-Host "  supabase secrets set BLOOM_S3_ENDPOINT=https://s3.$Region.amazonaws.com"
Write-Host "  supabase secrets set BLOOM_S3_FORCE_PATH_STYLE=false"
if ($brokerResult.CallerAccessKey) {
    Write-Host "  supabase secrets set AWS_ACCESS_KEY_ID=$($brokerResult.CallerAccessKey.AccessKeyId)"
    Write-Host "  supabase secrets set AWS_SECRET_ACCESS_KEY=$($brokerResult.CallerAccessKey.SecretAccessKey)"
} else {
    Write-Host "  supabase secrets set AWS_ACCESS_KEY_ID=<from the existing '$BrokerCallerName' access key>"
    Write-Host "  supabase secrets set AWS_SECRET_ACCESS_KEY=<ditto - not re-displayable; see the warning above>"
}
if ($adminKey) {
    Write-Host "  supabase secrets set BLOOM_S3_ADMIN_ACCESS_KEY=$($adminKey.AccessKeyId)"
    Write-Host "  supabase secrets set BLOOM_S3_ADMIN_SECRET_KEY=$($adminKey.SecretAccessKey)"
} else {
    Write-Host "  supabase secrets set BLOOM_S3_ADMIN_ACCESS_KEY=<from the existing '$AdminUserName' access key>"
    Write-Host "  supabase secrets set BLOOM_S3_ADMIN_SECRET_KEY=<ditto>"
}
Write-Host ""
Write-Host "AWS_REGION is read by the STS client for the broker role assume call (defaults to"
Write-Host "us-east-1 in _shared/env.ts if unset) - only set it explicitly if this Region differs:"
Write-Host "  supabase secrets set AWS_REGION=$Region"
Write-Host ""
Write-Host "Capture any freshly-printed secret values above NOW - AWS will not show them again." -ForegroundColor Red
