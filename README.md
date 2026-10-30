# NcNamer · Cimatron NC 后处理自动命名插件

Cimatron BJ 后处理完成后，自动把 NC 程序重命名为：

```
序号-刀名-最小刀长 nc.bj
```

例如 `03-D10-72 nc.bj`、`04-BNED-07d5-7 nc.bj`、`06-QTECH12-NA nc.bj`。

刀长直接取自 Cimatron 当前程序的实时数据，经四重校验后才采用；任一环节不通过就保持原文件名，绝不写错名。全过程无弹窗、无需点击菜单。

- 支持版本：**Cimatron 2024 / 2025 / 2026**
- 运行平台：Windows x64，.NET Framework 4.x，需要管理员权限安装
- 命名规则图解：[`NC命名规则.png`](NC命名规则.png) / [`naming_rule.html`](naming_rule.html)
- 最终安装包：[`dist/NcNamer_Setup_v2_nc.bj.zip`](dist/NcNamer_Setup_v2_nc.bj.zip)

## 一键安装

1. 把整个仓库（或解压后的安装包）拷贝到目标电脑，U 盘、网络共享均可。
2. 右键以管理员身份运行 `安装.cmd`，脚本会自动请求提权。
3. 脚本自动探测本机所有受支持的 Cimatron 安装并逐个部署。
4. 安装完成后**重启 Cimatron**，插件在启动时自动加载。

卸载：以管理员身份运行 `卸载.cmd`。卸载会移除注册项与后处理钩子，原始文件均备份在 `%USERPROFILE%\NcNamer\backup_时间戳`。

### 安装参数

可直接在 PowerShell 中调用 `install.ps1`：

| 参数 | 作用 |
| --- | --- |
| `-OnlyVersion 2025` | 只处理指定版本 |
| `-AutoLoadOff` | 注册项使用 `@0`（默认 `@1`，随 Cimatron 自动加载） |
| `-Uninstall` | 执行卸载 |

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -OnlyVersion 2026
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Uninstall
```

## 版本支持

| 版本 | 自动命名 | 实现方式 |
| --- | --- | --- |
| Cimatron 2024 / 2025 / 2026 | 支持 | 后台插件实时导出 XML + `NcPostRename.exe` |
| Cimatron E11 及更早 | 不支持 | 无插件框架，GPP1 后处理也没有最小刀长变量 |

E11 按 fail-closed 原则跳过安装，不会写入不准确的刀长。可选出路是把 BJ 后处理迁移到 GPP2（ex2），用 `XML_MIN_CLEAR_LEN` 直出刀长。命名器已预留 `NCMETA2` 直出解析路径，迁移后无需改动。

## 工作原理

```
BJ 后处理
   │
   ├─ BJ.EXF 在程序头输出 ( NCMETA PROC=编号 )
   │
   └─ BJ.BAT / BJ.CMD 追加钩子
          │  start "" /min NcPostRename.exe
          ▼
   NcPostRename.exe（命名器）
          │  1. 在 TEMP_POST / 输出目录找新生成的 NC 文件
          │  2. 从 NCMETA 行取出程序编号
          │  3. 写入 export_request_<令牌>_<编号>.txt
          ▼
   NcNamerAutoService.dll（Cimatron 后台插件）
          │  FileSystemWatcher 监听请求
          │  Cimatron API: INcModel.GetItemAsXML(编号, xml)
          │  写 export_response_<令牌>.txt（路径 / 编号 / SHA256）
          ▼
   NcPostRename.exe
          │  四重校验 → 解析 XML → 重命名 → 清理 → 打开目录
          ▼
   03-D10-72 nc.bj
