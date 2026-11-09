# 生成自动更新用的原子替换资源包
# 用法：先关闭 Cimatron，运行 安装.cmd 生成/安装最新 DLL 和更新器，
#       再运行本脚本。把 dist\IKUN_Cimatron_Plugins_<大版本>.zip 上传到对应 Release。
param(
    [string]$Version = '2024.0',
    [string]$ProgramDir = '',
    [string]$DataDir = ''
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$major = ($Version -split '\.')[0]

if (-not $ProgramDir) { $ProgramDir = "C:\Program Files\Cimatron\Cimatron\$Version\Program" }
if (-not $DataDir) { $DataDir = "C:\ProgramData\Cimatron\Cimatron\$Version\Data" }

$toolboxSource = Join-Path $repo 'plugins\nc-rename\src\ToolboxUpdate.cs'
$toolboxVersion = $null
if (Test-Path $toolboxSource) {
    $text = [IO.File]::ReadAllText($toolboxSource, [Text.Encoding]::UTF8)
    $m = [Regex]::Match($text, 'CurrentVersion\s*=\s*"([^"]+)"')
    if ($m.Success) { $toolboxVersion = $m.Groups[1].Value }
}
if (-not $toolboxVersion) { throw '无法从 ToolboxUpdate.cs 读取 CurrentVersion' }

$updater = Join-Path $env:USERPROFILE 'NcNamer\IKUNUpdater.exe'
$imageDir = Join-Path $DataDir 'Images\Commands'

$files = @(
    @{ Source = (Join-Path $ProgramDir 'NcNamerAutoService.dll');  Name = 'NcNamerAutoService.dll' },
    @{ Source = (Join-Path $ProgramDir 'EjectorPinFlatten.dll');   Name = 'EjectorPinFlatten.dll' },
    @{ Source = (Join-Path $ProgramDir 'NcNamer_icon64.ico');     Name = 'NcNamer_icon64.ico' },
    @{ Source = (Join-Path $ProgramDir 'EjectorPin_icon64.ico');  Name = 'EjectorPin_icon64.ico' },
    @{ Source = (Join-Path $imageDir '50468_IKUNtools.ico');      Name = '50468_IKUNtools.ico' },
    @{ Source = (Join-Path $imageDir '50897_IKUNtools.ico');      Name = '50897_IKUNtools.ico' },
    @{ Source = $updater;                                         Name = 'IKUNUpdater.exe' }
)

foreach ($f in $files) {
    if (-not (Test-Path $f.Source)) {
        throw ("缺少资源: " + $f.Source + "`n请先关闭 Cimatron，运行 安装.cmd，再运行本脚本。")
    }
}

$stage = Join-Path $repo ('dist\payload_' + $major)
$zip = Join-Path $repo ('dist\IKUN_Cimatron_Plugins_' + $major + '.zip')
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

foreach ($f in $files) {
    Copy-Item $f.Source (Join-Path $stage $f.Name) -Force
}

$manifest = @(
    ('version=' + $toolboxVersion),
    ('cimatron=' + $Version),
    ('built=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
)
[IO.File]::WriteAllLines((Join-Path $stage 'manifest.txt'), $manifest, (New-Object System.Text.UTF8Encoding($false)))

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force

Write-Host ('版本: ' + $toolboxVersion) -ForegroundColor Cyan
Write-Host ('资源: ' + $zip) -ForegroundColor Green
Write-Host ('大小: ' + [Math]::Round((Get-Item $zip).Length / 1KB, 1) + ' KB') -ForegroundColor Gray
Write-Host ('下一步：Release tag 必须与资源版本一致（v' + $toolboxVersion + '）。') -ForegroundColor Yellow
Write-Host '如果要测试自动更新，先把 ToolboxUpdate.cs 的 CurrentVersion 调到更高版本，重新安装并重建资源。' -ForegroundColor Yellow
