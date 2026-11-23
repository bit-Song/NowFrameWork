param(
    [string]$EditorPath = $env:TUANJIE_EDITOR_PATH,
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9_-]*$')]
    [string]$RunName = ('{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 8))),
    [ValidateRange(1, 86400)]
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = 'Stop'
if (-not $EditorPath) {
    $editor = Get-Command 'Tuanjie.exe' -ErrorAction SilentlyContinue
    if ($editor) { $EditorPath = $editor.Source }
}
if (-not $EditorPath -or -not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
    throw '请通过 -EditorPath 或 TUANJIE_EDITOR_PATH 指定团结编辑器可执行文件。'
}
$EditorPath = (Resolve-Path -LiteralPath $EditorPath).Path

$repoRoot = Split-Path -Parent $PSScriptRoot
$mainProject = Join-Path $repoRoot 'NowFrameWork'
$runRoot = Join-Path $mainProject "Build/FrameworkRegression/$RunName"
if (Test-Path -LiteralPath $runRoot) { throw "该运行目录已存在，请更换 RunName。$runRoot" }
New-Item -ItemType Directory -Path $runRoot | Out-Null

$manifest = Get-Content -Raw -LiteralPath (Join-Path $mainProject 'Packages/manifest.json') | ConvertFrom-Json
$locked = (Get-Content -Raw -LiteralPath (Join-Path $mainProject 'Packages/packages-lock.json') | ConvertFrom-Json).dependencies
$core = 'com.now.nowframework.core'
$assets = 'com.now.nowframework.assets'
$ugui = 'com.now.nowframework.ui.ugui'

function Get-PackageSpec([string]$Name) {
    $entry = $locked.PSObject.Properties[$Name].Value
    $spec = $manifest.dependencies.PSObject.Properties[$Name].Value
    if (-not $spec -and $entry) { $spec = $entry.version }
    if (-not $spec) { throw "主工程未记录包版本 $Name" }

    if ($entry -and $entry.source -in @('git', 'registry')) {
        $id = if ($entry.source -eq 'git') { $entry.hash.Substring(0, 10) } else { $entry.version }
        $cached = Join-Path $mainProject "Library/PackageCache/$Name@$id"
        if (Test-Path -LiteralPath (Join-Path $cached 'package.json') -PathType Leaf) {
            return 'file:' + $cached.Replace('\', '/')
        }
    }
    return $spec
}

function New-Consumer([string]$Name, [string[]]$Packages) {
    $project = Join-Path $runRoot $Name
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
        New-Item -ItemType Directory -Path (Join-Path $project $folder) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $mainProject 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings')

    $dependencies = [ordered]@{}
    foreach ($package in $Packages) {
        $source = (Resolve-Path -LiteralPath (Join-Path $repoRoot "Framework/$package")).Path
        $dependencies[$package] = 'file:' + $source.Replace('\', '/')
    }
    $external = @('com.cysharp.unitask', 'com.unity.test-framework', 'com.unity.ext.nunit',
        'com.unity.modules.imgui', 'com.unity.modules.jsonserialize')
    if ($ugui -in $Packages) {
        $external += @('com.unity.ugui', 'com.unity.modules.ui', 'com.unity.modules.animation',
            'com.unity.modules.physics', 'com.unity.modules.physics2d', 'com.unity.modules.audio', 'com.unity.modules.assetbundle')
    }
    foreach ($package in $external) { $dependencies[$package] = Get-PackageSpec $package }

    @{ dependencies = $dependencies; testables = $Packages } | ConvertTo-Json -Depth 5 |
        Set-Content -Encoding UTF8 -LiteralPath (Join-Path $project 'Packages/manifest.json')
    return $project
}

function Confirm-ConsumerScope([string]$Project, [string[]]$Packages, [xml]$Report) {
    $resolved = (Get-Content -Raw -LiteralPath (Join-Path $Project 'Packages/packages-lock.json') | ConvertFrom-Json).dependencies
    $actualPackages = @($resolved.PSObject.Properties.Name | Where-Object { $_ -like 'com.now.nowframework.*' })
    if (Compare-Object ($Packages | Sort-Object) ($actualPackages | Sort-Object)) {
        throw "独立消费工程包范围错误。实际框架包 $($actualPackages -join ', ')"
    }

    $testAssembly = @{
        $core = 'NowFrameWork.Core.Tests.dll'
        $assets = 'NowFrameWork.Assets.Tests.dll'
        $ugui = 'NowFrameWork.UI.Tests.dll'
    }
    foreach ($package in $Packages) {
        $suite = $Report.SelectSingleNode("//test-suite[@name='$($testAssembly[$package])' and @passed > 0]")
        if (-not $suite) { throw "$package 没有实际通过的包测试。" }
    }
}

function Invoke-Stage([string]$Name, [string[]]$ConsumerPackages = @()) {
    $log = Join-Path $runRoot "$Name.log"
    $reportPath = Join-Path $runRoot "$Name.xml"
    $started = [DateTime]::UtcNow
    $result = [ordered]@{
        name = $Name; status = 'failed'; exitCode = $null
        log = $log; report = $reportPath; total = $null; passed = $null; failed = $null; skipped = $null
        error = $null; seconds = $null
    }
    $process = $null
    try {
        $project = if ($ConsumerPackages.Count -gt 0) { New-Consumer $Name $ConsumerPackages } else { $mainProject }
        $argsList = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"'))
        if ($Name -eq 'preflight') {
            $argsList += @('-executeMethod', 'NowFrameWork.EditorTools.FrameworkRegressionPreflight.Run',
                '-nwfPreflightReport', ('"' + $reportPath + '"'), '-quit')
        }
        else {
            $argsList += @('-runTests', '-testPlatform', 'EditMode', '-testResults', ('"' + $reportPath + '"'))
        }
        Write-Host "[$Name] 开始"
        $process = Start-Process -FilePath $EditorPath -ArgumentList $argsList -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "超过 $TimeoutSeconds 秒未结束。" }
        $result.exitCode = $process.ExitCode

        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            throw "缺少 NUnit XML 报告，编辑器退出码 $($process.ExitCode)。请查看日志。"
        }

        [xml]$xml = Get-Content -Raw -LiteralPath $reportPath
        $report = $xml.DocumentElement
        if ($report.Name -ne 'test-run') { throw '报告缺少 NUnit test-run 根节点。' }
        foreach ($field in @('total', 'passed', 'failed', 'skipped')) {
            $value = 0
            if (-not [int]::TryParse($report.GetAttribute($field), [ref]$value) -or $value -lt 0) {
                throw "报告字段 $field 无效。"
            }
            $result[$field] = $value
        }
        if ($process.ExitCode -ne 0 -or $report.GetAttribute('result') -ne 'Passed' -or
            $result.failed -ne 0 -or $result.passed -eq 0 -or
            $result.total -ne ($result.passed + $result.skipped)) {
            throw '编辑器退出码或 NUnit 结果表明本阶段未通过。'
        }
        if ($ConsumerPackages.Count -gt 0) { Confirm-ConsumerScope $project $ConsumerPackages $xml }
        $result.status = 'passed'
    }
    catch {
        $result.error = $_.Exception.Message
    }
    finally {
        if ($process) {
            if (-not $process.HasExited) {
                Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
                $null = $process.WaitForExit(10000)
            }
            $process.Dispose()
        }
        $result.seconds = [Math]::Round(([DateTime]::UtcNow - $started).TotalSeconds, 2)
    }

    if ($result.status -eq 'passed') { Write-Host "[$Name] $($result.passed)/$($result.total) 通过" }
    else { Write-Warning "[$Name] $($result.error)" }
    return [pscustomobject]$result
}

$stages = @(
    Invoke-Stage 'preflight'
    Invoke-Stage 'main-editmode'
    Invoke-Stage 'core-consumer' @($core)
    Invoke-Stage 'ui-consumer' @($core, $assets, $ugui)
)
$failed = @($stages | Where-Object status -ne 'passed').Count
$summary = Join-Path $runRoot 'run.json'
[ordered]@{
    runName = $RunName; editor = $EditorPath; utc = [DateTime]::UtcNow.ToString('O')
    status = if ($failed) { 'failed' } else { 'passed' }
    stages = $stages
} | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 -LiteralPath $summary
Write-Host "回归报告 $summary"
if ($failed) { exit 1 }
exit 0
