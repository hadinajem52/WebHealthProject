[CmdletBinding()]
param([switch] $UseTestcontainers)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WebHealthProject.sln'
$infrastructure = Join-Path $root 'src\WebHealth.Infrastructure'

npm ci --prefix $root --ignore-scripts
if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency restore failed.' }

npm run vendor --prefix $root
if ($LASTEXITCODE -ne 0) { throw 'Frontend asset vendoring failed.' }

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

    # The application binds a pre-built model, so a model change that is not regenerated here is
    # a change the running application never sees: it would keep querying the previous shape and
    # fail at runtime against a schema the migration had already moved. Regenerating and asking
    # git whether anything moved is what keeps the generated copy honest.
    dotnet ef dbcontext optimize `
        --project $infrastructure `
        --startup-project $infrastructure `
        --configuration Release `
        --no-build `
        --output-dir Persistence/CompiledModels `
        --namespace WebHealth.Infrastructure.Persistence.CompiledModels
    if ($LASTEXITCODE -ne 0) { throw 'Compiled model regeneration failed.' }

    git -C $root diff --quiet -- 'src/WebHealth.Infrastructure/Persistence/CompiledModels'
    if ($LASTEXITCODE -ne 0) {
        throw 'Compiled model is stale. Regenerate it with dotnet ef dbcontext optimize and commit the result.'
    }
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
