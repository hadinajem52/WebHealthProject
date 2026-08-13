[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'PostgresTestCluster.psm1') -Force
$cluster = $null
Start-PostgresTestCluster -RepositoryRoot $root -Name 'postgres' -Port 6543 -Cluster ([ref] $cluster)

try {
    $env:SPIKE_POSTGRES = $cluster.AdminConnectionString
    dotnet restore (Join-Path $root 'tests\FeasibilitySpikes\FeasibilitySpikes.csproj') --use-lock-file
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    dotnet test (Join-Path $root 'tests\FeasibilitySpikes\FeasibilitySpikes.csproj') --no-restore --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Feasibility spikes failed.' }
}
finally {
    Remove-Item Env:SPIKE_POSTGRES -ErrorAction SilentlyContinue
    Stop-PostgresTestCluster -Cluster $cluster
}
