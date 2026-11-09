# IKUN思美创工具箱 - Cimatron 二次开发插件 · 多版本一键安装脚本
# 支持 Cimatron 2024 / 2025 / 2026（E11 见报告中的降级说明）
#
# 插件目录约定（安装脚本自动扫描）：
#   plugins\<插件目录>\plugin.json           插件清单
#   plugins\<插件目录>\src\*.cs              插件源码（安装时按目标版本现场编译）
#   plugins\<插件目录>\payload\common\*      通用二进制（图标、外部 exe）
#   plugins\<插件目录>\payload\plugins\*     预编译 DLL（prebuilt_<版本>_<名称>.dll）
param(
    [switch]$Uninstall,
    [string]$OnlyVersion,
    [switch]$AutoLoadOff
)

$ErrorActionPreference = 'Continue'
$ToolboxName = 'IKUN思美创工具箱'
$PluginsRoot = Join-Path $PSScriptRoot 'plugins'
$FlagSuffix  = if ($AutoLoadOff) { '0' } else { '1' }

$script:Report = New-Object System.Collections.Generic.List[string]

function Say($msg, $color = 'White') {
    Write-Host $msg -ForegroundColor $color
    $script:Report.Add($msg)
}

function Section($msg) { Say ''; Say ('=== ' + $msg + ' ===') 'Cyan' }

function New-BackupRoot {
    $root = Join-Path (Join-Path $env:USERPROFILE 'NcNamer') ('backup_' + (Get-Date -Format 'yyyyMMdd_HHmmss'))
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    return $root
}

function Backup-File($path, $backupRoot, $label) {
    if (-not (Test-Path $path)) { return $null }
    $dest = Join-Path $backupRoot ($label + '_' + (Split-Path $path -Leaf))
    try {
        Copy-Item $path $dest -Force
        return $dest
    } catch { return $null }
}

