param([string]$RunName = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'NowFrameWork/Build/StorageRegression'
$exe = Join-Path $root 'Player/StorageRegression.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw '请先构建独立 StorageRegression.exe，见文档 20' }
if (-not $RunName) { $RunName = Get-Date -Format 'yyyyMMdd-HHmmss' }
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName 只允许字母、数字、横线和下划线' }
$run = Join-Path $root ('Evidence/' + $RunName)
if (Test-Path -LiteralPath $run) { throw '报告目录已存在，请使用新的 RunName' }
$data = Join-Path $run 'Data'
New-Item -ItemType Directory -Path $data -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $data '.nw-storage-regression'), 'NowFrameWork.StorageRegression.v1')
$hashes = [ordered]@{}
foreach ($file in @('StorageRegression.exe','GameAssembly.dll','UnityPlayer.dll','TuanjiePlayer.dll')) {
    $code = Join-Path (Split-Path -Parent $exe) $file
    if (Test-Path -LiteralPath $code) { $hashes[$file] = (Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash }
}
$results = @()
foreach ($phase in @('seed','read','recover','read-repaired','migrate','read-v2','reject-old','limit','thread','corrupt-both')) {
    $json = Join-Path $run ($phase + '.json')
    $log = Join-Path $run ($phase + '.log')
    $arguments = @('-batchmode','-nographics','-logFile', ('"' + $log + '"'), '--nw-phase', $phase,
        '--nw-root', ('"' + $data + '"'), '--nw-report', ('"' + $json + '"'))
    $player = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    try {
        if (-not $player.WaitForExit(30000)) { throw ('Player 超过 30 秒未退出：' + $phase) }
        if (-not (Test-Path -LiteralPath $json)) { throw ('缺少 Player 报告：' + $phase) }
        $report = Get-Content -Raw -Encoding UTF8 -LiteralPath $json | ConvertFrom-Json
        $result = [ordered]@{ phase=$phase; exitCode=$player.ExitCode; report=$json; passed=($player.ExitCode -eq 0 -and $report.finished -and $report.passed -and $report.phase -eq $phase); checks=@($report.checks).Count }
        $results += $result
        [IO.File]::WriteAllText((Join-Path $run 'run.json'), ([ordered]@{ utc=[DateTime]::UtcNow.ToString('O'); finished=$false; passed=$false; codeSha256=$hashes; phases=$results } | ConvertTo-Json -Depth 8))
        if (-not $result.passed) { throw ('Storage 回归失败：' + $phase + '；' + $report.error) }
        Write-Output ('PASS ' + $phase + ' checks=' + $result.checks)
    }
    finally { if (-not $player.HasExited) { Stop-Process -Id $player.Id -ErrorAction SilentlyContinue } }
}
foreach ($file in $hashes.Keys) {
    $code = Join-Path (Split-Path -Parent $exe) $file
    if ((Get-FileHash -LiteralPath $code -Algorithm SHA256).Hash -ne $hashes[$file]) { throw '运行期间 Player 代码发生变化' }
}
[IO.File]::WriteAllText((Join-Path $run 'run.json'), ([ordered]@{ utc=[DateTime]::UtcNow.ToString('O'); finished=$true; passed=$true; codeSha256=$hashes; phases=$results } | ConvertTo-Json -Depth 8))
Write-Output ('全部 10 个独立进程阶段通过，报告：' + $run)
