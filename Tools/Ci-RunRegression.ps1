param([string]$EditorPath = $env:TUANJIE_EDITOR_PATH)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$copyDir = Join-Path $root "NowFrameWork/Build/FrameworkRegression"
$transcript = Join-Path $env:RUNNER_TEMP "ci-step.log"

try {
    try { Start-Transcript -Path $transcript -Force | Out-Null } catch { }

    Write-Host "仓库: $env:GITHUB_REPOSITORY  提交: $env:GITHUB_SHA"
    Write-Host "工作目录: $(Get-Location)"
    Write-Host "编辑器路径变量: [$EditorPath]"
    Write-Host "执行策略(Process): $(Get-ExecutionPolicy -Scope Process)"

    if (-not $EditorPath) {
        throw "仓库变量 TUANJIE_EDITOR_PATH 未配置（Settings > Secrets and variables > Actions > Variables）"
    }

    if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
        throw "编辑器路径不存在或不是文件: $EditorPath"
    }

    $regression = Join-Path $root "Tools/Run-FrameworkRegression.ps1"
    if (-not (Test-Path -LiteralPath $regression -PathType Leaf)) {
        throw "找不到 Tools/Run-FrameworkRegression.ps1"
    }

    & $regression -EditorPath $EditorPath

    $exitCode = $LASTEXITCODE
    Write-Host "回归脚本退出码: $exitCode"

    if ($exitCode -ne 0) { throw "框架回归失败，退出码 $exitCode" }
}
catch {
    Write-Host "步骤异常: $($_.Exception.GetType().FullName): $($_.Exception.Message)"
    throw
}
finally {
    try { Stop-Transcript | Out-Null } catch { }

    # 无论成败都把输出复制到 Build，随 artifact 上传；CI 不需要它留在 _temp
    try {
        New-Item -ItemType Directory -Force -Path $copyDir | Out-Null
        if (Test-Path $transcript) { Copy-Item $transcript (Join-Path $copyDir "ci-step.log") -Force }
    }
    catch { }
}
