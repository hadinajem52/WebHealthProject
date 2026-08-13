[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WebHealthProject.sln'

dotnet restore $solution --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked restore failed.' }

dotnet build $solution --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

dotnet test $solution --no-build --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Automated tests failed.' }
