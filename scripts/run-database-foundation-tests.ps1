[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'PostgresTestCluster.psm1') -Force

$testProject = Join-Path $root 'tests\WebHealth.IntegrationTests\WebHealth.IntegrationTests.csproj'
$infrastructure = Join-Path $root 'src\WebHealth.Infrastructure'

$build = Start-Job -Name 'build' -ScriptBlock {
    param($root, $testProject)

    Set-Location $root
    dotnet tool restore 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
    dotnet restore (Join-Path $root 'WebHealthProject.sln') --locked-mode 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }
    dotnet build $testProject --configuration Release --no-restore 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
} -ArgumentList $root, $testProject

$cluster = $null
$migrations = $null
try {
    Start-PostgresTestCluster -RepositoryRoot $root -Name 'database-foundation' -Port 6544 -Cluster ([ref] $cluster) `
        -ServerSetting @('fsync=off', 'synchronous_commit=off', 'full_page_writes=off', 'max_connections=300')

    foreach ($database in @('webhealth_foundation', 'webhealth_migrations')) {
        & (Join-Path $cluster.Bin 'createdb.exe') -h 127.0.0.1 -p $cluster.Port -U postgres $database
        if ($LASTEXITCODE -ne 0) { throw "Creating $database failed." }
    }

    $prefix = "Host=127.0.0.1;Port=$($cluster.Port);Username=postgres;SSL Mode=Disable;Pooling=false"
    $env:WEBHEALTH_TEST_POSTGRES = "$prefix;Database=webhealth_foundation"

    Receive-Job -Job $build -Wait | Write-Host
    if ($build.State -ne 'Completed') { throw "Build job $($build.State)." }
    Remove-Job -Job $build
    $build = $null

    $migrations = Start-Job -Name 'migrations' -ScriptBlock {
        param($infrastructure, $connectionString)

        $env:WEBHEALTH_MIGRATIONS_CONNECTION = $connectionString
        dotnet ef database update --project $infrastructure --startup-project $infrastructure `
            --configuration Release --no-build 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Explicit migration update failed.' }
    } -ArgumentList $infrastructure, "$prefix;Database=webhealth_migrations"

    dotnet test $testProject --configuration Release --no-build --filter 'FullyQualifiedName~DatabaseFoundationTests' --logger 'console;verbosity=normal'
    if ($LASTEXITCODE -ne 0) { throw 'Database foundation integration test failed.' }

    Receive-Job -Job $migrations -Wait | Write-Host
    if ($migrations.State -ne 'Completed') { throw "Migration job $($migrations.State)." }
    Remove-Job -Job $migrations
    $migrations = $null
}
finally {
    foreach ($job in @($build, $migrations)) {
        if ($job) { Stop-Job -Job $job -ErrorAction SilentlyContinue; Remove-Job -Job $job -Force -ErrorAction SilentlyContinue }
    }
    Remove-Item Env:WEBHEALTH_TEST_POSTGRES -ErrorAction SilentlyContinue
    if ($cluster) { Stop-PostgresTestCluster -Cluster $cluster }
}
