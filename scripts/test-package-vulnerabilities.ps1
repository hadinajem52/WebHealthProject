[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WebHealthProject.sln'
$acceptedAdvisory = 'https://github.com/advisories/GHSA-q939-rpr3-3284'

$auditJson = dotnet list $solution package --vulnerable --include-transitive --format json | Out-String
if ($LASTEXITCODE -ne 0) { throw 'Package vulnerability audit failed.' }

$audit = $auditJson | ConvertFrom-Json
$vulnerabilities = @(
    foreach ($project in $audit.projects) {
        foreach ($framework in @($project.frameworks | Where-Object { $null -ne $_ })) {
            $packages = @($framework.topLevelPackages | Where-Object { $null -ne $_ }) +
                @($framework.transitivePackages | Where-Object { $null -ne $_ })
            foreach ($package in $packages) {
                foreach ($vulnerability in @($package.vulnerabilities | Where-Object { $null -ne $_ })) {
                    [PSCustomObject]@{
                        Package = $package.id
                        Version = $package.resolvedVersion
                        Severity = $vulnerability.severity
                        Advisory = $vulnerability.advisoryurl
                    }
                }
            }
        }
    }
)

$unexpected = @($vulnerabilities | Where-Object {
    $_.Advisory -ne $acceptedAdvisory -or $_.Package -ne 'SSH.NET'
})
if ($unexpected.Count -gt 0) {
    $unexpected | Format-Table | Out-String | Write-Host
    throw 'An unaccepted vulnerable package was found.'
}

foreach ($vulnerability in $vulnerabilities) {
    Write-Host "Accepted vulnerability: $($vulnerability.Package) $($vulnerability.Version) $($vulnerability.Advisory)"
}
