param(
    [ValidateSet('suite','hot','offline','remote-cancel','remote-timeout','scene-cancel')]
    [string]$Mode = 'suite',
    [string]$Expected = 'PC-PROBE-V1',
    [ValidateSet('Current','V1','V2')]
    [string]$Release = 'Current',
    [string]$NodePath = 'node',
    [string]$RunName = ''
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectRoot = Join-Path $repoRoot 'NowFrameWork'
$outputRoot = Join-Path $projectRoot 'Build/PCRegression'
$exe = Join-Path $outputRoot 'Player/PCRegression.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw '请先构建 PCRegression.exe' }
if (-not $RunName) { $RunName = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + $Mode }
if ($RunName -notmatch '^[a-zA-Z0-9_-]+$') { throw 'RunName 只能包含字母、数字、横线和下划线' }
$runRoot = Join-Path $outputRoot ('Evidence/' + $RunName)
if (Test-Path -LiteralPath $runRoot) { throw '该报告目录已存在，请使用新的 RunName' }
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$shutdownToken = [guid]::NewGuid().ToString('N')
$server = $null
$player = $null
try {
    $listener = Get-NetTCPConnection -LocalPort 18182 -State Listen -ErrorAction SilentlyContinue
    if ($listener -and $Mode -ne 'suite') { throw '18182 端口已被占用，请先关闭本地回归服务器再运行此脚本' }
    if ($Mode -notin @('offline','suite')) {
        $delay = if ($Mode -eq 'remote-timeout') { 7000 } elseif ($Mode -in @('remote-cancel','scene-cancel')) { 500 } else { 0 }
        $serverScript = Join-Path $PSScriptRoot 'pc-regression-server.cjs'
        $contentRoot = if ($Release -eq 'Current') { Join-Path $outputRoot 'ServerData' } else { Join-Path $outputRoot ('Releases/' + $Release) }
        if (-not (Test-Path -LiteralPath $contentRoot)) { throw ('资源快照不存在: ' + $contentRoot) }
        $httpLog = Join-Path $runRoot 'http.jsonl'
        $server = Start-Process -FilePath $NodePath -ArgumentList @(
            ('"' + $serverScript + '"'), '--root', ('"' + $contentRoot + '"'),
            '--port', '18182', '--delay-ms', $delay, '--log', ('"' + $httpLog + '"'), '--shutdown-token', $shutdownToken
        ) -WindowStyle Hidden -PassThru
        $ready = $false
        for ($i = 0; $i -lt 30; $i++) {
            try { $null = Invoke-RestMethod 'http://127.0.0.1:18182/__status'; $ready = $true; break } catch { Start-Sleep -Milliseconds 100 }
        }
        if (-not $ready) { throw '本地回归服务器未启动' }
    }
    $json = Join-Path $runRoot 'report.json'
    $playerLog = Join-Path $runRoot 'player.log'
    $cache = Join-Path $runRoot 'Cache'
    $arguments = @('-batchmode','-nographics','-logFile', ('"' + $playerLog + '"'),
        '--nw-mode', $Mode, '--nw-report', ('"' + $json + '"'),
        '--nw-cache', ('"' + $cache + '"'), '--nw-expect', $Expected, '--nw-quit')
    $hashBefore = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    $codeHashes = [ordered]@{}
    foreach ($codeFile in @('PCRegression.exe','GameAssembly.dll','UnityPlayer.dll','TuanjiePlayer.dll')) {
        $codePath = Join-Path (Split-Path -Parent $exe) $codeFile
        if (Test-Path -LiteralPath $codePath) { $codeHashes[$codeFile] = (Get-FileHash -LiteralPath $codePath -Algorithm SHA256).Hash }
    }
    $player = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $player.WaitForExit(120000)) { $player.Kill(); throw 'Player 超过 120 秒未结束，保留现场日志' }
    if (-not (Test-Path -LiteralPath $json)) { throw 'Player 未生成报告，请查看 player.log' }
    $report = Get-Content -Raw -LiteralPath $json | ConvertFrom-Json
    [ordered]@{ mode=$Mode; release=$Release; playerSha256=$hashBefore; playerCodeSha256=$codeHashes; playerExitCode=$player.ExitCode; report=$json; utc=[DateTime]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $runRoot 'run.json')
    $report.cases | Select-Object name,passed,seconds,@{ Name='detail'; Expression={ ($_.detail -split "`n")[0] } } | Format-Table -Wrap
    Write-Output ('报告：' + $json)
    if ($player.ExitCode -ne 0 -or -not $report.finished -or -not $report.passed) { throw 'PC 回归失败，请查看报告和日志' }
}
finally {
    if ($player -and -not $player.HasExited) { Stop-Process -Id $player.Id -ErrorAction SilentlyContinue }
    if ($server -and -not $server.HasExited) {
        try { $null = Invoke-WebRequest -Method Post 'http://127.0.0.1:18182/__shutdown' -Headers @{ 'X-PC-Probe-Token' = $shutdownToken }; $null = $server.WaitForExit(10000) }
        catch { Write-Warning '测试服务器未能正常退出，请检查该次运行的 http.jsonl' }
    }
}
