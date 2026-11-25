param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Attempts = 12
)

$ErrorActionPreference = "Stop"
$state = Join-Path $env:USERPROFILE "AppData\LocalLow\DefaultCompany\NowFrameWork\WindowsReleases\settings-release-state.nwf"

function Read-State {
    if (-not (Test-Path $state)) { return $null }
    $raw = Get-Content $state -Raw -ErrorAction SilentlyContinue
    $confirmed = if ($raw -match '"ConfirmedManifest":\{"Id":"([^"]*)"') { $matches[1] } else { "" }
    $trial = if ($raw -match '"TrialManifest":\{"Id":"([^"]*)"') { $matches[1] } else { "" }
    $pending = if ($raw -match '"PendingManifest":\{"Id":"([^"]*)"') { $matches[1] } else { "" }
    return [pscustomobject]@{ Confirmed = $confirmed; Trial = $trial; Pending = $pending; Raw = $raw }
}

function Stop-Player([System.Diagnostics.Process]$process) {
    try { if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction Stop } } catch { }
}

$before = Read-State
"起始状态: Confirmed=[$($before.Confirmed)] Trial=[$($before.Trial)]"
$target = if ($before.Confirmed -eq "v3") { "v2" } else { "v3" }
"本次目标试用版本: $target"

"=== 1. 暂存 $target ==="
$p = Start-Process $Exe -ArgumentList "-stageRelease", $target -PassThru
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 300
    $s = Read-State
    if ($s.Pending -eq $target) { break }
}
$s = Read-State
"  暂存后: Pending=[$($s.Pending)] Confirmed=[$($s.Confirmed)]"
Stop-Player $p
Start-Sleep -Milliseconds 800

"=== 2. 试用启动并强杀（最多 $Attempts 次）==="
$captured = $false
$attempt = 0
while (-not $captured -and $attempt -lt $Attempts) {
    $attempt++
    $proc = Start-Process $Exe -PassThru
    $killed = $false
    $limit = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $limit) {
        $s = Read-State
        if ($s.Trial -eq $target) {
            Stop-Player $proc
            $killed = $true
            break
        }
        if ($proc.HasExited) { break }
        Start-Sleep -Milliseconds 10
    }
    Stop-Player $proc
    $after = Read-State
    if ($killed) {
        $captured = $true
        "  第 $attempt 次: 捕获到试用状态并已强杀 -> Trial=[$($after.Trial)] Confirmed=[$($after.Confirmed)]"
    } else {
        "  第 $attempt 次: 未命中（Trial=[$($after.Trial)] Confirmed=[$($after.Confirmed)]）"
    }
    Start-Sleep -Milliseconds 300
}

"CAPTURED=$captured"

"=== 3. 重新启动验证回退 ==="
$run = Start-Process $Exe -PassThru
Start-Sleep -Seconds 6
Stop-Player $run
Start-Sleep -Milliseconds 500
$log = Join-Path $env:USERPROFILE "AppData\LocalLow\DefaultCompany\NowFrameWork\Player.log"
"  日志中的选版行:"
Select-String -Path $log -Pattern "选中发布集|使用随包" | Select-Object -Last 3 | ForEach-Object { "    " + $_.Line.Trim() }
$final = Read-State
"  最终状态: Confirmed=[$($final.Confirmed)] Trial=[$($final.Trial)] Pending=[$($final.Pending)]"
