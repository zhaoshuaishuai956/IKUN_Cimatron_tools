# NcNamer · Cimatron NC 后处理自动命名插件

[![Release](https://img.shields.io/github/v/release/zhaoshuaishuai956/cimatron-nc-namer)](https://github.com/zhaoshuaishuai956/cimatron-nc-namer/releases/latest)

Cimatron BJ 后处理完成后，自动把 NC 程序重命名为：

```
序号-刀名-最小刀长 nc.bj
```

例如 `03-D10-72 nc.bj`、`04-BNED-07d5-7 nc.bj`、`06-QTECH12-NA nc.bj`。

刀长直接取自 Cimatron 当前程序的实时数据，经四重校验后才采用；任一环节不通过就保持原文件名，绝不写错名。全过程无弹窗、无需点击菜单。

- 支持版本：**Cimatron 2024 / 2025 / 2026**
- 运行平台：Windows x64，.NET Framework 4.x，需要管理员权限安装
- 命名规则图解：[`NC命名规则.png`](NC命名规则.png) / [`naming_rule.html`](naming_rule.html)
- 安装方法：见下方[「安装（从 Release 下载）」](#安装从-release-下载)，无需 GitHub 账号

## 安装（从 Release 下载）

> 不需要注册 GitHub 账号，也不需要会 git。整个过程就是「下载一个压缩包 → 解压 → 双击运行」。

### 第 1 步：打开下载页面

1. 打开浏览器，访问：

   **https://github.com/zhaoshuaishuai956/cimatron-nc-namer/releases/latest**

2. 页面会自动跳到最新版本（当前是 `v2.0.0`）。如果没有跳转，点击页面顶部的 **Releases** 文字，再点版本号 `v2.0.0`。

3. 页面往下找一个叫 **Assets**（资源）的小区域，里面有一行：

   ```
   NcNamer_Setup_v2_nc.bj.zip      562 KB
   ```

4. 点这一行，浏览器就开始下载。下载完成后，文件通常在「下载」文件夹里。

> 如果浏览器提示「此类型的文件可能会损害您的计算机」或「不常下载」，选择 **保留 / 仍然保留** 即可。这是浏览器的常规提醒，本工具不含病毒，源码全部公开在仓库里可供审查。
>
> **打不开 Releases 页面时**：在仓库首页点绿色的 **Code** 按钮 → **Download ZIP**，下载的是完整源码包，解压后同样可以运行 `安装.cmd`。

### 第 2 步：解压

1. 找到下载好的 `NcNamer_Setup_v2_nc.bj.zip`。
2. **右键 → 全部解压缩**（Windows 11 可能显示为「全部提取」）。
3. 选择一个容易找到的位置，例如 `D:\NcNamer_Setup`，然后点「提取」。

> ⚠️ 一定要先解压再运行。直接双击 zip 里的 `安装.cmd` 会失败，因为压缩包内无法写入文件。
>
> ⚠️ 不要把文件放在 Cimatron 安装目录里，也不需要放到 Program Files。普通文件夹即可。

解压后打开文件夹，应该能看到：

```
安装.cmd
卸载.cmd
install.ps1
payload\
NC命名规则.png
naming_rule.html
README.md
```

### 第 3 步：运行安装

1. **关闭 Cimatron**（若正在运行）。插件 DLL 被占用时无法替换。
2. 在 `安装.cmd` 上 **右键 → 以管理员身份运行**。
3. 如果弹出「用户账户控制」窗口询问是否允许更改，点 **是**。
4. 会出现一个黑色的命令行窗口，自动逐条打印执行过程，大致如下：

   ```
   === NcNamer 安装程序 ===
   备份目录: C:\Users\<你的用户名>\NcNamer\backup_20261030_101500
   === 版本 2026.0  (C:\Program Files\Cimatron\Cimatron\2026.0) ===
      Program : C:\Program Files\Cimatron\Cimatron\2026.0\Program
      插件已编译: ...\Program\NcNamerAutoService.dll
      已注册: NcNamerAutoService.NcDumpPlugin=...@1
      -- post: C:\ProgramData\Cimatron\Cimatron\2026.0\Data\It\var\post
         NcPostRename.exe 已部署
         钩子已写入: BJ.BAT
         BJ.EXF 已注入 NCMETA PROC
         bj.dex 已重新编译
   === 安装结果 ===
   生效条件: 重启 Cimatron（插件在启动时加载，无需手工点击）
   ```

5. 脚本会自动找出本机所有 2024 / 2025 / 2026 的 Cimatron，**逐个版本安装**，无需手工选择。
6. 窗口最后会停在「请按任意键继续」，看完后按键关闭即可。

> 如果安装过程中提示 `现场编译失败`，脚本会自动改用安装包内自带的预编译 DLL，一般不影响使用。
>
> 如果提示 `DLL 被占用无法替换`，说明 Cimatron 还开着，关掉它再重新运行一次 `安装.cmd`。
>
> 如果双击后弹出蓝色的「Windows 已保护你的电脑」窗口：点 **更多信息** → **仍要运行**。这是因为脚本没有商业数字签名，属于正常提示。
>
> 如果窗口一闪就消失：不要直接双击，务必使用 **右键 → 以管理员身份运行**；也可以先打开 PowerShell 进入该文件夹，执行 `powershell -ExecutionPolicy Bypass -File .\install.ps1`。

### 第 4 步：重启 Cimatron 并确认生效

1. 重新打开 Cimatron。插件在启动时自动加载，**不需要点任何菜单**。
2. 检查日志文件，确认插件已启动：

   ```
   %USERPROFILE%\NcNamer\auto_service_log.txt
   ```

   打开后第一行附近应出现 `NcNamer auto service started`。

3. 正常做一次 BJ 后处理，输出目录里应该出现形如 `01-D10-72 nc.bj` 的文件，同时自动弹出输出目录窗口。

> 如果文件名没有变化，检查同目录下是否生成了 `NC_RENAME_FAILED.txt`，里面会写明失败原因。详见下文「日志与排查」。

### 第 5 步：卸载（需要时）

1. 关闭 Cimatron。
2. 在解压出来的 `卸载.cmd` 上 **右键 → 以管理员身份运行**。
3. 脚本会移除注册项与后处理钩子。安装前备份的原始文件保留在：

   ```
   %USERPROFILE%\NcNamer\backup_时间戳\
   ```

> 卸载只移除本插件写入的内容，不会破坏 Cimatron 自身的后处理文件。

### 安装参数（进阶，可选）

一般用户用不到。需要精细控制时，在解压后的目录里打开 PowerShell，调用 `install.ps1`：

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
- 校验发起方 PID：请求文件里记录了发起实例的 PID，只有被指定的实例才继续处理。
- 收到请求后切回 UI 线程，先看活动文档，再用 `IApplication.GetPdm().GetOpenDocuments()`
  遍历本实例所有打开的文档，逐个尝试
  `IModelContainer → INcModel.GetItemAsXML(procUid, xmlPath)` 导出该程序的 XML。
- 导出失败时区分两类错误：`非空闲模式`（等待重试，最多约 60 秒）、其余（直接失败），
  重试通过定时器排队，不阻塞 Cimatron 界面。
- 成功后返回 `OKXML|路径|程序号|SHA256`（Base64 编码），失败返回 `ERR|原因`。
- 令牌去重，避免重复请求造成多次导出。

### 多个 Cimatron 同时打开

同时开着两个及以上 Cimatron 实例时，每个实例都会加载本插件。为避免"另一个实例抢答、
返回错误的刀长"，流程按实例隔离：

1. 命名器从 `NcPostRename.exe` 沿父进程链向上，找到拉起它的 `CimatronE.exe`。
   整条链会写进 `post_rename_log.txt` 的 `ProcessChain=...` 一行，便于排查。
2. 该实例的 PID 写入请求文件（`owner=<pid>`）。
3. 各实例收到请求后比对 PID：只有一个匹配，其余实例保持静默，不再写响应文件。
4. 匹配的实例在自己的所有打开文档里查找该程序编号，因此后处理时切到别的文档也不会取错值。

无法确定发起实例时（例如手工双击运行 `NcPostRename.exe`，或后处理经由非子进程执行），
会退回**广播模式**：所有实例都可应答；取不到数据的实例会延迟 8 秒才写失败结果，
且不会覆盖已经写入的成功结果。日志里会记录 `Ambiguous: N Cimatron instances running; using broadcast mode`。

### 3. 命名器 `payload/src/NcPostRenameAuto.cs`

- 运行目录为 Cimatron 的 `TEMP_POST` 时，自动改为其上一级输出目录。
- 沿父进程链定位发起本次后处理的 Cimatron 实例，把其 PID 写入请求文件（见下节）。
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
│  │  ├─ prebuilt_2025_NcNamerAutoService.dll
│  │  └─ prebuilt_2026_NcNamerAutoService.dll
│  └─ src/
│     ├─ NcNamerAutoService.cs  后台插件源码
│     └─ NcPostRenameAuto.cs    命名器源码
└─ dist/
   └─ NcNamer_Setup_v2.1_nc.bj.zip   最终多版本一键安装包
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

- **v2.1.1（当前）**：修正请求文件的写入竞态——先写临时文件再原子改名，避免插件读到空文件而误判为广播请求。
- **v2.1**：多实例支持——只由实际执行后处理的 Cimatron 实例应答，并在该实例的所有打开文档里查找程序；`非空闲` 自动等待重试；新增 2026 预编译插件。
- **v2**：文件名末尾为 `空格 + nc + .bj`（`01-D10-72 nc.bj`）；无最小伸出要求时刀长段记 `NA`。
- **v1**：文件名末尾为 `空格 + nc`（无扩展名），已不再发布。
- 自定义命令图标方案已放弃，命令沿用 Cimatron 默认图标。

## 下载链接

| 内容 | 地址 |
| --- | --- |
| 最新安装包 | https://github.com/zhaoshuaishuai956/cimatron-nc-namer/releases/latest |
| 版本 v2.1.1 | https://github.com/zhaoshuaishuai956/cimatron-nc-namer/releases/tag/v2.1.1 |
| 直接下载 zip | https://github.com/zhaoshuaishuai956/cimatron-nc-namer/releases/download/v2.1.1/NcNamer_Setup_v2.1_nc.bj.zip |

下载后可在 PowerShell 中校验（SHA256 值见 Releases 页面说明）：

```powershell
Get-FileHash .\NcNamer_Setup_v2.1_nc.bj.zip -Algorithm SHA256
```

仓库内也留有一份同内容的副本：[`dist/NcNamer_Setup_v2.1_nc.bj.zip`](dist/NcNamer_Setup_v2.1_nc.bj.zip)，解压后即为安装器目录。
