# Lyric Motion review harness

This console project opens the production Windows mini player and taskbar overlay over a deterministic playback clock. It writes to an isolated profile under the output folder. Existing Taskitify preferences and the live media session are left alone.

From the repository root, run:

```powershell
dotnet build tools/LyricMotionReview -c Release
$review = '.\tools\LyricMotionReview\bin\Release\net8.0-windows10.0.19041.0\LyricMotionReview.exe'
& $review smoke "$env:TEMP\taskitify-flow-smoke"
& $review bench "$env:TEMP\taskitify-flow-bench" cover.jpg missing.lrc --long
& $review bench "$env:TEMP\taskitify-flow-software" cover.jpg missing.lrc E --software
& $review demo "$env:TEMP\taskitify-flow-demo" cover.jpg missing.lrc 13
```

The cover path is optional. An existing LRC path replaces the built-in fixture. The demo accepts comma-separated scenario IDs after its duration and an optional `--start=seconds`. When the file `demo.ready` appears, the stage is ready for a native screen recorder. The stage is 704 × 560 DIPs on the primary display.

Bench metrics use process CPU time, private bytes, working set, managed allocations, dispatcher interval p95 and local play/pause dispatch time. The latter measures command propagation to the test clock. It excludes media service and device delays. Flow tick counts and intervals are collected from the actual timer. Software mode forces WPF's two window targets to software. GPU engine utilization can be sampled separately for the review process with Windows Performance Counters. On the 90-second extended test the fixture loops at a deterministic position.
