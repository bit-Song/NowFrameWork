param([string]$UniTaskPath = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'NowFrameWork/Build/StorageRegression/Consumer'
$marker = Join-Path $project '.nw-storage-consumer'
if ((Test-Path -LiteralPath $project) -and -not (Test-Path -LiteralPath $marker)) { throw '目标目录已存在且没有隔离标记，拒绝覆盖' }
if (-not $UniTaskPath) {
    $cached = @(Get-ChildItem -LiteralPath (Join-Path $repo 'NowFrameWork/Library/PackageCache') -Directory | Where-Object Name -like 'com.cysharp.unitask@*')
    if ($cached.Count -ne 1) { throw '未找到唯一的本地 UniTask 缓存，请传入 -UniTaskPath' }
    $UniTaskPath = $cached[0].FullName
}
$UniTaskPath = (Resolve-Path -LiteralPath $UniTaskPath).Path
$package = Get-Content -Raw -LiteralPath (Join-Path $UniTaskPath 'package.json') | ConvertFrom-Json
if ($package.name -ne 'com.cysharp.unitask') { throw 'UniTaskPath 不是 UniTask 包目录' }
New-Item -ItemType Directory -Path (Join-Path $project 'Assets'),(Join-Path $project 'Packages'),(Join-Path $project 'ProjectSettings') -Force | Out-Null
[IO.File]::WriteAllText($marker, 'NowFrameWork.StorageConsumer.v1')
$dependencies = [ordered]@{}
foreach ($name in @('core','unity')) {
    $dependencies['com.now.nowframework.' + $name] = 'file:' + (Join-Path $repo ('Framework/com.now.nowframework.' + $name)).Replace('\','/')
}
$dependencies['com.cysharp.unitask'] = 'file:' + $UniTaskPath.Replace('\','/')
$dependencies['com.unity.modules.jsonserialize'] = '1.0.0'
$dependencies['com.unity.modules.imgui'] = '1.0.0'
[IO.File]::WriteAllText((Join-Path $project 'Packages/manifest.json'), (@{ dependencies=$dependencies } | ConvertTo-Json -Depth 4))
Copy-Item -LiteralPath (Join-Path $repo 'NowFrameWork/ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Force
$source = Join-Path $PSScriptRoot 'StorageRegression'
foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
    $relative = $file.FullName.Substring($source.Length).TrimStart([char[]]'\/')
    $target = Join-Path (Join-Path $project 'Assets/StorageProbe') $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
Write-Output ('隔离消费工程：' + $project)
Write-Output '用相同团结编辑器以 -batchmode -nographics -buildTarget Win64 -executeMethod NowFrameWork.Validation.Editor.StorageProbeBuild.Build 构建，输出到 Build/StorageRegression/Player。'
