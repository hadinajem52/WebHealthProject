[CmdletBinding()]
param(
    [int] $Port = 6546,
    [ValidateRange(1, 60)]
    [int] $MemoryWindowMinutes = 15
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'PostgresTestCluster.psm1') -Force
$cluster = $null
Start-PostgresTestCluster -RepositoryRoot $root -Name 'representative-monitoring' -Port $Port -Cluster ([ref] $cluster)
$databaseName = 'webhealth_representative'

try {
    & (Join-Path $cluster.Bin 'createdb.exe') -h 127.0.0.1 -p $cluster.Port -U postgres $databaseName
    if ($LASTEXITCODE -ne 0) { throw 'Database creation failed.' }
    $connectionString = "Host=127.0.0.1;Port=$($cluster.Port);Database=$databaseName;Username=postgres;SSL Mode=Disable;Pooling=true"
    $env:WEBHEALTH_MIGRATIONS_CONNECTION = $connectionString
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    dotnet build (Join-Path $root 'WebHealthProject.sln') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet ef database update --project (Join-Path $root 'src\WebHealth.Infrastructure') --startup-project (Join-Path $root 'src\WebHealth.Infrastructure') --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Explicit migration update failed.' }
    $env:WEBHEALTH_REPRESENTATIVE_CONNECTION = $connectionString
    $env:WEBHEALTH_REPRESENTATIVE_EVIDENCE = Join-Path $root 'docs\phase-7\Representative_Monitoring_Evidence.md'
    $env:WEBHEALTH_MEMORY_WINDOW_MINUTES = $MemoryWindowMinutes.ToString([System.Globalization.CultureInfo]::InvariantCulture)
    Remove-Item Env:WEBHEALTH_POSTGRESQL_OUTAGE_OBSERVED -ErrorAction SilentlyContinue
    dotnet test (Join-Path $root 'tests\WebHealth.IntegrationTests\WebHealth.IntegrationTests.csproj') --configuration Release --no-build --filter 'FullyQualifiedName~RepresentativeMonitoringLoadTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Representative monitoring evidence failed.' }
    & $cluster.PgCtl -D $cluster.Data -m fast -w stop
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL outage stop failed.' }
    & (Join-Path $cluster.Bin 'pg_isready.exe') -h 127.0.0.1 -p $cluster.Port -q
    if ($LASTEXITCODE -eq 0) { throw 'PostgreSQL remained available during the outage check.' }
    $restartOut = Join-Path (Split-Path -Parent $cluster.Data) 'pg_ctl-restart.out'
    $restartErr = Join-Path (Split-Path -Parent $cluster.Data) 'pg_ctl-restart.err'
    $restart = Start-Process -FilePath $cluster.PgCtl `
        -ArgumentList @('-D', "`"$($cluster.Data)`"", '-l', "`"$(Join-Path (Split-Path -Parent $cluster.Data) 'postgres.log')`"", '-o', "`"-p $($cluster.Port) -h 127.0.0.1`"", '-w', 'start') `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $restartOut -RedirectStandardError $restartErr
    $restart.WaitForExit()
    if ($restart.ExitCode -ne 0) { throw 'PostgreSQL outage restart failed.' }
    $env:WEBHEALTH_POSTGRESQL_OUTAGE_OBSERVED = 'true'
    dotnet test (Join-Path $root 'tests\WebHealth.IntegrationTests\WebHealth.IntegrationTests.csproj') --configuration Release --no-build --filter 'FullyQualifiedName~RestartedPostgresql_ReconcilesTheOutstandingFleetWithoutRepair' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL outage recovery evidence failed.' }
}
finally {
    Remove-Item Env:WEBHEALTH_MIGRATIONS_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_REPRESENTATIVE_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_REPRESENTATIVE_EVIDENCE -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_MEMORY_WINDOW_MINUTES -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_POSTGRESQL_OUTAGE_OBSERVED -ErrorAction SilentlyContinue
    if ($cluster) { Stop-PostgresTestCluster -Cluster $cluster }
}
