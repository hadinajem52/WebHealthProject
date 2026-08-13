[CmdletBinding()]
param([switch] $UseTestcontainers)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WebHealthProject.sln'
$infrastructure = Join-Path $root 'src\WebHealth.Infrastructure'

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }

dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

dotnet format $solution --no-restore --verify-no-changes
if ($LASTEXITCODE -ne 0) { throw 'Formatting check failed.' }

dotnet build $solution --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

$env:WEBHEALTH_MIGRATIONS_CONNECTION = 'Host=localhost;Database=webhealth_migrations;Username=postgres'
try {
    dotnet ef migrations has-pending-model-changes `
        --project $infrastructure `
        --startup-project $infrastructure `
        --configuration Release `
        --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Migration drift check failed.' }
}
finally {
    Remove-Item Env:WEBHEALTH_MIGRATIONS_CONNECTION -ErrorAction SilentlyContinue
}

if ($UseTestcontainers) {
    $env:WEBHEALTH_TESTCONTAINERS = 'true'
}
try {
    dotnet test $solution `
        --configuration Release `
        --no-build `
        --no-restore `
        --logger 'trx' `
        --results-directory (Join-Path $root 'TestResults')
    if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }
}
finally {
    if ($UseTestcontainers) {
        Remove-Item Env:WEBHEALTH_TESTCONTAINERS -ErrorAction SilentlyContinue
    }
}

& (Join-Path $PSScriptRoot 'test-package-vulnerabilities.ps1')
& (Join-Path $PSScriptRoot 'test-repository-secrets.ps1')

git -C $root diff --check
if ($LASTEXITCODE -ne 0) { throw 'Whitespace check failed.' }
