[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pattern = '(password|secret|token|api[_-]?key)\s*[:=]\s*["''][^"'']+["'']'
$repositoryFiles = git -C $root ls-files --cached --others --exclude-standard
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate repository files.' }

$matches = foreach ($relativePath in $repositoryFiles) {
    if ($relativePath -match '(^|/)(bin|obj|TestResults)/' -or
        $relativePath -match 'packages\.lock\.json$') {
        continue
    }

    $fullPath = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    Select-String -LiteralPath $fullPath -Pattern $pattern | ForEach-Object {
        "${relativePath}:$($_.LineNumber):$($_.Line.Trim())"
    }
}

if ($matches.Count -gt 0) {
    $matches | Write-Host
    throw 'A possible committed secret was found.'
}
