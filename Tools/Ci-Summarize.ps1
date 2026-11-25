$summary = $env:GITHUB_STEP_SUMMARY

if (-not $summary) { Write-Host "没有 GITHUB_STEP_SUMMARY，跳过汇总"; exit 0 }

$root = "NowFrameWork/Build/FrameworkRegression"

if (-not (Test-Path $root)) {
    Add-Content $summary "## 框架回归`n`n未生成回归目录（脚本可能在启动阶段失败）。"
    exit 0
}

$dir = Get-ChildItem $root -Directory | Sort-Object LastWriteTime | Select-Object -Last 1
$json = Join-Path $dir.FullName "run.json"

if (-not (Test-Path $json)) {
    Add-Content $summary "## 框架回归`n`n缺少 run.json，请查看上传的日志。"
    exit 0
}

$run = Get-Content $json -Raw | ConvertFrom-Json

Add-Content $summary "## 框架回归 ``$($run.runName)``"
Add-Content $summary ""
Add-Content $summary "**状态**：``$($run.status)``"
Add-Content $summary ""
Add-Content $summary "| 阶段 | 通过 | 失败 | 跳过 | 耗时(s) |"
Add-Content $summary "|---|---|---|---|---|"

foreach ($stage in $run.stages) {
    Add-Content $summary "| $($stage.name) | $($stage.passed)/$($stage.total) | $($stage.failed) | $($stage.skipped) | $([math]::Round($stage.seconds, 1)) |"
}
