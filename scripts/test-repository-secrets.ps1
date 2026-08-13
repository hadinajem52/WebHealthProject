[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pattern = '(password|secret|token|api[_-]?key)\s*[:=]\s*["''][^"'']+["'']'

rg --line-number --ignore-case `
    --hidden `
    --glob '!.git/**' `
    --glob '!**/bin/**' `
    --glob '!**/obj/**' `
    --glob '!TestResults/**' `
    --glob '!**/packages.lock.json' `
    $pattern $root

if ($LASTEXITCODE -eq 0) { throw 'A possible committed secret was found.' }
if ($LASTEXITCODE -gt 1) { throw 'Repository secret-pattern scan failed.' }
$global:LASTEXITCODE = 0