# ---------- 0. 读取插件清单 ----------
function Get-Plugins {
    $found = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path $PluginsRoot)) { return ,$found }

    $manifests = @(Get-ChildItem -Path $PluginsRoot -Filter 'plugin.json' -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($manifest in $manifests) {
        try {
            $json = Get-Content -LiteralPath $manifest.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $dir = $manifest.DirectoryName
            $sources = New-Object System.Collections.Generic.List[string]
            foreach ($source in @($json.sources)) {
                if (-not $source) { continue }
                $sources.Add((Join-Path $dir ($source -replace '/', '\')))
            }

            $plugin = [PSCustomObject]@{
                Folder          = $dir
                FolderName      = [string]$json.folder
                DisplayName     = [string]$json.displayName
                Dll             = [string]$json.dll
                Sources         = $sources
                CommandClass    = [string]$json.commandClass
                IconSource      = (Join-Path $dir ([string]$json.iconSource -replace '/', '\'))
                PrebuiltPattern = (Join-Path $dir ([string]$json.prebuiltPattern -replace '/', '\'))
                PostProcess     = [string]$json.postProcess
                IniPrefix       = [string]$json.iniPrefix
            }
            $found.Add($plugin)
        } catch {
            Say ("  插件清单解析失败: " + $manifest.FullName + " :: " + $_.Exception.Message) 'Red'
        }
    }
    return ,$found
}

# ---------- 1. 探测本机所有 Cimatron 安装 ----------
function Find-Installations {
    $found = New-Object System.Collections.Generic.List[object]
    $roots = @()
    foreach ($drive in @('C', 'D', 'E', 'F')) {
        $p = "$drive`:\Program Files\Cimatron"
        if (Test-Path $p) { $roots += $p }
    }
    if ($roots.Count -eq 0) { return $found }

    foreach ($root in $roots) {
        # 目录结构有两种:
        #   <ProgramFiles>\Cimatron\Cimatron\2024.0\Program   (2024 及以后)
        #   <ProgramFiles>\Cimatron\CimatronE\Program         (E11 及更早)
        $level1 = @(Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue)
        $dirs = New-Object System.Collections.Generic.List[object]
        foreach ($l1 in $level1) {
            if ($l1.Name -ieq 'Cimatron') {
                foreach ($l2 in (Get-ChildItem -Path $l1.FullName -Directory -ErrorAction SilentlyContinue)) { $dirs.Add($l2) }
            } else {
                $dirs.Add($l1)
            }
        }

        foreach ($dir in $dirs) {
            $name = $dir.Name
            $version = $null
            $isLegacy = $false
            if ($name -match '^(\d{4})\.(\d+)$') { $version = $Matches[1] + '.' + $Matches[2] }
            elseif ($name -match '^(\d{4})$') { $version = $Matches[1] + '.0' }
            elseif ($name -match '^Cimatron[\\ _-]?(\d{4})(?:\.(\d+))?$') {
                $version = $Matches[1] + '.' + $(if ($Matches[2]) { $Matches[2] } else { '0' })
            }
            elseif ($name -ieq 'CimatronE') { $version = 'E11'; $isLegacy = $true }
            else { continue }
            if (-not $version) { continue }

            $programDir = Join-Path $dir.FullName 'Program'
            if (-not (Test-Path $programDir)) { continue }

            # 配置目录：优先 ProgramData，其次安装目录下 Data
            $dataDir = $null
            $candidates = @()
            if ($isLegacy) {
                $candidates += "C:\ProgramData\Cimatron\CimatronE\Data"
                $candidates += (Join-Path $dir.FullName 'Data')
            } else {
                foreach ($drive in @('C', 'D', 'E', 'F')) {
                    $candidates += "$drive`:\ProgramData\Cimatron\Cimatron\$version\Data"
                }
                $candidates += (Join-Path $dir.FullName 'Data')
            }
            foreach ($c in $candidates) { if (Test-Path $c) { $dataDir = $c; break } }
            if (-not $dataDir) { continue }

            # 后处理目录
            $postDirs = New-Object System.Collections.Generic.List[string]
            $relList = @('It\var\post', 'Nc\MachineWorks\post')
            if ($isLegacy) { $relList = @('IT\var\post') }
            foreach ($rel in $relList) {
                $p = Join-Path $dataDir $rel
                if (Test-Path $p) { $postDirs.Add($p) }
            }
            # SourceData 与 E11 安装目录下的 post
            $altList = @((Join-Path $dir.FullName 'SourceData\It\var\post'))
            if ($isLegacy) { $altList += (Join-Path $programDir 'IT\var\post') }
            foreach ($a in $altList) { if (Test-Path $a) { $postDirs.Add($a) } }
            if ($postDirs.Count -eq 0) {
                $alt = Join-Path $dir.FullName 'Data\It\var\post'
                if (Test-Path $alt) { $postDirs.Add($alt) }
            }

            $found.Add([PSCustomObject]@{
                Version     = $version
                IsLegacy    = $isLegacy
                InstallDir  = $dir.FullName
                ProgramDir  = $programDir
                DataDir     = $dataDir
                PostDirs    = $postDirs
                IniPath     = (Join-Path $dataDir 'ExternalCommands.ini')
                HasPluginFx = (Test-Path (Join-Path $programDir 'CimUIInfrastructure.dll'))
            })
        }
    }
    return $found
}

# ---------- 2. 编译插件 DLL（用目标机器自身的 Cimatron 引用） ----------
function Build-Plugin($inst, $plugin, $outDll) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { return "未找到 csc.exe" }

    $refFiles = @(
        'CimUIInfrastructure.dll'
        'CimWpfContracts.dll'
        'interop.CimNcAPI.dll'
        'interop.CimatronE.dll'
        'interop.CimServicesAPI.dll'
        'interop.CimAppAccess.dll'
        'interop.CimBaseAPI.dll'
        'interop.CimMdlrAPI.dll'
    )
    $refs = New-Object System.Collections.Generic.List[string]
    foreach ($f in $refFiles) {
        $full = Join-Path $inst.ProgramDir $f
        if (Test-Path $full) { $refs.Add('/r:' + $full) }
    }
    foreach ($s in @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll')) {
        $refs.Add('/r:' + $s)
    }

    $sources = @($plugin.Sources)
    if ($sources.Count -eq 0) { return "插件没有源码" }

    $tmpName = [IO.Path]::GetFileNameWithoutExtension($plugin.Dll) + '_' + [Guid]::NewGuid().ToString('N') + '.dll'
    $tmpOut = Join-Path $env:TEMP $tmpName
    $out = & $csc /nologo /target:library /platform:x64 /codepage:65001 /out:$tmpOut $refs $sources 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmpOut)) {
        if (Test-Path $tmpOut) { Remove-Item $tmpOut -Force -ErrorAction SilentlyContinue }
        return ("编译失败: " + $out.Trim() + "`n引用: " + ($refs -join ' '))
    }
    try {
        Copy-Item $tmpOut $outDll -Force
    } catch {
        Remove-Item $tmpOut -Force -ErrorAction SilentlyContinue
        return ("DLL 被占用无法替换(请关闭 Cimatron 后重试): " + $_.Exception.Message)
    }
    Remove-Item $tmpOut -Force -ErrorAction SilentlyContinue
    return $null
}

# ---------- 2b. 清理早期版本遗留的图标文件（仅 nc-rename） ----------
function Remove-LegacyIcons($inst) {
    $dirs = @($inst.ProgramDir, (Join-Path $env:USERPROFILE 'NcNamer'))
    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($d in $dirs) {
        if (-not (Test-Path $d)) { continue }
        foreach ($f in @(Get-ChildItem -Path $d -Filter 'NcNamer_icon*' -File -ErrorAction SilentlyContinue)) {
            if ($f.Name -ieq 'NcNamer_icon64.ico') { continue }
            $targets.Add($f.FullName)
        }
    }
    $removed = New-Object System.Collections.Generic.List[string]
    foreach ($t in $targets) {
        try { Remove-Item -LiteralPath $t -Force; $removed.Add($t) } catch { }
    }
    return ,$removed
}

# ---------- 2c. 编译独立更新器 ----------
function Build-Updater {
    $src = Join-Path $PSScriptRoot 'updater\IKUNUpdater.cs'
    if (-not (Test-Path $src)) { return ('更新器源码缺失: ' + $src) }

    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path $csc)) { return '未找到 csc.exe' }

    $outDir = Join-Path $env:USERPROFILE 'NcNamer'
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $out = Join-Path $outDir 'IKUNUpdater.exe'
    $tmp = Join-Path $env:TEMP ('IKUNUpdater_' + [Guid]::NewGuid().ToString('N') + '.exe')

    $refs = @(
        '/r:System.dll'
        '/r:System.Core.dll'
        '/r:System.Windows.Forms.dll'
        '/r:System.Drawing.dll'
        '/r:System.IO.Compression.dll'
        '/r:System.IO.Compression.FileSystem.dll'
    )
    $outText = & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$tmp" $refs $src 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmp)) {
        if (Test-Path $tmp) { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
        return ('更新器编译失败: ' + $outText.Trim())
    }

    try {
        Copy-Item $tmp $out -Force
    } catch {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        return ('更新器无法写入(可能正在运行): ' + $_.Exception.Message)
    }
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    return $null
}

# ---------- 3. 注册 ExternalCommands.ini ----------
function Get-IniPrefix($plugin) {
    if ($plugin.IniPrefix) { return $plugin.IniPrefix }
    return (($plugin.CommandClass -split '\.')[0])
}

function Register-Command($iniPath, $plugin, $entry) {
    if (-not (Test-Path $iniPath)) { return "ExternalCommands.ini 不存在: $iniPath" }
    $prefix = [Regex]::Escape((Get-IniPrefix $plugin))
    $lines = [IO.File]::ReadAllLines($iniPath)
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($l in $lines) {
        if ($l -match ('^\s*' + $prefix + '[^=]*=')) { continue }
        # ResetApiCommands=1 会让 Cimatron 在启动时把所有插件项改回 @0，
        # 结果菜单永远不会被插入（AddApiDefaultMenu 只在 ResetMode==1 时执行）。
        if ($l -match '^\s*ResetApiCommands\s*=') { $kept.Add('ResetApiCommands =0'); continue }
        $kept.Add($l)
    }
    $result = New-Object System.Collections.Generic.List[string]
    $inserted = $false
    $hasGlobal = $false
    for ($i = 0; $i -lt $kept.Count; $i++) {
        if ($kept[$i].Trim() -eq '[Global Flags]') { $hasGlobal = $true }
        $result.Add($kept[$i])
        if ($kept[$i].Trim() -eq '[Plugin Ext Commands]') {
            $result.Add($entry)
            $inserted = $true
        }
    }
    if (-not $inserted) {
        $result.Add('')
        $result.Add('[Plugin Ext Commands]')
        $result.Add($entry)
    }
    if (-not $hasGlobal) {
        $result.Insert(0, '')
        $result.Insert(1, '[Global Flags]')
        $result.Insert(2, 'ResetApiCommands =0')
    }
    [IO.File]::WriteAllLines($iniPath, $result, (New-Object System.Text.UTF8Encoding($false)))
    return $null
}

function Remove-CommandRegistration($iniPath, $plugin) {
    if (-not (Test-Path $iniPath)) { return $false }
    $prefix = [Regex]::Escape((Get-IniPrefix $plugin))
    $lines = [IO.File]::ReadAllLines($iniPath)
    $kept = @($lines | Where-Object { $_ -notmatch ('^\s*' + $prefix + '[^=]*=') })
    if ($kept.Count -eq $lines.Count) { return $false }
    [IO.File]::WriteAllLines($iniPath, $kept, (New-Object System.Text.UTF8Encoding($false)))
    return $true
}

# ---------- 3b. 清理工具栏里堆积的匿名分组（仅 nc-rename） ----------
function Remove-StrayToolbarGroups($dataDir, $backupRoot) {
    $script:RemovedCount = 0
    $layout = Join-Path $dataDir 'CommandsLayout.xml'
    if (-not (Test-Path $layout)) { return 0 }

    $text = [IO.File]::ReadAllText($layout, [Text.Encoding]::UTF8)

    $staleNames = @('NcNamerAutoService', 'IKUN tools')

    $menuHit = [Regex]::Match($text, 'NcNamer[\s\S]{0,800}?<Id Type="Simple" Id="(\d+)"')
    if (-not $menuHit.Success) { $menuHit = [Regex]::Match($text, 'IKUN[\s\S]{0,800}?<Id Type="Simple" Id="(\d+)"') }
    if (-not $menuHit.Success) { return 0 }
    $cmdId = $menuHit.Groups[1].Value

    $grpPattern = '<StreepGroupModel [^>]*Name="(Group -?\d+)"[^>]*>[\s\S]*?</StreepGroupModel>'
    $result = [Regex]::Replace($text, $grpPattern, {
        param($m)
        $ids = @([Regex]::Matches($m.Value, '<Id Type="Simple" Id="(\d+)"') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if ($ids.Count -eq 1 -and $ids[0] -eq $cmdId) { $script:RemovedCount++; return '' }
        return $m.Value
    })

    foreach ($stale in $staleNames) {
        $esc = [Regex]::Escape($stale)
        $subPattern = '<StreepSubMenuGroupModel [^>]*Name="' + $esc + '"[^>]*>[\s\S]*?</StreepSubMenuGroupModel>'
        $result = [Regex]::Replace($result, $subPattern, {
            param($m)
            if ($m.Value -match ('Type="Simple" Id="' + $cmdId + '"')) { $script:RemovedCount++; return '' }
            return $m.Value
        })
    }

    foreach ($stale in $staleNames) {
        $esc = [Regex]::Escape($stale)
        $menuPattern = '<StreepSubMenuModel [^>]*Name="' + $esc + '"[^>]*>[\s\S]*?</StreepSubMenuModel>'
        $result = [Regex]::Replace($result, $menuPattern, {
            param($m)
            if ($m.Value -match ('Type="Simple" Id="' + $cmdId + '"')) { $script:RemovedCount++; return '' }
            return $m.Value
        })
    }

    if ($script:RemovedCount -gt 0) {
        $null = Backup-File $layout $backupRoot 'CommandsLayout'
        [IO.File]::WriteAllText($layout, $result, (New-Object System.Text.UTF8Encoding($false)))
        return $script:RemovedCount
    }
    return 0
}

# ---------- 3c. 投放命令图标 ----------
function Get-ApiCommandId([string]$key) {
    $text = $key.ToLowerInvariant()
    $sum = 0
    foreach ($ch in $text.ToCharArray()) {
        $v = [int]$ch - 65 + 1
        if ($v -ge 0) { $sum += $v }
        elseif ([int]$ch -ge 48 -and [int]$ch -le 57) { $sum += ([int]$ch - 48) }
    }
    return 49152 + ($sum % 4096)
}

function Test-CommandInPartMenu($dataDir, $commandClass) {
    $layout = Join-Path $dataDir 'CommandsLayout.xml'
    if (-not (Test-Path $layout)) { return $false }
    $id = Get-ApiCommandId $commandClass
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.Load($layout)
        $menus = @($xml.SelectNodes('//StreepSubMenuModel[@Application="Part" and @Name="IKUN思美创工具箱"]'))
        foreach ($menu in $menus) {
            foreach ($node in @($menu.SelectNodes('.//Id[@Type="Simple"]'))) {
                if ($node.GetAttribute('Id') -eq [string]$id) { return $true }
            }
        }
    } catch { return $false }
    return $false
}

# 同一命令在 @1 状态下被多次启动 Cimatron 会重复插入同一工具栏组。
# 清理 IKUN 工具箱工具栏组里的重复命令，保留第一个，避免按钮越开越多。
function Remove-DuplicateToolbarItems($dataDir, $backupRoot) {
    $layout = Join-Path $dataDir 'CommandsLayout.xml'
    if (-not (Test-Path $layout)) { return 0 }

    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($layout)

    $removed = 0
    foreach ($group in @($xml.SelectNodes('//StreepGroupModel[@Name="IKUN思美创工具箱"]'))) {
        $seen = @{}
        foreach ($item in @($group.SelectNodes('Items/StreepBaseItem'))) {
            $idNode = $item.SelectSingleNode('Id[@Type="Simple"]')
            if ($null -eq $idNode) { continue }
            $id = $idNode.GetAttribute('Id')
            if ($seen.ContainsKey($id)) {
                $item.ParentNode.RemoveChild($item) | Out-Null
                $removed++
            } else {
                $seen[$id] = $true
            }
        }
    }

    if ($removed -gt 0) {
        $null = Backup-File $layout $backupRoot 'CommandsLayout_dedupe'
        $settings = New-Object System.Xml.XmlWriterSettings
        $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
        $settings.Indent = $false
        $settings.OmitXmlDeclaration = $false
        $writer = [System.Xml.XmlWriter]::Create($layout, $settings)
        try { $xml.Save($writer) } finally { $writer.Close() }
    }
    return $removed
}

function Install-CommandIcon($inst, $plugin) {
    $src = $plugin.IconSource
    if (-not (Test-Path $src)) { return ('图标源文件缺失: ' + $src) }

    $imgDir = Join-Path $inst.DataDir 'Images\Commands'
    if (-not (Test-Path $imgDir)) { return ('图标目录不存在: ' + $imgDir) }

    $id = Get-ApiCommandId $plugin.CommandClass
    $fileName = '{0}_IKUNtools.ico' -f $id
    $dst = Join-Path $imgDir $fileName
    try {
        Copy-Item $src $dst -Force
        if (-not (Test-Path $dst)) { return ('图标复制后未找到: ' + $dst) }
        return $null
    } catch {
        return ('图标复制失败: ' + $_.Exception.Message)
    }
}

function Remove-CommandIcon($inst, $plugin) {
    $imgDir = Join-Path $inst.DataDir 'Images\Commands'
    if (-not (Test-Path $imgDir)) { return 0 }
    $id = Get-ApiCommandId $plugin.CommandClass
    $removed = 0
    foreach ($suffix in @('_IKUNtools.ico', '_NcNamer.ico')) {
        $dst = Join-Path $imgDir ('{0}{1}' -f $id, $suffix)
        if (Test-Path $dst) {
            try { Remove-Item $dst -Force; $removed++ } catch { }
        }
    }
    return $removed
}

# ---------- 4. NC重命名 专属：EXF / DEX / 钩子 ----------
function Inject-Exf($exfPath) {
    if (-not (Test-Path $exfPath)) { return "BJ.EXF 不存在" }
    $text = [IO.File]::ReadAllText($exfPath, [Text.Encoding]::Default)
    if ($text -match 'NCMETA\s+PROC') { return 'already' }

    $lines = [IO.File]::ReadAllLines($exfPath, [Text.Encoding]::Default)
    $out = New-Object System.Collections.Generic.List[string]
    $anchor = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $out.Add($lines[$i])
        if ($lines[$i] -match '^\s*BEGINNING\s+OF\s+PROC\s*:') { $anchor = $i }
        if ($anchor -ge 0 -and $i -eq $anchor) {
            $out.Add('      OUTPUT $ "( NCMETA PROC=" PROC_NUM " )" ;')
        }
    }
    if ($anchor -lt 0) { return "未找到 BEGINNING OF PROC 锚点，需手动注入" }
    [IO.File]::WriteAllLines($exfPath, $out, (New-Object System.Text.UTF8Encoding($false)))
    return $null
}

function Compile-Dex($inst, $postDir) {
    $dfexf = Join-Path $inst.ProgramDir 'dfexf.exe'
    if (-not (Test-Path $dfexf)) { return "未找到 dfexf.exe" }
    $exf = Join-Path $postDir 'BJ.EXF'
    if (-not (Test-Path $exf)) { return "该目录无 BJ.EXF" }
    Push-Location $postDir
    try {
        $null = ("" | & $dfexf BJ 2>&1 | Out-String)
        Pop-Location
    } catch { Pop-Location }
    $dex = Join-Path $postDir 'bj.dex'
    if (-not (Test-Path $dex)) { return "dex 未生成" }
    if ((Get-Item $dex).Length -lt 100) { return "dex 大小异常" }
    return $null
}

function Get-HookLine {
    return 'for /f "delims=" %%i in (''"%~dp0NcPostRename.exe" /ownerpid'') do start "" /min "%~dp0NcPostRename.exe" /owner=%%i'
}

function Write-Hook($postDir, $hookName, $backupRoot) {
    $target = Join-Path $postDir $hookName
    $marker = 'NcPostRename.exe'
    $hookLine = Get-HookLine
    $text = ''
    $exists = Test-Path $target
    if ($exists) {
        $text = [IO.File]::ReadAllText($target, [Text.Encoding]::Default)
        if ($text -match [Regex]::Escape($hookLine)) { return 'already' }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    if ($exists) {
        foreach ($l in ($text -split "`r?`n")) {
            if ($l -match [Regex]::Escape($marker)) { continue }
            if ($l.Trim() -eq '') { continue }
            $lines.Add($l)
        }
        if ($lines.Count -gt 0) { $null = Backup-File $target $backupRoot ("hook_" + $hookName) }
    }
    if ($lines.Count -eq 0) { $lines.Add('@echo off') }
    if (-not ($lines[0] -match '(?i)^\s*@echo\s+off')) { $lines.Insert(0, '@echo off') }

    $body = ($lines -join "`r`n").TrimEnd() + "`r`n" + $hookLine + "`r`n"
    [IO.File]::WriteAllText($target, $body, (New-Object System.Text.UTF8Encoding($false)))
    return $null
}

function Install-NcRenamePost($inst, $plugin, $backupRoot) {
    $commonDir = Join-Path $plugin.Folder 'payload\common'
    foreach ($pd in $inst.PostDirs) {
        Say ("  -- post: " + $pd) 'Gray'
        $dst = Join-Path $pd 'NcPostRename.exe'
        if (Test-Path $dst) { $null = Backup-File $dst $backupRoot ('exe_' + $inst.Version) }
        Copy-Item (Join-Path $commonDir 'NcPostRename.exe') $dst -Force
        Say '     NcPostRename.exe 已部署' 'Green'

        foreach ($hook in @('BJ.BAT', 'BJ.CMD')) {
            $r = Write-Hook $pd $hook $backupRoot
            if ($r -eq 'already') { Say ("     钩子已存在: " + $hook) 'Gray' }
            elseif ($r) { Say ("     钩子失败(" + $hook + "): " + $r) 'Yellow' }
            else { Say ("     钩子已写入: " + $hook) 'Green' }
        }

        $exf = Join-Path $pd 'BJ.EXF'
        if (Test-Path $exf) {
            $r = Inject-Exf $exf
            if ($r -eq 'already') { Say '     BJ.EXF 已含 NCMETA' 'Gray' }
            elseif ($r) { Say ("     BJ.EXF 注入失败: " + $r) 'Yellow' }
            else {
                $null = Backup-File $exf $backupRoot ('exf_' + $inst.Version)
                Say '     BJ.EXF 已注入 NCMETA PROC' 'Green'
                $rc = Compile-Dex $inst $pd
                if ($rc) { Say ("     dex 编译失败: " + $rc) 'Yellow' }
                else { Say '     bj.dex 已重新编译' 'Green' }
            }
        } else {
            Say '     该目录无 BJ.EXF，跳过' 'Gray'
        }
    }
}

function Uninstall-NcRenamePost($inst) {
    foreach ($pd in $inst.PostDirs) {
        foreach ($h in @((Join-Path $pd 'BJ.BAT'), (Join-Path $pd 'BJ.CMD'))) {
            if (Test-Path $h) {
                $txt = [IO.File]::ReadAllText($h, [Text.Encoding]::Default)
                if ($txt -match 'NcPostRename.exe') {
                    $txt = ($txt -split "`r?`n" | Where-Object { $_ -notmatch 'NcPostRename.exe' }) -join "`r`n"
                    [IO.File]::WriteAllText($h, $txt, (New-Object System.Text.UTF8Encoding($false)))
                    Say ("  已清理钩子: " + $h) 'Green'
                }
            }
        }
    }
}

# ================= 主流程 =================
Section 'IKUN思美创工具箱 安装程序'
$backupRoot = New-BackupRoot
Say ("备份目录: " + $backupRoot) 'Yellow'

$plugins = Get-Plugins
if ($plugins.Count -eq 0) { Say '未找到任何插件清单 (plugins\*\plugin.json)' 'Red'; exit 1 }
Say ("已加载插件: " + (($plugins | ForEach-Object { $_.DisplayName }) -join ' / ')) 'Gray'

Section '更新器'
$updaterErr = Build-Updater
if ($updaterErr) {
    Say ('  更新器编译失败: ' + $updaterErr) 'Yellow'
} else {
    Say ('  更新器已安装: ' + (Join-Path $env:USERPROFILE 'NcNamer\IKUNUpdater.exe')) 'Green'
}

$installs = Find-Installations
if ($installs.Count -eq 0) { Say '未探测到任何 Cimatron 安装' 'Red'; exit 1 }

foreach ($inst in $installs) {
    if ($OnlyVersion -and ($inst.Version -notlike "*$OnlyVersion*")) { continue }
    Section ("版本 " + $inst.Version + "  (" + $inst.InstallDir + ")")
    Say ("  Program : " + $inst.ProgramDir) 'Gray'
    Say ("  Data    : " + $inst.DataDir) 'Gray'
    Say ("  Post    : " + ($inst.PostDirs -join ' | ')) 'Gray'

    if ($Uninstall) {
        foreach ($plugin in $plugins) {
            $dllPath = Join-Path $inst.ProgramDir $plugin.Dll
            if (Test-Path $dllPath) { $null = Backup-File $dllPath $backupRoot ('dll_' + $inst.Version + '_' + $plugin.FolderName) }

            if (Test-Path $inst.IniPath) {
                $null = Backup-File $inst.IniPath $backupRoot ('ini_' + $inst.Version)
                if (Remove-CommandRegistration $inst.IniPath $plugin) {
                    Say ("  已移除注册: " + $plugin.DisplayName) 'Green'
                }
            }

            $iconGone = Remove-CommandIcon $inst $plugin
            if ($iconGone -gt 0) { Say ("  已移除命令图标: " + $plugin.DisplayName) 'Green' }

            if (Test-Path $dllPath) {
                try {
                    Remove-Item $dllPath -Force
                    Say ("  已移除 DLL: " + $plugin.Dll) 'Green'
                } catch {
                    Say ("  DLL 删除失败(可能被占用): " + $_.Exception.Message) 'Yellow'
                }
            }

            if ($plugin.PostProcess -eq 'nc-rename') {
                $gone = Remove-LegacyIcons $inst
                if ($gone.Count -gt 0) { Say ("  已清除遗留图标文件 " + $gone.Count + " 个") 'Green' }
                $stray = Remove-StrayToolbarGroups $inst.DataDir $backupRoot
                if ($stray -gt 0) { Say ("  已清理堆积的工具栏按钮 " + $stray + " 个") 'Green' }
                Uninstall-NcRenamePost $inst
            }
        }
        continue
    }

    if (-not $inst.HasPluginFx) {
        Say '  [跳过] 该版本缺少插件框架(CimUIInfrastructure)，即 Cimatron E11 及更早版本' 'Yellow'
        Say '         原因: E11 无插件接口可实时取数，其 GPP1 后处理也没有最小刀长变量，' 'Yellow'
        Say '               无法满足"刀长必须及时准确"的要求。按 fail-closed 原则保持现状不安装。' 'Yellow'
        Say '         可选出路: 将 BJ 后处理迁移到 GPP2(ex2) 后可用 XML_MIN_CLEAR_LEN 直出刀长，需现场开发。' 'Yellow'
        continue
    }

    $iniBackedUp = $false

    foreach ($plugin in $plugins) {
        Say ("  ── " + $plugin.DisplayName) 'Cyan'

        $dllPath = Join-Path $inst.ProgramDir $plugin.Dll
        if (Test-Path $dllPath) { $null = Backup-File $dllPath $backupRoot ('dll_' + $inst.Version + '_' + $plugin.FolderName) }

        # 图标源文件必须和插件 DLL 放在同一目录，插件启动时据此投放到 Images\Commands
        if (Test-Path $plugin.IconSource) {
            $iconLeaf = Split-Path $plugin.IconSource -Leaf
            Copy-Item $plugin.IconSource (Join-Path $inst.ProgramDir $iconLeaf) -Force
        }

        if ($plugin.PostProcess -eq 'nc-rename') {
            foreach ($legacy in @('NcNamer_icon64.png', 'NcNamer_icon32.png', 'NcNamer_icon16.png')) {
                $p = Join-Path $inst.ProgramDir $legacy
                if (Test-Path $p) { try { Remove-Item $p -Force } catch { } }
            }
        }

        $err = Build-Plugin $inst $plugin $dllPath
        if ($err) {
            Say ("  现场编译失败: " + $err) 'Yellow'
            $major = $inst.Version -replace '\.0$', ''
            $pre = $plugin.PrebuiltPattern.Replace('{version}', $major)
            if (Test-Path $pre) {
                try {
                    Copy-Item $pre $dllPath -Force
                    Say ("  已改用预编译 DLL: " + (Split-Path $pre -Leaf)) 'Yellow'
                } catch {
                    Say ("  预编译 DLL 也无法替换(DLL 被占用，请关闭 Cimatron 后重跑): " + $_.Exception.Message) 'Red'
                    continue
                }
            } else {
                Say ("  无可用预编译 DLL，插件安装失败: " + $plugin.DisplayName) 'Red'
                continue
            }
        } else {
            Say ("  插件已编译: " + $dllPath) 'Green'
        }

        if (-not $iniBackedUp -and (Test-Path $inst.IniPath)) {
            $null = Backup-File $inst.IniPath $backupRoot ('ini_' + $inst.Version)
            $iniBackedUp = $true
        }
        $flag = $FlagSuffix
        if (-not $AutoLoadOff -and (Test-CommandInPartMenu $inst.DataDir $plugin.CommandClass)) {
            $flag = '0'
            Say '  Part 菜单已包含该命令，注册为 @0（避免重复插入）' 'Gray'
        } elseif (-not $AutoLoadOff) {
            Say '  Part 菜单尚无该命令，注册为 @1（首次插入）' 'Gray'
        }
        $entry = $plugin.CommandClass + '=' + $plugin.CommandClass + '@' + $flag
        $err = Register-Command $inst.IniPath $plugin $entry
        if ($err) { Say ("  注册失败: " + $err) 'Red' } else { Say ("  已注册: " + $entry) 'Green' }

        if ($plugin.PostProcess -eq 'nc-rename') {
            $stray = Remove-StrayToolbarGroups $inst.DataDir $backupRoot
            if ($stray -gt 0) { Say ("  已清理堆积的工具栏按钮 " + $stray + " 个") 'Green' }
        }

        $iconErr = Install-CommandIcon $inst $plugin
        $iconId = Get-ApiCommandId $plugin.CommandClass
        $iconFile = '{0}_IKUNtools.ico' -f $iconId
        if ($iconErr) {
            Say ("  图标安装失败: " + $iconErr) 'Yellow'
        } else {
            Say ("  图标已安装: " + $iconFile) 'Green'
        }

        if ($plugin.PostProcess -eq 'nc-rename') {
            Install-NcRenamePost $inst $plugin $backupRoot
        }
    }

    $deduped = Remove-DuplicateToolbarItems $inst.DataDir $backupRoot
    if ($deduped -gt 0) { Say ("  已清理重复的工具栏按钮 " + $deduped + " 个") 'Green' }
}

Section '安装结果'
Say '已处理版本:' 'Cyan'
foreach ($inst in $installs) { Say ('  - Cimatron ' + $inst.Version) }
Say ''
Say '生效条件: 重启 Cimatron（插件在启动时加载，无需手工点击）' 'Yellow'
Say '日志位置: %USERPROFILE%\NcNamer\auto_service_log.txt、post_rename_log.txt、pin_flatten_log.txt' 'Gray'
Say ("备份位置: " + $backupRoot) 'Gray'

$logPath = Join-Path (Join-Path $env:USERPROFILE 'NcNamer') 'install_report.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null
[IO.File]::WriteAllLines($logPath, $script:Report, (New-Object System.Text.UTF8Encoding($true)))
