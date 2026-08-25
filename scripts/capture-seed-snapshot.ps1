[CmdletBinding()]
param(
    [string] $PgHost = '127.0.0.1',
    [int] $PgPort = 6432,
    [string] $PgUser = 'postgres',
    [string] $PgPassword,
    [string] $Database = 'webhealth'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'setup\webhealth-seed.dump'

function Find-PgDump {
    $candidatePaths = @()

    foreach ($onPath in @(Get-Command 'pg_dump' -All -ErrorAction SilentlyContinue)) {
        if ($onPath.Source) {
            $candidatePaths += $onPath.Source
        }
    }

    foreach ($searchRoot in @((Join-Path $env:ProgramFiles 'PostgreSQL'), (Join-Path ${env:ProgramFiles(x86)} 'PostgreSQL'))) {
        if (-not $searchRoot -or -not (Test-Path -LiteralPath $searchRoot)) {
            continue
        }

        foreach ($versionDirectory in Get-ChildItem -LiteralPath $searchRoot -Directory -ErrorAction SilentlyContinue) {
            $candidate = Join-Path $versionDirectory.FullName 'bin\pg_dump.exe'
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $candidatePaths += $candidate
            }
        }
    }

    $best = $null
    foreach ($candidatePath in ($candidatePaths | Select-Object -Unique)) {
        $ErrorActionPreference = 'Continue'
        $versionOutput = (& $candidatePath --version 2>&1 | Out-String)
        $ErrorActionPreference = 'Stop'
        $major = 0
        if ($versionOutput -match '(\d+)\.') {
            $major = [int]$Matches[1]
        }

        if (-not $best -or $major -gt $best.Major) {
            $best = [pscustomobject]@{ Path = $candidatePath; Major = $major }
        }
    }

    return $best
}

$pgDump = Find-PgDump
if (-not $pgDump) {
    throw 'pg_dump was not found.'
}

if (-not $PgPassword -and $env:PGPASSWORD) {
    $PgPassword = $env:PGPASSWORD
}

if (-not $PgPassword) {
    $secure = Read-Host -Prompt ("Password for PostgreSQL user '{0}'" -f $PgUser) -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $PgPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

$previousPassword = [Environment]::GetEnvironmentVariable('PGPASSWORD', 'Process')
$env:PGPASSWORD = $PgPassword
$temporary = $target + '.new'

try {
    & $pgDump.Path `
        --host $PgHost `
        --port $PgPort `
        --username $PgUser `
        --dbname $Database `
        --no-password `
        --format=custom `
        --compress=9 `
        --no-owner `
        --no-privileges `
        --file $temporary

    if ($LASTEXITCODE -ne 0) {
        throw 'pg_dump failed.'
    }

    Move-Item -LiteralPath $temporary -Destination $target -Force
}
finally {
    Remove-Item -LiteralPath $temporary -ErrorAction SilentlyContinue
    if ($null -eq $previousPassword) {
        Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    }
    else {
        $env:PGPASSWORD = $previousPassword
    }
}

$sizeMb = [math]::Round((Get-Item -LiteralPath $target).Length / 1MB, 1)
Write-Host ("Captured {0} ({1} MB) from {2} using pg_dump {3}." -f $target, $sizeMb, $Database, $pgDump.Major)
Write-Host 'Commit it so a fresh clone seeds the current data.'
