# NcNamer 自动命名插件 - 多版本一键安装脚本
# 支持 Cimatron 2024 / 2025 / 2026（E11 见报告中的降级说明）
param(
    [switch]$Uninstall,
    [string]$OnlyVersion,
    [switch]$AutoLoadOff
)

$ErrorActionPreference = 'Continue'
$PayloadRoot = Join-Path $PSScriptRoot 'payload'
$SrcDir      = Join-Path $PayloadRoot 'src'
$CommonDir   = Join-Path $PayloadRoot 'common'
$PluginDir   = Join-Path $PayloadRoot 'plugins'
$PluginSrc   = Join-Path $SrcDir 'NcNamerAutoService.cs'
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
function Build-Plugin($inst, $outDll) {
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
    )
    $refs = New-Object System.Collections.Generic.List[string]
    foreach ($f in $refFiles) {
        $full = Join-Path $inst.ProgramDir $f
        if (Test-Path $full) { $refs.Add('/r:' + $full) }
    }
    foreach ($s in @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Windows.Forms.dll')) {
        $refs.Add('/r:' + $s)
    }

    $tmpOut = Join-Path $env:TEMP ('NcNamerAutoService_' + [Guid]::NewGuid().ToString('N') + '.dll')
    # 图标以 Win32 资源嵌入 DLL，Cimatron 才能通过 LoadIcon 取到
    $iconArg = @()
    $icoPath = Join-Path $CommonDir 'NcNamer_icon64.ico'
    if (Test-Path $icoPath) { $iconArg = @('/win32icon:' + $icoPath) }
    $out = & $csc /nologo /target:library /platform:x64 /out:$tmpOut $iconArg $refs $PluginSrc 2>&1 | Out-String
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

# ---------- 2b. 清理早期版本遗留的图标文件 ----------
# 早期调试版本曾向 Program 目录与 %USERPROFILE%\NcNamer\ 写入 NcNamer_icon*.* ，
# 现已不再使用自定义图标，安装/卸载时一并清除，避免残留。
function Remove-LegacyIcons($inst) {
    $dirs = @($inst.ProgramDir, (Join-Path $env:USERPROFILE 'NcNamer'))
    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($d in $dirs) {
        if (-not (Test-Path $d)) { continue }
        foreach ($f in @(Get-ChildItem -Path $d -Filter 'NcNamer_icon*' -File -ErrorAction SilentlyContinue)) {
            $targets.Add($f.FullName)
        }
    }
    $removed = New-Object System.Collections.Generic.List[string]
    foreach ($t in $targets) {
        try { Remove-Item -LiteralPath $t -Force; $removed.Add($t) } catch { }
    }
    return ,$removed
}