```

### 1. 安装器 `install.ps1`

- 扫描 `C:\Program Files\Cimatron\Cimatron\<版本>\Program`，识别 2024 / 2025 / 2026。
- 定位配置目录与后处理目录：
  - `C:\ProgramData\Cimatron\Cimatron\<版本>\Data\It\var\post`
  - `...\Data\Nc\MachineWorks\post`
  - `<安装目录>\SourceData\It\var\post`
- 编译插件：使用目标机器自身的 Cimatron DLL 作为引用，调用 .NET Framework 的 `csc.exe` 现场编译；失败时回退到 `payload/plugins` 下的预编译 DLL。
- 注册命令：向 `ExternalCommands.ini` 的 `[Plugin Ext Commands]` 写入
  `NcNamerAutoService.NcDumpPlugin=NcNamerAutoService.NcDumpPlugin@1`（自动去重、保留备份）。
- 部署命名器：把 `payload/common/NcPostRename.exe` 复制到每个后处理目录。
- 写入钩子：在 `BJ.BAT` / `BJ.CMD` 末尾追加 `start "" /min "%~dp0NcPostRename.exe"`，原内容保留。
- 注入元数据：若 `BJ.EXF` 中没有 `NCMETA PROC`，在 `BEGINNING OF PROC:` 之后插入
  `OUTPUT $ "( NCMETA PROC=" PROC_NUM " )" ;`，再用该版本的 `dfexf.exe` 重新编译 `bj.dex`。
- 安装/卸载时清理早期实验版本遗留的 `NcNamer_icon*` 文件。

### 2. 后台插件 `payload/src/NcNamerAutoService.cs`

- 实现 `ICimApiCommandPlugin`，随 Cimatron 启动加载。
- 启动即拉起 `NcAutoExportService`，用 `FileSystemWatcher` 监听 `%USERPROFILE%\NcNamer\export_request_*.txt`。
- 收到请求后切回 UI 线程，通过
  `CimApplicationProvider → IApplication → IModelContainer → INcModel.GetItemAsXML(procUid, xmlPath)`
  导出该程序的 XML。
- 同一个请求最多重试 8 次；成功后返回 `OKXML|路径|程序号|SHA256`（Base64 编码），失败返回 `ERR|原因`。
- 令牌去重，避免重复请求造成多次导出。

### 3. 命名器 `payload/src/NcPostRenameAuto.cs`

- 运行目录为 Cimatron 的 `TEMP_POST` 时，自动改为其上一级输出目录。
- 筛选新生成的 NC 候选文件（排除 `.nc` / ` nc` / ` nc.bj` / `.nc.bj` 及后处理附属文件）。
- 优先尝试 **GPP 直出**：从文件内容解析 `NCMETA2 TOOL=... LEN=...`，适用于迁移到 GPP2 的后处理。
- 否则解析 `NCMETA PROC=编号`，向插件请求实时 XML。
- 四重校验全部通过才采用：
  1. GUID 令牌一致（响应文件与请求一一对应）
  2. 响应中的程序编号与 NC 文件中的 `PROC` 完全一致
  3. XML 写入时间不早于本次请求发起时间（允许 2 秒时钟误差）
  4. XML 的 SHA256 与插件记录值一致
- 解析字段（按字段 ID 优先，名称候选兜底，兼容中英文版）：
  - 程序状态：ID `30009`，必须是「计算完成 / Calculated / Done」
  - 刀具_夹持：ID `3001`
  - 最小有效长度：ID `30010`，向上取整
- 失败保护：任一步失败都保留原 NC 文件，并在输出目录生成 `NC_RENAME_FAILED.txt` 说明原因。等待上限 180 秒。

## 命名规则

| 字段 | 规则 |
| --- | --- |
| 序号 | 扫描输出目录已有文件，取最大序号 +1，两位显示；目标重名则继续顺延 |
| 刀名 | XML「刀具_夹持」；`.` 替换为 `d`；`\ / : * ? " < > |` 与控制字符替换为 `_`；连续空白压缩；最多 80 字符；为空则 `NO_TOOL` |
| 最小刀长 | XML「最小有效长度」向上取整；Cimatron 用 `-1` / `-2` / `0` 表示无最小伸出要求，此时记 `NA` |
| 后缀 | 空格 + `nc` + `.bj` 扩展名（v2） |

严格模式下（设置环境变量 `NCNAMER_LEN_ZERO_AS_FAIL=1`），`-1` / `-2` / `0` 将视为失败并保持原文件名。

命名成功后自动删除 `.origins`、`.tools`、`.cycles`、`.msg`、`print0.txt`、`print00.txt` 等后处理附属文件，并打开输出目录。

## 目录结构

