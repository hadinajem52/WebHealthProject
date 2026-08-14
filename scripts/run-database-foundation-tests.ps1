[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'PostgresTestCluster.psm1') -Force
$cluster = $null
Start-PostgresTestCluster -RepositoryRoot $root -Name 'database-foundation' -Port 6544 -Cluster ([ref] $cluster)
$databaseName = 'webhealth_foundation'

try {
    & (Join-Path $cluster.Bin 'createdb.exe') -h 127.0.0.1 -p $cluster.Port -U postgres $databaseName
    if ($LASTEXITCODE -ne 0) { throw 'Database creation failed.' }

    $connectionString = "Host=127.0.0.1;Port=$($cluster.Port);Database=$databaseName;Username=postgres;SSL Mode=Disable;Pooling=false"
    $env:WEBHEALTH_MIGRATIONS_CONNECTION = $connectionString
    $env:WEBHEALTH_TEST_POSTGRES = $connectionString

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    dotnet restore (Join-Path $root 'WebHealthProject.sln') --locked-mode
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet test (Join-Path $root 'tests\WebHealth.IntegrationTests\WebHealth.IntegrationTests.csproj') --configuration Release --no-restore --filter 'FullyQualifiedName~DatabaseFoundationTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Database foundation integration test failed.' }
    dotnet ef database update --project (Join-Path $root 'src\WebHealth.Infrastructure') --startup-project (Join-Path $root 'src\WebHealth.Infrastructure') --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Explicit migration update failed.' }
}
finally {
    Remove-Item Env:WEBHEALTH_MIGRATIONS_CONNECTION -ErrorAction SilentlyContinue
    Remove-Item Env:WEBHEALTH_TEST_POSTGRES -ErrorAction SilentlyContinue
    Stop-PostgresTestCluster -Cluster $cluster
}
