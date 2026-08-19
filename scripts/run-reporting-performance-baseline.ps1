<#
.SYNOPSIS
Phase 5 increment 5.7: query plans, reporting index evidence, and the NFR-02 dashboard baseline.

.DESCRIPTION
Starts a disposable PostgreSQL 18 cluster, applies the migrations explicitly, seeds a
representative fleet with ninety days of history, and measures the dashboard and every report
against it. Plans are captured with auto_explain, so what is recorded is the plan for the query
the application actually issued rather than for a hand-copied transcription of it.

The evidence is written to artifacts/phase-5/.
#>
[CmdletBinding()]
param(
    [switch] $KeepCluster,

    # Port and cluster name are parameters because the cluster helper deletes its work directory
    # and re-runs initdb. Two runs sharing a port destroy each other's database mid-measurement,
    # which is indistinguishable from a hung run.
    [int] $Port = 6545,
    [string] $ClusterName = 'reporting-baseline'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'PostgresTestCluster.psm1') -Force
$cluster = $null
Start-PostgresTestCluster -RepositoryRoot $root -Name $ClusterName -Port $Port -Cluster ([ref] $cluster)
$databaseName = 'webhealth_baseline'

try {
    & (Join-Path $cluster.Bin 'createdb.exe') -h 127.0.0.1 -p $cluster.Port -U postgres $databaseName
    if ($LASTEXITCODE -ne 0) { throw 'Database creation failed.' }

    $connectionString = "Host=127.0.0.1;Port=$($cluster.Port);Database=$databaseName;Username=postgres;SSL Mode=Disable;Pooling=false"
    $env:WEBHEALTH_MIGRATIONS_CONNECTION = $connectionString

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    dotnet build (Join-Path $root 'WebHealthProject.sln') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    # Principle 7: the schema is applied explicitly, never implicitly at startup.
    dotnet ef database update --project (Join-Path $root 'src\WebHealth.Infrastructure') --startup-project (Join-Path $root 'src\WebHealth.Infrastructure') --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Explicit migration update failed.' }

    $artifacts = Join-Path $root 'artifacts\phase-5'
    New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

    $env:WEBHEALTH_TEST_POSTGRES_BASELINE = $connectionString
    $env:WEBHEALTH_BASELINE_SERVER_LOG = (Join-Path (Split-Path -Parent $cluster.Data) 'postgres.log')
    $env:WEBHEALTH_BASELINE_EVIDENCE = (Join-Path $artifacts 'reporting-baseline.md')

    dotnet test (Join-Path $root 'tests\WebHealth.IntegrationTests\WebHealth.IntegrationTests.csproj') --configuration Release --no-build --filter 'FullyQualifiedName~ReportingPerformanceBaselineTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Reporting performance baseline failed.' }

    Write-Host "Evidence written to $($env:WEBHEALTH_BASELINE_EVIDENCE)"
}
finally {
    Remove-Item Env:WEBHEALTH_MIGRATIONS_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_TEST_POSTGRES_BASELINE -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_BASELINE_SERVER_LOG -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_BASELINE_EVIDENCE -ErrorAction SilentlyContinue
    if (-not $KeepCluster) { Stop-PostgresTestCluster -Cluster $cluster }
    else { Write-Host "Cluster left running on port $($cluster.Port); stop it with pg_ctl -D $($cluster.Data) -m fast stop" }
}
