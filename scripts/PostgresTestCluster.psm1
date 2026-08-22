function Start-PostgresTestCluster {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [int] $Port,
        [Parameter(Mandatory)] [ref] $Cluster,
        [string[]] $ServerSetting = @()
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

    $data = Join-Path $work 'data'
    $log = Join-Path $work 'postgres.log'
    $pgCtl = Join-Path $postgresBin 'pg_ctl.exe'

    # An interrupted run leaves its postmaster alive, still holding the data directory open. The
    # delete below would then block instead of failing, so stop the stale cluster first.
    if (Test-Path -LiteralPath (Join-Path $data 'postmaster.pid')) {
        & $pgCtl -D $data -m immediate -w stop *>&1 | Out-Null
    }

    # A postmaster whose data directory was already deleted keeps the port without leaving a pid
    # file behind, so pg_ctl above cannot see it. Kill anything still serving this data directory.
    Get-CimInstance Win32_Process -Filter "Name='postgres.exe'" |
        Where-Object { $_.CommandLine -and $_.CommandLine.Replace('/', '\') -like "*$data*" } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force
    }
    New-Item -ItemType Directory -Path $work | Out-Null

    if (-not (Test-Path -LiteralPath $spikesRoot)) {
        New-Item -ItemType Directory -Path $spikesRoot | Out-Null
    }

    $initdb = Join-Path $postgresBin 'initdb.exe'
    $template = Join-Path $spikesRoot ("_initdb-" + ((& $initdb --version) -replace '[^\d.]', ''))
    if (-not (Test-Path -LiteralPath (Join-Path $template 'PG_VERSION'))) {
        Remove-Item -LiteralPath $template -Recurse -Force -ErrorAction SilentlyContinue
        $staging = "$template-$PID"
        & $initdb -D $staging -U postgres -A trust --no-locale --encoding=UTF8 --no-sync
        if ($LASTEXITCODE -ne 0) { throw 'initdb failed.' }
        Move-Item -LiteralPath $staging -Destination $template
    }

    robocopy $template $data /E /MT:8 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw 'Copying the initdb template failed.' }

    $serverOptions = (@("-p $Port", '-h 127.0.0.1') +
        ($ServerSetting | ForEach-Object { "-c $_" })) -join ' '

    # The postmaster inherits pg_ctl's standard handles and outlives it. When this script's own
    # stdout is a pipe rather than a console, the daemon holds that pipe open forever and the
    # caller blocks reading it, so the run hangs right after the cluster starts. Start-Process
    # hands pg_ctl its own files instead, leaving nothing of ours for the daemon to keep open.
    $startOut = Join-Path $work 'pg_ctl-start.out'
    $startErr = Join-Path $work 'pg_ctl-start.err'
    # Wait on pg_ctl itself, not on Start-Process -Wait: that form also waits for descendants, and
    # the postmaster it leaves behind is one.
    $start = Start-Process -FilePath $pgCtl `
        -ArgumentList @('-D', "`"$data`"", '-l', "`"$log`"", '-o', "`"$serverOptions`"", '-w', 'start') `
        -NoNewWindow -PassThru -RedirectStandardOutput $startOut -RedirectStandardError $startErr
    $start.WaitForExit()
    Get-Content -LiteralPath $startOut, $startErr -ErrorAction SilentlyContinue | Write-Host
    if ($start.ExitCode -ne 0) { throw 'PostgreSQL test cluster failed to start.' }

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
