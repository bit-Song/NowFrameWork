$summary = $env:GITHUB_STEP_SUMMARY

if (-not $summary) { Write-Host "没有 GITHUB_STEP_SUMMARY，跳过汇总"; exit 0 }

$utf8 = New-Object System.Text.UTF8Encoding($false)

function Add-Summary([string]$text) {
    [System.IO.File]::AppendAllText($summary, $text + [Environment]::NewLine, $utf8)
}

$root = "NowFrameWork/Build/FrameworkRegression"

if (-not (Test-Path $root)) {
    Add-Summary "## 框架回归`n`n未生成回归目录（脚本可能在启动阶段失败）。"
    exit 0
}

$dir = Get-ChildItem $root -Directory | Sort-Object LastWriteTime | Select-Object -Last 1
$json = Join-Path $dir.FullName "run.json"

if (-not (Test-Path $json)) {
    Add-Summary "## 框架回归`n`n缺少 run.json，请查看上传的日志。"
    exit 0
}

$run = Get-Content $json -Raw | ConvertFrom-Json

Add-Summary "## 框架回归 ``$($run.runName)``"
Add-Summary ""
Add-Summary "**状态**：``$($run.status)``"
Add-Summary ""
Add-Summary "| 阶段 | 通过 | 失败 | 跳过 | 耗时(s) |"
Add-Summary "|---|---|---|---|---|"

foreach ($stage in $run.stages) {
    Add-Summary "| $($stage.name) | $($stage.passed)/$($stage.total) | $($stage.failed) | $($stage.skipped) | $([math]::Round($stage.seconds, 1)) |"
}
