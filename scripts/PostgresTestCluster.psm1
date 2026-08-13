function Start-PostgresTestCluster {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [int] $Port,
        [Parameter(Mandatory)] [ref] $Cluster
    )

    $postgresBin = 'C:\Program Files\PostgreSQL\18\bin'
    if (-not (Test-Path -LiteralPath $postgresBin)) {
        throw "PostgreSQL 18 binaries were not found at $postgresBin."
    }

    $spikesRoot = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot '.spikes'))
    $work = [IO.Path]::GetFullPath((Join-Path $spikesRoot $Name))
    $expectedPrefix = $spikesRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $work.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PostgreSQL test work path must remain under $spikesRoot."
    }

    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
    New-Item -ItemType Directory -Path $work | Out-Null

    $data = Join-Path $work 'data'
    $log = Join-Path $work 'postgres.log'
    $pgCtl = Join-Path $postgresBin 'pg_ctl.exe'

    & (Join-Path $postgresBin 'initdb.exe') -D $data -U postgres -A trust --no-locale --encoding=UTF8
    if ($LASTEXITCODE -ne 0) { throw 'initdb failed.' }

    & $pgCtl -D $data -l $log -o "-p $Port -h 127.0.0.1" -w start
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL test cluster failed to start.' }

    $Cluster.Value = [PSCustomObject]@{
        Bin = $postgresBin
        Data = $data
        PgCtl = $pgCtl
        Port = $Port
        AdminConnectionString = "Host=127.0.0.1;Port=$Port;Database=postgres;Username=postgres;SSL Mode=Disable;Pooling=false"
    }
}

function Stop-PostgresTestCluster {
    [CmdletBinding()]
    param([Parameter(Mandatory)] $Cluster)

    if (Test-Path -LiteralPath (Join-Path $Cluster.Data 'postmaster.pid')) {
        & $Cluster.PgCtl -D $Cluster.Data -m fast -w stop
    }
}

Export-ModuleMember -Function Start-PostgresTestCluster, Stop-PostgresTestCluster
