param(
    [string]$UnityPath = "C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe",
    [string]$ProjectPath = "C:\PersonalProjects\UnityProjects\IronKingdoms",
    [string]$ExecuteMethod = "IronKingdoms.Editor.Tests.ForestFogRenderRegressionBatchRunner.Run",
    [string]$LogPrefix = "iterative-auto",
    [string]$ResultsDir = "C:\PersonalProjects\UnityProjects\IronKingdoms\TestResults",
    [int]$IntervalSeconds = 30,
    [int]$MaxRuns = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $UnityPath)) {
    throw "Unity executable not found: $UnityPath"
}

if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Project path not found: $ProjectPath"
}

if (-not (Test-Path -LiteralPath $ResultsDir)) {
    New-Item -ItemType Directory -Path $ResultsDir | Out-Null
}

Write-Host "Starting fog regression loop..."
Write-Host "Unity:   $UnityPath"
Write-Host "Project: $ProjectPath"
Write-Host "Method:  $ExecuteMethod"
Write-Host "Results: $ResultsDir"
Write-Host "Interval: $IntervalSeconds sec"
if ($MaxRuns -gt 0) {
    Write-Host "Max runs: $MaxRuns"
}
else {
    Write-Host "Max runs: infinite (Ctrl+C to stop)"
}
Write-Host ""

$run = 1
while ($true) {
    if ($MaxRuns -gt 0 -and $run -gt $MaxRuns) {
        Write-Host "Reached MaxRuns=$MaxRuns. Exiting."
        break
    }

    $logName = "{0}-{1}.log" -f $LogPrefix, $run.ToString("00")
    $logPath = Join-Path $ResultsDir $logName
    $startedAt = Get-Date

    Write-Host ("[{0}] RUN {1} starting..." -f $startedAt.ToString("HH:mm:ss"), $run)

    $args = @(
        "-batchmode",
        "-projectPath", $ProjectPath,
        "-executeMethod", $ExecuteMethod,
        "-logFile", $logPath,
        "-quit"
    )

    $proc = Start-Process -FilePath $UnityPath -ArgumentList $args -Wait -PassThru -NoNewWindow
    $endedAt = Get-Date
    $duration = [math]::Round(($endedAt - $startedAt).TotalSeconds, 1)

    # Optional tiny audible ping on completion of each run.
    try { [console]::Beep(1200, 120) } catch {}

    Write-Host ("[{0}] RUN {1} done (exit={2}, {3}s)" -f $endedAt.ToString("HH:mm:ss"), $run, $proc.ExitCode, $duration)
    Write-Host ("LOG: {0}" -f $logPath)
    Write-Host "Paste this log path in chat when you want analysis."
    Write-Host ""

    Start-Sleep -Seconds $IntervalSeconds
    $run++
}
