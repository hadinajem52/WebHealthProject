[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$postgresBin = 'C:\Program Files\PostgreSQL\18\bin'
$work = Join-Path $root '.spikes\postgres'
$data = Join-Path $work 'data'
$log = Join-Path $work 'postgres.log'
$port = 6543
$pgCtl = Join-Path $postgresBin 'pg_ctl.exe'

if (-not (Test-Path -LiteralPath $postgresBin)) {
    throw "PostgreSQL 18 binaries were not found at $postgresBin."
}

if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
}
New-Item -ItemType Directory -Path $work | Out-Null

try {
    & (Join-Path $postgresBin 'initdb.exe') -D $data -U postgres -A trust --no-locale --encoding=UTF8
    if ($LASTEXITCODE -ne 0) { throw 'initdb failed.' }

    & $pgCtl -D $data -l $log -o "-p $port -h 127.0.0.1" -w start
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL spike cluster failed to start.' }

    $env:SPIKE_POSTGRES = "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;SSL Mode=Disable;Pooling=false"
    dotnet restore (Join-Path $root 'tests\FeasibilitySpikes\FeasibilitySpikes.csproj') --use-lock-file
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet test (Join-Path $root 'tests\FeasibilitySpikes\FeasibilitySpikes.csproj') --no-restore --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Feasibility spikes failed.' }
}
finally {
    Remove-Item Env:SPIKE_POSTGRES -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath (Join-Path $data 'postmaster.pid')) {
        & $pgCtl -D $data -m fast -w stop
    }
}
