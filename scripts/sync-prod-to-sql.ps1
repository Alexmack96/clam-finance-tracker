<#
.SYNOPSIS
    Pulls the live production SQLite database off Railway, dumps it to CSV, and
    inserts into SQL Server whatever is not already there.

.DESCRIPTION
    Three steps, and each can be skipped:

      1. Download /data/prod.db off the Railway volume (skip with -SkipDownload).
      2. Dump every migrated table to CSV under -CsvDir.
      3. Insert the rows the target does not already have.

    Step 3 only ever inserts. A row already present is left as it is, so a note
    written or a category corrected on the target survives every re-run. The cost
    is the other direction: an edit made in production after a row was copied does
    not follow it, and a row deleted in production is not deleted here.

    This is not the cutover. That is api-dotnet/tools/Clam.Migrate, which runs
    once into an empty database and refuses a target that already holds rows.

.PARAMETER Target
    SQL Server connection string. Falls back to $env:CLAM_SQL_CONNECTION, then to
    ConnectionStrings:ClamFinance in Clam.AppHost's user-secrets.

.PARAMETER DryRun
    Do the whole load and roll it back, printing exactly what would be inserted.
    Run this first.

.EXAMPLE
    ./scripts/sync-prod-to-sql.ps1 -DryRun
    ./scripts/sync-prod-to-sql.ps1
#>
[CmdletBinding()]
param(
    [string] $Target,
    [string] $Sqlite = 'prod.db',
    [string] $CsvDir = '.sync/prod-csv',
    [switch] $DryRun,
    [switch] $SkipDownload,
    [switch] $DumpOnly
)

$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

# ── 1. Pull the snapshot ──────────────────────────────────────────────────────
#
# The npm global bin is not reliably on PATH, especially in a non-interactive
# shell, so the CLI is always invoked by full path. And this has to be PowerShell
# rather than Git Bash: MSYS rewrites the leading /data/... remote path into a
# Windows path and the download 404s. See the db-snapshot skill.
if (-not $SkipDownload) {
    $railway = "$env:APPDATA\npm\node_modules\@railway\cli\bin\railway.exe"
    if (-not (Test-Path $railway)) {
        $railway = (Get-Command railway -ErrorAction SilentlyContinue).Source
    }
    if (-not $railway) {
        throw "Railway CLI not found. npm i -g @railway/cli, or pass -SkipDownload to use the $Sqlite already on disk."
    }

    Write-Host "Downloading /data/prod.db from the Railway volume..." -ForegroundColor Cyan

    Remove-Item $Sqlite, "$Sqlite-wal", "$Sqlite-shm" -ErrorAction SilentlyContinue
    & $railway volume files --volume '@helpdesk/server-volume' download /data/prod.db "./$Sqlite" --overwrite
    if ($LASTEXITCODE -ne 0) { throw "railway volume files download failed with exit code $LASTEXITCODE." }
}

if (-not (Test-Path $Sqlite)) { throw "No $Sqlite on disk." }

# A failed download leaves a 0-byte file rather than no file, and every step after
# this would report an empty but successful sync.
$size = (Get-Item $Sqlite).Length
if ($size -lt 100KB) { throw "$Sqlite is only $size bytes — the download did not land whole." }
Write-Host ("{0} is {1:N0} bytes." -f $Sqlite, $size) -ForegroundColor DarkGray

# ── 2. Resolve the target ─────────────────────────────────────────────────────
#
# Never committed anywhere. The AppHost's user-secrets is where it actually lives
# (CLAUDE.md), so read it from there rather than making the caller paste it in.
if (-not $DumpOnly -and -not $Target) {
    $Target = $env:CLAM_SQL_CONNECTION
}

if (-not $DumpOnly -and -not $Target) {
    $secrets = & dotnet user-secrets list --project api-dotnet/Clam.AppHost 2>$null
    $line = $secrets | Where-Object { $_ -like 'ConnectionStrings:ClamFinance = *' } | Select-Object -First 1
    if ($line) { $Target = $line -replace '^ConnectionStrings:ClamFinance = ', '' }
}

if (-not $DumpOnly -and -not $Target) {
    throw @'
No connection string. Pass -Target, set $env:CLAM_SQL_CONNECTION, or put it in
user-secrets:

    dotnet user-secrets set "ConnectionStrings:ClamFinance" "<connection string>" --project api-dotnet/Clam.AppHost
'@
}

# ── 3. Dump and load ──────────────────────────────────────────────────────────
$arguments = @('--sqlite', $Sqlite, '--csv', $CsvDir)
if ($DumpOnly) { $arguments += '--dump-only' } else { $arguments += @('--target', $Target) }
if ($DryRun) { $arguments += '--dry-run' }

& dotnet run --project api-dotnet/tools/Clam.Sync -- @arguments
if ($LASTEXITCODE -ne 0) { throw "Clam.Sync failed with exit code $LASTEXITCODE." }

Write-Host ''
Write-Host "CSVs are in $CsvDir (gitignored — real financial data)." -ForegroundColor DarkGray
if ($DryRun) { Write-Host 'Dry run. Re-run without -DryRun to write.' -ForegroundColor Yellow }