# ---------- 3. 注册 ExternalCommands.ini ----------
function Register-Command($iniPath, $entry) {
    if (-not (Test-Path $iniPath)) { return "ExternalCommands.ini 不存在: $iniPath" }
    $lines = [IO.File]::ReadAllLines($iniPath)
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($l in $lines) {
        if ($l -match '^\s*NcNamer[^=]*=') { continue }
        $kept.Add($l)
    }
    $result = New-Object System.Collections.Generic.List[string]
    $inserted = $false
    for ($i = 0; $i -lt $kept.Count; $i++) {
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
    [IO.File]::WriteAllLines($iniPath, $result, (New-Object System.Text.UTF8Encoding($false)))
    return $null
}

# ---------- 3b. 清理工具栏里堆积的匿名分组 ----------
# 早期版本把 ApiCommand.ToolbarName 留空，Cimatron 每次启动都会新建一个
# "Group -1234567890" 工具栏分组放进 CommandsLayout.xml，导致按钮越启动越多。
# 这里把只包含本插件命令的匿名分组删掉，只保留菜单项。
function Remove-StrayToolbarGroups($dataDir, $backupRoot) {
    $script:RemovedCount = 0
    $layout = Join-Path $dataDir 'CommandsLayout.xml'
    if (-not (Test-Path $layout)) { return 0 }

    $text = [IO.File]::ReadAllText($layout, [Text.Encoding]::UTF8)
    $menuHit = [Regex]::Match($text, 'NcNamer[\s\S]{0,800}?<Id Type="Simple" Id="(\d+)"')
    if (-not $menuHit.Success) { return 0 }
    $cmdId = $menuHit.Groups[1].Value

    $pattern = '<StreepGroupModel [^>]*Name="(Group -?\d+)"[^>]*>[\s\S]*?</StreepGroupModel>'
    $result = [Regex]::Replace($text, $pattern, {
        param($m)
        $body = $m.Value
        $simpleIds = @([Regex]::Matches($body, '<Id Type="Simple" Id="(\d+)"') |
            ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
        if ($simpleIds.Count -eq 1 -and $simpleIds[0] -eq $cmdId) {
            $script:RemovedCount++
            return ''
        }
        return $body
    })

    if ($script:RemovedCount -gt 0) {
        $null = Backup-File $layout $backupRoot 'CommandsLayout'
        [IO.File]::WriteAllText($layout, $result, (New-Object System.Text.UTF8Encoding($false)))
        return $script:RemovedCount
    }
    return 0
}

# ---------- 4. 注入 BJ.EXF 的 NCMETA 行 ----------
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

# ---------- 5. 编译 EXF -> DEX ----------
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

# ---------- 6. 写 BJ 钩子 ----------
# 钩子先用 /ownerpid 探测发起后处理的 Cimatron 实例，再把 PID 传给命名进程。
# 多开时只有该实例会应答取数请求，避免取到另一个窗口的数据。
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

    # 去掉历史版本留下的 NcPostRename 行（含旧式 start "");
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

# ================= 主流程 =================
Section 'NcNamer 安装程序'
$backupRoot = New-BackupRoot
Say ("备份目录: " + $backupRoot) 'Yellow'

$installs = Find-Installations
if ($installs.Count -eq 0) { Say '未探测到任何 Cimatron 安装' 'Red'; exit 1 }

foreach ($inst in $installs) {
    if ($OnlyVersion -and ($inst.Version -notlike "*$OnlyVersion*")) { continue }
    Section ("版本 " + $inst.Version + "  (" + $inst.InstallDir + ")")
    Say ("  Program : " + $inst.ProgramDir) 'Gray'
    Say ("  Data    : " + $inst.DataDir) 'Gray'
    Say ("  Post    : " + ($inst.PostDirs -join ' | ')) 'Gray'

    if ($Uninstall) {
        $gone = Remove-LegacyIcons $inst
        if ($gone.Count -gt 0) { Say ("  已清除遗留图标文件 " + $gone.Count + " 个") 'Green' }
        $stray = Remove-StrayToolbarGroups $inst.DataDir $backupRoot
        if ($stray -gt 0) { Say ("  已清理堆积的工具栏按钮 " + $stray + " 个") 'Green' }
        if (Test-Path $inst.IniPath) {
            $null = Backup-File $inst.IniPath $backupRoot ('ini_' + $inst.Version)
            $kept = [IO.File]::ReadAllLines($inst.IniPath) | Where-Object { $_ -notmatch '^\s*NcNamer[^=]*=' }
            [IO.File]::WriteAllLines($inst.IniPath, $kept, (New-Object System.Text.UTF8Encoding($false)))
            Say '  已从 ExternalCommands.ini 移除注册' 'Green'
        }
        foreach ($pd in $inst.PostDirs) {
            $h1 = Join-Path $pd 'BJ.BAT'; $h2 = Join-Path $pd 'BJ.CMD'
            foreach ($h in @($h1, $h2)) {
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
        continue
    }

    # 6a. 插件
    if (-not $inst.HasPluginFx) {
        Say '  [跳过] 该版本缺少插件框架(CimUIInfrastructure)，即 Cimatron E11 及更早版本' 'Yellow'
        Say '         原因: E11 无插件接口可实时取数，其 GPP1 后处理也没有最小刀长变量，' 'Yellow'
        Say '               无法满足"刀长必须及时准确"的要求。按 fail-closed 原则保持现状不安装。' 'Yellow'
        Say '         可选出路: 将 BJ 后处理迁移到 GPP2(ex2) 后可用 XML_MIN_CLEAR_LEN 直出刀长，需现场开发。' 'Yellow'
        continue
    }

    $dllName = 'NcNamerAutoService.dll'
    $dllPath = Join-Path $inst.ProgramDir $dllName
    if (Test-Path $dllPath) { $null = Backup-File $dllPath $backupRoot ('dll_' + $inst.Version) }

    # 图标资源在编译时用 /win32icon 嵌入 DLL，无需额外文件；这里清掉早期版本留下的图标散件
    foreach ($legacy in @('NcNamer_icon64.png','NcNamer_icon32.png','NcNamer_icon16.png','NcNamer_icon64.ico')) {
        $p = Join-Path $inst.ProgramDir $legacy
        if (Test-Path $p) { try { Remove-Item $p -Force } catch { } }
    }

    $err = Build-Plugin $inst $dllPath
    if ($err) {
        Say ("  现场编译失败: " + $err) 'Yellow'
        $pre = Join-Path $PluginDir ('prebuilt_' + ($inst.Version -replace '\.0$', '') + '_NcNamerAutoService.dll')
        if (Test-Path $pre) {
            try {
                Copy-Item $pre $dllPath -Force
                Say ("  已改用预编译 DLL: " + (Split-Path $pre -Leaf)) 'Yellow'
            } catch {
                Say ("  预编译 DLL 也无法替换(DLL 被占用，请关闭 Cimatron 后重跑): " + $_.Exception.Message) 'Red'
                continue
            }
        } else {
            Say '  无可用预编译 DLL，插件安装失败' 'Red'
            continue
        }
    } else {
        Say ("  插件已编译: " + $dllPath) 'Green'
    }

    # 6a-2. 清掉早期版本留下的图标文件（不再使用自定义图标）
    $gone = Remove-LegacyIcons $inst
    if ($gone.Count -gt 0) { Say ("  已清除遗留图标文件 " + $gone.Count + " 个") 'Green' }

    # 6b. 注册
    if (Test-Path $inst.IniPath) { $null = Backup-File $inst.IniPath $backupRoot ('ini_' + $inst.Version) }
    $entry = 'NcNamerAutoService.NcDumpPlugin=NcNamerAutoService.NcDumpPlugin@' + $FlagSuffix
    $err = Register-Command $inst.IniPath $entry
    if ($err) { Say ("  注册失败: " + $err) 'Red' } else { Say ("  已注册: " + $entry) 'Green' }

    # 6b-2. 清掉历史版本堆积出来的匿名工具栏分组
    $stray = Remove-StrayToolbarGroups $inst.DataDir $backupRoot
    if ($stray -gt 0) { Say ("  已清理堆积的工具栏按钮 " + $stray + " 个") 'Green' }

    # 6c. 后处理目录
    foreach ($pd in $inst.PostDirs) {
        Say ("  -- post: " + $pd) 'Gray'
        $dst = Join-Path $pd 'NcPostRename.exe'
        if (Test-Path $dst) { $null = Backup-File $dst $backupRoot ('exe_' + $inst.Version) }
        Copy-Item (Join-Path $CommonDir 'NcPostRename.exe') $dst -Force
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

Section '安装结果'
Say '已处理版本:' 'Cyan'
foreach ($inst in $installs) { Say ('  - Cimatron ' + $inst.Version) }
Say ''
Say '生效条件: 重启 Cimatron（插件在启动时加载，无需手工点击）' 'Yellow'
Say '日志位置: %USERPROFILE%\NcNamer\auto_service_log.txt 与 post_rename_log.txt' 'Gray'
Say ("备份位置: " + $backupRoot) 'Gray'

$logPath = Join-Path (Join-Path $env:USERPROFILE 'NcNamer') 'install_report.txt'
New-Item -ItemType Directory -Force -Path (Split-Path $logPath) | Out-Null
[IO.File]::WriteAllLines($logPath, $script:Report, (New-Object System.Text.UTF8Encoding($true)))
