<#
.SYNOPSIS
    Measures Taskitify's memory and CPU: the app plus the WebView2 processes of its Spotify web
    session, next to the song playing in Spotify.

.DESCRIPTION
    Every -Interval seconds it records, for Taskitify.exe and for the msedgewebview2.exe processes
    whose command line points at the app's "spotify-web-session" profile:
      * private bytes (commit charge) and private working set (Task Manager's "Memory" column);
      * CPU over the interval as a share of the whole machine, like Task Manager;
      * the Spotify window title, which shows the current song.

    Attach mode (no -Exe) watches the Taskitify that is already running, for -Seconds.

    Scenario mode (-Exe) is reproducible. It starts that build against a throwaway copy of the
    user's data (TASKITIFY_DATA_DIR) with debug logging on and an empty lyrics cache, so every
    song is a real lookup; in the Standard scenario it skips songs through the Windows media
    session; then it stops the app and writes samples.csv, events.txt, app-log.txt and summary.txt.
    The user's settings, Spotify session and cache are only read, never written: the session is
    copied once to <DataRoot>\session-snapshot and every run starts from a fresh copy of it.

    Standard timeline: the current song restarts and the app starts; nothing happens until the
    first of -SkipTimes; then Spotify skips to the next song at each of -SkipTimes (by default
    four quick changes 45 s apart, then two 150 s apart, like songs played to the end); then
    -After s without changes.
    Phases in the summary:
      startup  0-150 s     launch; the first lookup opens the web player (token and search query),
                           which unloads its page 5 s after use and closes 30 s after
      idle     150 s to the first change: nothing happening ("parado")
      changes  first change to 45 s after the last one, each change a cold lookup
      post     the last 60 s, 2.5-3.5 min after the last change ("depois de 2-3 min")
    Each change also gets a line: time to lyrics, web view peak and the lookup's path.

.EXAMPLE
    .\Measure-Taskitify.ps1 -Seconds 300
    Watch the running app for five minutes.

.EXAMPLE
    .\Measure-Taskitify.ps1 -Exe ..\..\publish-spotify\Taskitify.exe -Label baseline -Rewind -StopRunning
    Close the running Taskitify, run the Standard scenario on the published build and go back to
    the song that was playing, so the next run sees the same songs.
#>
[CmdletBinding()]
param(
    [int]$Seconds = 300,
    [string]$Exe,
    [ValidateSet('Standard', 'Idle')]
    [string]$Scenario = 'Standard',
    [double]$Interval = 5,
    [string]$Label = 'run',
    # Short on purpose: Chromium profiles nest ~150 characters deep, and MAX_PATH is 260.
    [string]$DataRoot = (Join-Path $env:TEMP 'tkperf'),
    [string]$OutRoot,
    [int[]]$SkipTimes = @(210, 255, 300, 345, 495, 645),
    [int]$After = 210,
    # Go back to the first skipped song afterwards, so the next run replays the same songs.
    [switch]$Rewind,
    [switch]$RefreshSnapshot,
    # Start with a copy of this lyrics cache instead of an empty one (a warm start).
    [string]$CacheFrom,
    # Extra environment for the app, e.g. @{ DOTNET_gcConcurrent = '0' } to try runtime knobs.
    [hashtable]$Env = @{},
    # Close a Taskitify that is already running (it holds the single-instance lock).
    [switch]$StopRunning,
    # Only close the running Taskitify cleanly (before dotnet publish overwrites its exe).
    [switch]$StopOnly,
    [switch]$KeepData
)

$ErrorActionPreference = 'Stop'
# Decimal points in the CSV and summary whatever the Windows language.
[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::InvariantCulture
if (!$OutRoot) { $OutRoot = Join-Path $DataRoot 'results' }

if (-not ('TaskitifyPerf.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TaskitifyPerf
{
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryCounters
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage,
                PrivateUsage, PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("psapi.dll")] private static extern bool GetProcessMemoryInfo(IntPtr process, out MemoryCounters counters, uint size);
        [DllImport("kernel32.dll")] private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>Private bytes, private working set, CPU time (100 ns) and creation time; null when the process is gone.</summary>
        public static long[] Read(int pid)
        {
            IntPtr process = OpenProcess(0x1000 | 0x0010, false, pid); // query limited information, VM read
            if (process == IntPtr.Zero) return null;
            try
            {
                MemoryCounters counters;
                if (!GetProcessMemoryInfo(process, out counters, (uint)Marshal.SizeOf(typeof(MemoryCounters)))) return null;
                long creation, exit, kernel, user;
                if (!GetProcessTimes(process, out creation, out exit, out kernel, out user)) return null;
                return new long[] { (long)counters.PrivateUsage.ToUInt64(), (long)counters.PrivateWorkingSetSize.ToUInt64(), kernel + user, creation };
            }
            finally { CloseHandle(process); }
        }

        /// <summary>
        /// Ask an app to exit the way Windows does at sign-out. WPF answers WM_QUERYENDSESSION
        /// with Application.Shutdown(), so OnExit runs and WebView2 closes its profile cleanly.
        /// </summary>
        public static int QueryEndSession(int pid)
        {
            var windows = new List<IntPtr>();
            EnumWindows((hwnd, _) =>
            {
                uint owner;
                GetWindowThreadProcessId(hwnd, out owner);
                if (owner == pid) windows.Add(hwnd);
                return true;
            }, IntPtr.Zero);
            foreach (var hwnd in windows)
            {
                IntPtr result;
                SendMessageTimeout(hwnd, 0x0011, IntPtr.Zero, new IntPtr(1), 0x0002, 3000, out result); // ENDSESSION_CLOSEAPP, abort if hung
            }
            return windows.Count;
        }
    }
}
'@
}

# ---------------------------------------------------------------- Spotify media session (GSMTC)

$script:AsTask = $null
function Wait-WinRt($Operation, [type]$ResultType) {
    if (!$script:AsTask) {
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $script:AsTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
    }
    $task = $script:AsTask.MakeGenericMethod($ResultType).Invoke($null, @($Operation))
    if (!$task.Wait(10000)) { throw 'Media session call timed out' }
    $task.Result
}

function Get-SpotifySession {
    $null = [Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager, Windows.Media.Control, ContentType = WindowsRuntime]
    $manager = Wait-WinRt ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager]::RequestAsync()) `
        ([Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager])
    # Same rule as SpotifyMonitor.IsSpotify: desktop installer or Microsoft Store build.
    $manager.GetSessions() | Where-Object { $_.SourceAppUserModelId -match '(^|\\)Spotify\.exe$|^SpotifyAB\.SpotifyMusic' } |
        Select-Object -First 1
}

function Invoke-SpotifyCommand([ValidateSet('Next', 'Previous')][string]$Command) {
    $session = Get-SpotifySession
    if (!$session) { Write-Warning 'Spotify has no media session; is it open?'; return $false }
    $operation = if ($Command -eq 'Next') { $session.TrySkipNextAsync() } else { $session.TrySkipPreviousAsync() }
    Wait-WinRt $operation ([bool])
}

function Reset-SpotifyPosition {
    $session = Get-SpotifySession
    if ($session) { $null = Wait-WinRt ($session.TryChangePlaybackPositionAsync(0)) ([bool]) }
}

function Get-SpotifyTitle {
    $spotify = Get-Process Spotify -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle } | Select-Object -First 1
    if ($spotify) { $spotify.MainWindowTitle } else { '' }
}

# ---------------------------------------------------------------- sampling

$script:CpuCount = [Environment]::ProcessorCount
$script:PreviousCpu = @{}
$script:PreviousAt = $null

function Get-Sample([int]$AppPid, [string]$ProfileMarker) {
    $now = Get-Date
    $targets = New-Object System.Collections.Generic.List[object]
    if ($AppPid) { $targets.Add(@{ Pid = $AppPid; Group = 'app'; Kind = 'app' }) }
    else { foreach ($p in @(Get-Process Taskitify -ErrorAction SilentlyContinue)) { $targets.Add(@{ Pid = $p.Id; Group = 'app'; Kind = 'app' }) } }
    foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'")) {
        if (!$p.CommandLine -or $p.CommandLine.IndexOf($ProfileMarker, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
        $kind = if ($p.CommandLine -match '--type=([a-z-]+)') { $Matches[1] -replace '-process$|-handler$', '' } else { 'browser' }
        $targets.Add(@{ Pid = [int]$p.ProcessId; Group = 'wv'; Kind = $kind })
    }

    $elapsed = if ($script:PreviousAt) { ($now - $script:PreviousAt).TotalSeconds } else { 0 }
    $cpu = @{}
    $sums = @{ app_priv = 0L; app_pws = 0L; app_cpu = 0L; wv_priv = 0L; wv_pws = 0L; wv_cpu = 0L; wv_n = 0
        browser = 0L; renderer = 0L; renderer_pws = 0L; renderer_n = 0; gpu = 0L; utility = 0L; crashpad = 0L }
    foreach ($t in $targets) {
        $m = [TaskitifyPerf.Native]::Read($t.Pid)
        if (!$m) { continue }
        $key = '{0}:{1}' -f $t.Pid, $m[3]
        $cpu[$key] = $m[2]
        # A process that appeared since the last sample used all its CPU time within the interval.
        $delta = if ($script:PreviousCpu.ContainsKey($key)) { $m[2] - $script:PreviousCpu[$key] } elseif ($script:PreviousAt) { $m[2] } else { 0 }
        $g = $t.Group
        $sums["${g}_priv"] += $m[0]; $sums["${g}_pws"] += $m[1]; $sums["${g}_cpu"] += $delta
        if ($g -eq 'wv') {
            $sums.wv_n++
            if ($sums.ContainsKey($t.Kind)) { $sums[$t.Kind] += $m[0] }
            if ($t.Kind -eq 'renderer') { $sums.renderer_n++; $sums.renderer_pws += $m[1] }
        }
    }
    $script:PreviousCpu = $cpu
    $script:PreviousAt = $now

    $mb = { param($bytes) [math]::Round($bytes / 1MB, 1) }
    $pct = { param($ticks) if ($elapsed -gt 0) { [math]::Round($ticks / ($elapsed * 1e7 * $script:CpuCount) * 100, 3) } else { $null } }
    [pscustomobject][ordered]@{
        time = $now.ToString('HH:mm:ss')
        t = 0.0
        app_priv_mb = & $mb $sums.app_priv
        app_pws_mb = & $mb $sums.app_pws
        app_cpu_pct = & $pct $sums.app_cpu
        wv_n = $sums.wv_n
        wv_priv_mb = & $mb $sums.wv_priv
        wv_pws_mb = & $mb $sums.wv_pws
        wv_cpu_pct = & $pct $sums.wv_cpu
        total_priv_mb = & $mb ($sums.app_priv + $sums.wv_priv)
        total_pws_mb = & $mb ($sums.app_pws + $sums.wv_pws)
        browser_mb = & $mb $sums.browser
        renderer_n = $sums.renderer_n
        renderer_mb = & $mb $sums.renderer
        renderer_pws_mb = & $mb $sums.renderer_pws
        gpu_mb = & $mb $sums.gpu
        utility_mb = & $mb $sums.utility
        crashpad_mb = & $mb $sums.crashpad
        song = Get-SpotifyTitle
        events = ''
    }
}

# ---------------------------------------------------------------- app log (debug mode)

$script:LogOffset = 0L
function Read-NewLogLines([string]$Path) {
    if (!$Path -or !(Test-Path -LiteralPath $Path)) { return @() }
    $stream = [IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($stream.Length -lt $script:LogOffset) { $script:LogOffset = 0 } # rotated
        $count = $stream.Length - $script:LogOffset
        if ($count -le 0) { return @() }
        $bytes = New-Object byte[] $count
        $stream.Position = $script:LogOffset
        $read = $stream.Read($bytes, 0, $count)
    }
    finally { $stream.Dispose() }
    # Only whole lines; a line still being written is read next time.
    $end = [Array]::LastIndexOf($bytes, [byte]10, $read - 1)
    if ($end -lt 0) { return @() }
    $script:LogOffset += $end + 1
    [Text.Encoding]::UTF8.GetString($bytes, 0, $end + 1) -split "`r?`n" | Where-Object { $_ }
}

function ConvertTo-LogEvent([string]$Line, [datetime]$Start) {
    if ($Line -notmatch '^(\d\d:\d\d:\d\d\.\d{3}) (\w+)\s+(.*)$') { return $null }
    $at = [datetime]::ParseExact($Start.ToString('yyyy-MM-dd ') + $Matches[1], 'yyyy-MM-dd HH:mm:ss.fff', $null)
    if ($at -lt $Start.AddHours(-1)) { $at = $at.AddDays(1) }
    [pscustomobject]@{ t = [math]::Round(($at - $Start).TotalSeconds, 2); Level = $Matches[2]; Message = $Matches[3] }
}

# Short tags for the samples table; the full lines go to events.txt.
function Get-EventTag([string]$Message) {
    switch -Regex ($Message) {
        '^Spotify web player starting' { 'wv-start'; break }
        '^Spotify web player ready' { 'wv-ready'; break }
        '^Spotify token captured' { 'token'; break }
        '^Spotify web player page unloaded' { 'wv-unload'; break }
        '^Spotify web player closed' { 'wv-close'; break }
        '^Spotify lookup: .*api search (\S+)' { "api-$($Matches[1])"; break }
        '^Lyrics for .*?: (\w+)( from (\S+))?' { "lyrics-$($Matches[1])$(if ($Matches[3]) { '-' + $Matches[3] })"; break }
        '^Overlay shown' { 'overlay'; break }
        default { $null }
    }
}

# ---------------------------------------------------------------- app lifetime

function Stop-Taskitify([int]$ProcessId, [string]$ProfileMarker) {
    $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if (!$process) { return }
    $null = [TaskitifyPerf.Native]::QueryEndSession($ProcessId)
    if (!$process.WaitForExit(20000)) {
        Write-Warning "Taskitify $ProcessId did not exit on WM_QUERYENDSESSION; killing it"
        Stop-Process -Id $ProcessId -Force
    }
    # WebView2 follows its host; give it time to flush the profile before anything copies it.
    for ($i = 0; $i -lt 30; $i++) {
        $left = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($ProfileMarker, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        if ($left.Count -eq 0) { return }
        Start-Sleep -Milliseconds 500
    }
    Write-Warning 'WebView2 processes outlived Taskitify'
}

function Copy-Tree([string]$From, [string]$To) {
    $null = & robocopy $From $To /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { throw "robocopy $From -> $To failed ($LASTEXITCODE)" }
}

function New-RunData([string]$RunDir) {
    $realRoaming = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Taskitify'
    $realLocal = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Taskitify'
    $realProfile = Join-Path $realLocal 'spotify-web-session'
    $snapshot = Join-Path $DataRoot 'session-snapshot'

    if ($RefreshSnapshot -or !(Test-Path -LiteralPath $snapshot)) {
        $inUse = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
            Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($realProfile, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
        if ($inUse.Count -gt 0) { throw 'The real Spotify session is open in Taskitify; close it (-StopRunning) before taking the snapshot.' }
        if (Test-Path -LiteralPath $snapshot) { Remove-Item -LiteralPath $snapshot -Recurse -Force }
        Write-Host "Copying the Spotify session (read only) to $snapshot"
        Copy-Tree $realProfile $snapshot
    }

    $data = Join-Path $RunDir 'd'
    $roaming = Join-Path $data 'Roaming'
    $local = Join-Path $data 'Local'
    New-Item -ItemType Directory -Force -Path $roaming, (Join-Path $local 'lyrics-cache') | Out-Null
    # The user's settings with debug logging switched on, so lookups and the web player are logged.
    $settings = Get-Content -LiteralPath (Join-Path $realRoaming 'settings.json') -Raw | ConvertFrom-Json
    $settings.DebugMode = $true
    [IO.File]::WriteAllText((Join-Path $roaming 'settings.json'), ($settings | ConvertTo-Json -Depth 10))
    Copy-Tree $snapshot (Join-Path $local 'spotify-web-session')
    if ($CacheFrom) { Copy-Tree $CacheFrom (Join-Path $local 'lyrics-cache') }
    $data
}

# ---------------------------------------------------------------- summary

function Get-Stat($Rows, [string]$Name) {
    $values = @($Rows | ForEach-Object { $_.$Name } | Where-Object { $_ -ne $null } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($values.Count -eq 0) { return $null }
    [pscustomobject]@{
        Median = $values[[int][math]::Floor(($values.Count - 1) / 2)]
        Max = $values[-1]
        Mean = [math]::Round(($values | Measure-Object -Average).Average, 3)
    }
}

function Write-Summary($Samples, $Events, $Phases, $Changes, [string]$Path) {
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-15} {5,-15} {6,-15} {7,-9} {8,-9}' -f 'phase', 'n',
        'app priv', 'app pws', 'wv priv', 'wv pws', 'total priv', 'app cpu%', 'wv cpu%'))
    $lines.Add('         (MB: median / max)')
    foreach ($phase in $Phases) {
        $rows = @($Samples | Where-Object { $_.t -ge $phase.From -and $_.t -lt $phase.To })
        if ($rows.Count -eq 0) { continue }
        $cell = { param($n) $s = Get-Stat $rows $n; if ($s) { '{0,6:0.0} / {1,6:0.0}' -f $s.Median, $s.Max } else { '-' } }
        $cpu = { param($n) $s = Get-Stat $rows $n; if ($s) { '{0:0.000}' -f $s.Mean } else { '-' } }
        $lines.Add(('{0,-8} {1,4}  {2,-15} {3,-15} {4,-15} {5,-15} {6,-15} {7,-9} {8,-9}' -f $phase.Name, $rows.Count,
            (& $cell 'app_priv_mb'), (& $cell 'app_pws_mb'), (& $cell 'wv_priv_mb'), (& $cell 'wv_pws_mb'),
            (& $cell 'total_priv_mb'), (& $cpu 'app_cpu_pct'), (& $cpu 'wv_cpu_pct')))
    }
    $peak = $Samples | Sort-Object { [double]$_.total_priv_mb } -Descending | Select-Object -First 1
    if ($peak) { $lines.Add(('peak total private {0} MB at t={1} s (app {2}, web view {3}, renderer {4})' -f $peak.total_priv_mb, $peak.t, $peak.app_priv_mb, $peak.wv_priv_mb, $peak.renderer_mb)) }

    $lyrics = @($Events | Where-Object { $_.Message -like 'Lyrics for *' })
    $first = $Events | Where-Object { $_.Level -ne 'SCRIPT' } | Sort-Object t | Select-Object -First 1
    if ($first) { $lines.Add("app running (first log line) $($first.t) s after launch") }
    $overlay = $Events | Where-Object { $_.Message -like 'Overlay shown*' } | Select-Object -First 1
    if ($overlay) { $lines.Add("overlay shown $($overlay.t) s after launch") }
    if ($lyrics) { $lines.Add("first lyrics $($lyrics[0].t) s after launch: $($lyrics[0].Message)") }
    $starts = @($Events | Where-Object { $_.Message -like 'Spotify web player starting*' }).Count
    $unloads = @($Events | Where-Object { $_.Message -like 'Spotify web player page unloaded*' }).Count
    $closes = @($Events | Where-Object { $_.Message -like 'Spotify web player closed*' }).Count
    $lines.Add("web player: $starts start(s), $unloads page unload(s), $closes close(s)")
    $lookups = @($Events | Where-Object { $_.Message -like 'Spotify lookup:*' })
    if ($lookups) {
        $api = $lookups | ForEach-Object { if ($_.Message -match 'api search (\S+)') { $Matches[1] } else { 'none' } } | Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }
        $web = @($lookups | Where-Object { $_.Message -like '*web player search*' }).Count
        $lines.Add("spotify lookups: $($lookups.Count); api search: $($api -join ', '); web player searches: $web")
    }
    if ($lyrics) {
        $kinds = $lyrics | ForEach-Object { if ($_.Message -match ': (\w+)( from (.+?)( \(cache\))?)? in ') { "$($Matches[1])/$(if ($Matches[3]) { $Matches[3] } else { '-' })" } } |
            Group-Object | ForEach-Object { "$($_.Name) x$($_.Count)" }
        $lines.Add("lyrics: $($kinds -join ', ')")
    }
    $previous = $null
    foreach ($skip in $Changes) {
        $until = [math]::Min($skip + 45, ($Changes | Where-Object { $_ -gt $skip } | Select-Object -First 1) + 0)
        if ($until -le $skip) { $until = $skip + 45 }
        $before = $Samples | Where-Object { $_.t -lt $skip } | Select-Object -Last 1
        $window = @($Samples | Where-Object { $_.t -ge $skip -and $_.t -lt $until })
        $peakWv = ($window | Measure-Object -Property wv_priv_mb -Maximum).Maximum
        $state = if (!$before -or [int]$before.wv_n -eq 0) { 'closed' } else { 'open' }
        $next = $lyrics | Where-Object { $_.t -gt $skip } | Select-Object -First 1
        $lookup = $Events | Where-Object { $_.t -gt $skip -and $_.t -lt $until -and $_.Message -like 'Spotify lookup:*' } | Select-Object -First 1
        $gap = if ($previous) { '{0,4:0} s' -f ($skip - $previous) } else { '   -' }
        $result = if ($next -and $next.t -lt $until) { '{0,5:0.0} s  {1}' -f ($next.t - $skip), ($next.Message -replace '^Lyrics for ', '') } else { '    -  no lyrics line' }
        $lines.Add(('change at {0,4} s (gap {1}, web view {2,-6} before, peak {3,6:0.0} MB): {4}' -f $skip, $gap, $state, $peakWv, $result))
        if ($lookup) { $lines.Add("      $($lookup.Message)") }
        $previous = $skip
    }
    $lines | Set-Content -LiteralPath $Path -Encoding UTF8
    $lines | ForEach-Object { Write-Host $_ }
}

# ---------------------------------------------------------------- main

if ($StopOnly) {
    foreach ($p in @(Get-Process Taskitify -ErrorAction SilentlyContinue)) { Write-Host "Closing Taskitify $($p.Id)"; Stop-Taskitify $p.Id 'spotify-web-session' }
    return
}

$started = Get-Date
$runDir = Join-Path $OutRoot ('{0}-{1:yyyyMMdd-HHmmss}' -f $Label, $started)
New-Item -ItemType Directory -Force -Path $runDir | Out-Null
$appPid = 0
$logPath = $null
$marker = 'spotify-web-session'
$plan = New-Object System.Collections.Generic.List[object]
$changes = @()
$dataDir = $null
$firstSkipped = $null

if ($Exe) {
    $running = @(Get-Process Taskitify -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        if (!$StopRunning) { throw 'Taskitify is running. Close it or pass -StopRunning.' }
        foreach ($p in $running) { Write-Host "Closing Taskitify $($p.Id)"; Stop-Taskitify $p.Id 'spotify-web-session' }
    }
    $dataRunDir = Join-Path $DataRoot ('{0}-{1:HHmmss}' -f $Label, $started)
    $dataDir = New-RunData $dataRunDir
    $marker = $dataDir
    $logPath = Join-Path $dataDir 'Local\log.txt'

    if ($Scenario -eq 'Standard') {
        $changes = @($SkipTimes | Sort-Object)
        $Seconds = $changes[-1] + $After
        foreach ($s in $changes) { $plan.Add([pscustomobject]@{ At = $s; Action = 'Next' }) }
        $phases = @(
            @{ Name = 'startup'; From = 0; To = 150 },
            @{ Name = 'idle'; From = 150; To = $changes[0] },
            @{ Name = 'changes'; From = $changes[0]; To = $changes[-1] + 45 },
            @{ Name = 'post'; From = $Seconds - 60; To = $Seconds + 1 })
        # The same song from its start on every run, so nothing changes before the first skip.
        Reset-SpotifyPosition
    }
    else {
        $phases = @(@{ Name = 'startup'; From = 0; To = 150 }, @{ Name = 'idle'; From = 150; To = $Seconds + 1 })
    }

    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = (Resolve-Path -LiteralPath $Exe).Path
    $psi.Arguments = '--background' # no "running in the background" balloon on every run
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables['TASKITIFY_DATA_DIR'] = $dataDir
    foreach ($name in $Env.Keys) { $psi.EnvironmentVariables[$name] = [string]$Env[$name] }
    $started = Get-Date
    $appPid = [Diagnostics.Process]::Start($psi).Id
    Write-Host "Started $($psi.FileName) (pid $appPid), data in $dataDir"
}
else {
    $phases = @(@{ Name = 'all'; From = 0; To = $Seconds + 1 })
}

$samples = New-Object System.Collections.Generic.List[object]
$events = New-Object System.Collections.Generic.List[object]
$pendingTags = New-Object System.Collections.Generic.List[string]
$nextSample = 0.0
$csv = Join-Path $runDir 'samples.csv'
try {
    while ($true) {
        $t = ((Get-Date) - $started).TotalSeconds
        if ($t -gt $Seconds) { break }
        if ($appPid -and !(Get-Process -Id $appPid -ErrorAction SilentlyContinue)) { Write-Warning 'Taskitify exited'; break }

        foreach ($step in @($plan | Where-Object { $_.At -le $t })) {
            if (!$firstSkipped) { $firstSkipped = Get-SpotifyTitle }
            $ok = Invoke-SpotifyCommand $step.Action
            $tag = "skip$(if (!$ok) { '-REFUSED' })"
            $pendingTags.Add($tag)
            $events.Add([pscustomobject]@{ t = [math]::Round($t, 2); Level = 'SCRIPT'; Message = "$($step.Action) (song was: $(Get-SpotifyTitle))" })
            $null = $plan.Remove($step)
        }

        foreach ($line in @(Read-NewLogLines $logPath)) {
            $e = ConvertTo-LogEvent $line $started
            if (!$e) { continue }
            $events.Add($e)
            $tag = Get-EventTag $e.Message
            if ($tag) { $pendingTags.Add($tag) }
        }

        if ($t -ge $nextSample) {
            $sample = Get-Sample $appPid $marker
            $sample.t = [math]::Round($t, 1)
            $sample.events = $pendingTags -join ' '
            $pendingTags.Clear()
            $samples.Add($sample)
            $sample | Export-Csv -LiteralPath $csv -Append -NoTypeInformation -Encoding UTF8
            Write-Host ('{0,6:0}s app {1,6:0.0}/{2,6:0.0} MB  wv {3,6:0.0}/{4,6:0.0} MB ({5} procs)  cpu {6}/{7}%  {8}  {9}' -f $t,
                $sample.app_priv_mb, $sample.app_pws_mb, $sample.wv_priv_mb, $sample.wv_pws_mb, $sample.wv_n,
                $sample.app_cpu_pct, $sample.wv_cpu_pct, $sample.song, $sample.events)
            $nextSample += $Interval
        }
        Start-Sleep -Milliseconds 250
    }
}
finally {
    if ($appPid) {
        Stop-Taskitify $appPid $marker
        foreach ($line in @(Read-NewLogLines $logPath)) { $e = ConvertTo-LogEvent $line $started; if ($e) { $events.Add($e) } }
        if ($logPath -and (Test-Path -LiteralPath $logPath)) { Copy-Item -LiteralPath $logPath (Join-Path $runDir 'app-log.txt') }
        if ($Rewind -and $firstSkipped) {
            # "Previous" restarts a song that played for over 3 s; quick repeats then go back one song each.
            for ($i = 0; $i -lt 20 -and (Get-SpotifyTitle) -ne $firstSkipped; $i++) {
                $null = Invoke-SpotifyCommand 'Previous'
                Start-Sleep -Milliseconds 900
            }
            Write-Host "Rewound to: $(Get-SpotifyTitle) (wanted $firstSkipped)"
        }
        # The lyrics cache stays with the results (no session data in it) for -CacheFrom.
        $cacheDir = if ($dataDir) { Join-Path $dataDir 'Local\lyrics-cache' }
        if ($cacheDir -and (Test-Path -LiteralPath $cacheDir)) { Copy-Tree $cacheDir (Join-Path $runDir 'lyrics-cache') }
        if (!$KeepData -and $dataDir) { Remove-Item -LiteralPath (Split-Path $dataDir) -Recurse -Force -ErrorAction SilentlyContinue }
    }
    $events | Sort-Object t | ForEach-Object { '{0,8:0.00} {1,-6} {2}' -f $_.t, $_.Level, $_.Message } |
        Set-Content -LiteralPath (Join-Path $runDir 'events.txt') -Encoding UTF8
}

Write-Summary $samples $events $phases $changes (Join-Path $runDir 'summary.txt')
Write-Host "Results: $runDir"

# robocopy leaves 1 ("files copied") in $LASTEXITCODE; this script succeeded.
exit 0
