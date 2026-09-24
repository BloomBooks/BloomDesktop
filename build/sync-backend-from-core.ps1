#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Refreshes this branch's TEST-ONLY mirror of the Cloud Team Collections backend from a
    bloom-core-supabase checkout.

.DESCRIPTION
    The Team Collections backend (the `tc` schema, its pgTAP tests, the TC edge functions and
    their Deno tests, the backend docs) is developed in BloomBooks/bloom-core-supabase. This
    branch carries a copy only so the client's live tests and the E2E harness can run a local
    stack from this repo. Never edit the mirrored files here; change them in bloom-core-supabase
    and re-run this script.

    The copy is exact except for a fixed set of path rewrites, because the non-Supabase parts of
    the backend live in different places in the two repos (e.g. team-collections/dev/ there is
    server/dev/ here). supabase/config.toml and the rest of server/dev/ are NOT mirrored: they
    differ on purpose (project_id, which names the MinIO docker network, and the seed path).

    Afterwards, run build/regen-init-migration.sh and confirm it leaves the migration unchanged.

.PARAMETER CoreRepo
    Path to the bloom-core-supabase checkout to copy from. It must have no uncommitted changes.
#>
param(
    [Parameter(Mandatory)][string]$CoreRepo
)
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$CoreRepo = (Resolve-Path $CoreRepo).Path

$dirty = git -C $CoreRepo status --porcelain
if ($dirty) { throw "$CoreRepo has uncommitted changes; commit or discard them first." }
$commit = (git -C $CoreRepo rev-parse --short HEAD).Trim()
$subject = (git -C $CoreRepo log -1 --format=%s).Trim()

$tcFunctions = @(
    'checkin-abort', 'checkin-finish', 'checkin-start',
    'collection-files-finish', 'collection-files-start',
    'download-start', 'sweep-stale-uploads'
)

# Where bloom-core-supabase paths live in this repo. Applied to the content of every copied file.
$rewrites = [ordered]@{
    'team-collections/regen-init-migration.sh' = 'build/regen-init-migration.sh'
    'team-collections/aws/provision-aws.ps1'   = 'server/provision-aws.ps1'
    'team-collections\aws\provision-aws.ps1'   = 'server\provision-aws.ps1'
    'team-collections/dev/'                    = 'server/dev/'
    'team-collections/docs/'                   = 'Design/CloudTeamCollections/'
    'supabase/seeds/tc_dev.sql'                = 'server/dev/seed.sql'
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# Copies one file from the core repo to this repo, applying the path rewrites.
function Copy-Mirrored([string]$from, [string]$to) {
    $source = Join-Path $CoreRepo $from
    $dest = Join-Path $repoRoot $to
    $text = [System.IO.File]::ReadAllText($source, $utf8NoBom)
    foreach ($key in $rewrites.Keys) { $text = $text.Replace($key, $rewrites[$key]) }
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    [System.IO.File]::WriteAllText($dest, $text, $utf8NoBom)
}

# Copies every file directly in (or, with -Recurse, under) a core-repo folder.
function Copy-MirroredDir([string]$from, [string]$to, [string]$filter = '*', [switch]$Recurse) {
    $sourceDir = Join-Path $CoreRepo $from
    foreach ($file in Get-ChildItem $sourceDir -File -Filter $filter -Recurse:$Recurse) {
        $relative = $file.FullName.Substring($sourceDir.Length + 1)
        Copy-Mirrored (Join-Path $from $relative) (Join-Path $to $relative)
    }
}

# Clear out what we own, so files deleted upstream disappear here too.
foreach ($owned in 'supabase/schemas', 'supabase/migrations', 'supabase/tests') {
    $path = Join-Path $repoRoot $owned
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
$functionsDir = Join-Path $repoRoot 'supabase/functions'
Get-ChildItem $functionsDir -Force | Where-Object { $_.Name -ne '.gitkeep' } | Remove-Item -Recurse -Force

Copy-MirroredDir 'supabase/schemas/tc' 'supabase/schemas/tc'
Copy-MirroredDir 'supabase/migrations' 'supabase/migrations'
Copy-MirroredDir 'supabase/tests' 'supabase/tests' '*.sql'
Copy-MirroredDir 'supabase/functions/_shared/tc' 'supabase/functions/_shared/tc' -Recurse
Copy-MirroredDir 'supabase/functions/tests' 'supabase/functions/tests' 'tc-*-test.ts'
foreach ($fn in $tcFunctions) { Copy-MirroredDir "supabase/functions/$fn" "supabase/functions/$fn" -Recurse }
# The root deno.json is what `deno test`/`deno check` use; tc-deno-config-test.ts checks that
# each function's own deno.json pins the same versions.
Copy-Mirrored 'deno.json' 'deno.json'
Copy-Mirrored 'deno.lock' 'deno.lock'
Copy-Mirrored 'team-collections/regen-init-migration.sh' 'build/regen-init-migration.sh'
Copy-Mirrored 'supabase/seeds/tc_dev.sql' 'server/dev/seed.sql'
Copy-Mirrored 'team-collections/aws/provision-aws.ps1' 'server/provision-aws.ps1'
foreach ($doc in 'CONTRACTS.md', 'SCHEMA.md', 'GOING-LIVE.md') {
    Copy-Mirrored "team-collections/docs/$doc" "Design/CloudTeamCollections/$doc"
}

$readme = @"
# Cloud Team Collections backend: TEST-ONLY MIRROR

Everything under ``schemas/``, ``migrations/``, ``tests/`` and ``functions/`` here (plus the root
``deno.json``/``deno.lock``, ``build/regen-init-migration.sh``, ``server/dev/seed.sql``,
``server/provision-aws.ps1`` and ``Design/CloudTeamCollections/{CONTRACTS,SCHEMA,GOING-LIVE}.md``)
is a copy of the backend in **BloomBooks/bloom-core-supabase**, taken at commit **$commit**
("$subject"). It is here only so the client's live tests and the E2E harness
(``src/BloomTests/e2e``) can run a local stack from this repo.

**Do not edit these files here.** Make changes in bloom-core-supabase, then refresh the mirror:

``````powershell
build/sync-backend-from-core.ps1 -CoreRepo <path to a clean bloom-core-supabase checkout>
bash build/regen-init-migration.sh   # must leave supabase/migrations unchanged
``````

The copy is exact apart from path rewrites (``team-collections/dev/`` there is ``server/dev/``
here, and so on; see the script). ``config.toml`` and the rest of ``server/dev/`` are maintained
here by hand: this repo's ``project_id`` (which names the Docker network MinIO joins) and seed
path differ from bloom-core-supabase's.
"@
[System.IO.File]::WriteAllText((Join-Path $repoRoot 'supabase/README.md'), $readme.Replace("`r`n", "`n") + "`n", $utf8NoBom)

# bloom-core-supabase's deno.lock also covers its non-TC functions. Type-checking everything we
# mirrored makes Deno prune the lock down to what the TC code uses (and proves it still checks).
Push-Location $repoRoot
try {
    $targets = @((Get-ChildItem 'supabase/functions/*/index.ts').FullName) +
        @((Get-ChildItem 'supabase/functions/tests/tc-*-test.ts').FullName)
    & deno check @targets
    if ($LASTEXITCODE -ne 0) { throw "deno check failed on the mirrored functions/tests" }
}
finally { Pop-Location }

Write-Host "Mirrored bloom-core-supabase $commit ($subject) into $repoRoot"