```
NcNamerRepo/
├─ README.md
├─ 安装.cmd                     一键安装（自动提权）
├─ 卸载.cmd                     一键卸载
├─ install.ps1                  安装 / 卸载核心逻辑
├─ naming_rule.html             命名规则说明（网页版）
├─ NC命名规则.png               命名规则说明（图片版）
├─ payload/
│  ├─ common/
│  │  └─ NcPostRename.exe       命名器（v2，输出 .bj）
│  ├─ plugins/
│  │  ├─ prebuilt_2024_NcNamerAutoService.dll
│  │  └─ prebuilt_2025_NcNamerAutoService.dll
│  └─ src/
│     ├─ NcNamerAutoService.cs  后台插件源码
│     └─ NcPostRenameAuto.cs    命名器源码
└─ dist/
   └─ NcNamer_Setup_v2_nc.bj.zip   最终多版本一键安装包
```

## 从源码构建

环境：Windows x64 + .NET Framework 4.x（`csc.exe` 位于 `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`）。

### 命名器 EXE

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:exe /platform:x64 `
  /out:payload\common\NcPostRename.exe `
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
  payload\src\NcPostRenameAuto.cs
```

### 后台插件 DLL

插件依赖目标机器上的 Cimatron 程序集，建议直接用 `install.ps1` 在现场编译（它会自动探测引用路径并回退预编译 DLL）。手动编译示例：

```powershell
$program = 'C:\Program Files\Cimatron\Cimatron\2026.0\Program'
$refs = @(
  'CimUIInfrastructure.dll', 'CimWpfContracts.dll',
  'interop.CimNcAPI.dll', 'interop.CimatronE.dll',
  'interop.CimServicesAPI.dll', 'interop.CimAppAccess.dll', 'interop.CimBaseAPI.dll'
) | ForEach-Object { '/r:' + (Join-Path $program $_) }

& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /nologo /target:library /platform:x64 `
  /out:payload\plugins\prebuilt_2026_NcNamerAutoService.dll `
  $refs /r:System.dll /r:System.Core.dll /r:System.Xml.dll `
  payload\src\NcNamerAutoService.cs
```

## 日志与排查

| 路径 | 内容 |
| --- | --- |
| `%USERPROFILE%\NcNamer\auto_service_log.txt` | 插件启动、请求、XML 导出结果 |
| `%USERPROFILE%\NcNamer\post_rename_log.txt` | 命名器每次运行的详细日志（覆盖写） |
| `%USERPROFILE%\NcNamer\install_report.txt` | 最近一次安装报告 |
| `%USERPROFILE%\NcNamer\backup_时间戳\` | 安装前备份（ini / dll / exe / exf / 原钩子） |
| 输出目录 `NC_RENAME_FAILED.txt` | 命名失败原因，原 NC 文件保持不变 |

常见检查顺序：

1. 重启 Cimatron 后，`auto_service_log.txt` 是否出现 `NcNamer auto service started`。
2. `post_rename_log.txt` 是否收到 `FreshXmlReady`；若超时，检查 Cimatron 是否打开了对应文档。
3. 确认 `BJ.EXF` 已包含 `NCMETA PROC`，且 `bj.dex` 已重新生成。
4. 确认后处理目录下 `NcPostRename.exe` 存在，`BJ.BAT` / `BJ.CMD` 末尾有启动钩子。

## 设计约束

- **fail-closed**：宁可保持原名，也不使用过期或不确定的刀长。
- **实时取数**：每次命名都向 Cimatron 重新请求 XML，不复用缓存。
- **原文件可恢复**：安装前自动备份，卸载只移除本插件写入的内容。
- **不改后处理逻辑**：仅注入一行元数据输出并追加一个启动钩子。

## 版本说明

- **v2（当前）**：文件名末尾为 `空格 + nc + .bj`（`01-D10-72 nc.bj`）；无最小伸出要求时刀长段记 `NA`。
- **v1**：文件名末尾为 `空格 + nc`（无扩展名），已不再发布。
- 自定义命令图标方案已放弃，命令沿用 Cimatron 默认图标。

## 最终安装包

`dist/NcNamer_Setup_v2_nc.bj.zip`

- SHA256：`12D7BD8F3FDD70F10E9DF6D1CBF09F9ACC38EAA833BF3600066057604B207197`
- 解压后与仓库根目录的安装器内容一致，可直接拷贝到目标电脑运行 `安装.cmd`。
