<#
.SYNOPSIS
    Side-by-side table of runs written by Measure-Taskitify.ps1: median/max per phase for memory
    (MB) and the mean for CPU (% of the machine).

.EXAMPLE
    .\Compare-Runs.ps1 $env:TEMP\tkperf\results\baseline-* $env:TEMP\tkperf\results\software-*
#>
param([Parameter(Mandatory, ValueFromRemainingArguments)][string[]]$Runs)

$ErrorActionPreference = 'Stop'
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
$metrics = 'app_priv_mb', 'app_pws_mb', 'wv_priv_mb', 'wv_pws_mb', 'total_priv_mb', 'total_pws_mb', 'app_cpu_pct', 'wv_cpu_pct'

function Get-Median([double[]]$Values) {
    $sorted = $Values | Sort-Object
    $sorted[[int][math]::Floor(($sorted.Count - 1) / 2)]
}

$dirs = $Runs | ForEach-Object { Get-Item $_ } | Where-Object { Test-Path (Join-Path $_.FullName 'samples.csv') }
$table = foreach ($dir in $dirs) {
    $rows = @(Import-Csv (Join-Path $dir.FullName 'samples.csv') | ForEach-Object {
        foreach ($m in $metrics + 't') { $_.$m = if ($_.$m -ne '') { [double]$_.$m } else { $null } }
        $_
    })
    $end = $rows[-1].t
    # Same phases as the summary: skips are tagged on the first sample after them.
    $skips = @($rows | Where-Object { $_.events -like '*skip*' } | ForEach-Object { $_.t })
    $phases = if ($skips.Count -gt 0) {
        @(@{ Name = '1 startup'; From = 0; To = 150 }, @{ Name = '2 idle'; From = 150; To = $skips[0] - 5 },
          @{ Name = '3 changes'; From = $skips[0] - 5; To = $skips[-1] + 40 }, @{ Name = '4 post'; From = $end - 59; To = $end + 1 })
    } else {
        @(@{ Name = '1 startup'; From = 0; To = 150 }, @{ Name = '2 idle'; From = 150; To = $end + 1 })
    }
    foreach ($phase in $phases) {
        $slice = @($rows | Where-Object { $_.t -ge $phase.From -and $_.t -lt $phase.To })
        if ($slice.Count -eq 0) { continue }
        $row = [ordered]@{ phase = $phase.Name; run = $dir.Name; n = $slice.Count }
        foreach ($m in $metrics) {
            $values = @($slice | Where-Object { $_.$m -ne $null } | ForEach-Object { $_.$m })
            $row[$m] = if ($values.Count -eq 0) { '-' }
                elseif ($m -like '*cpu*') { '{0:0.000}' -f ($values | Measure-Object -Average).Average }
                else { '{0:0.0}/{1:0.0}' -f (Get-Median $values), ($values | Measure-Object -Maximum).Maximum }
        }
        [pscustomobject]$row
    }
}
$table | Sort-Object phase, run | Format-Table -AutoSize | Out-String -Width 300
