# IKUN思美创工具箱

Cimatron 二次开发插件集。所有插件统一挂在 Cimatron 的**顶级菜单「IKUN思美创工具箱」**下，按领域分组。

[![Release](https://img.shields.io/github/v/release/zhaoshuaishuai956/IKUN_Cimatron_tools)](https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/latest)

- 支持版本：**Cimatron 2024 / 2025 / 2026**
- 运行平台：Windows x64，.NET Framework 4.x，需要管理员权限安装
- 下载安装包：[Releases 页面](https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/latest)

## 插件列表

| 插件 | 菜单位置 | 说明 |
| --- | --- | --- |
| **NC重命名** | IKUN思美创工具箱 → NC → NC重命名 | BJ 后处理完成后自动把 NC 程序重命名为 `序号-刀名-刀长 nc.bj`，刀长取自 Cimatron 实时数据 |

新增插件只需在 `plugins/` 下加一个目录（见「开发新插件」）。

## 安装（从 Release 下载）

> 不需要注册 GitHub 账号，也不需要会 git。整个过程就是「下载一个压缩包 → 解压 → 双击运行」。

### 第 1 步：打开下载页面

1. 打开浏览器，访问：

   **https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/latest**

2. 页面会自动跳到最新版本。如果没有跳转，点页面顶部的 **Releases**，再点最新版本号。

3. 在 **Assets**（资源）区域点击安装包：

   ```
   IKUN_Cimatron_tools_Setup.zip
   ```

4. 下载完成后，文件通常在「下载」文件夹里。

> 浏览器提示「此类型的文件可能会损害您的计算机」时，选择 **保留 / 仍然保留** 即可。本工具源码完全公开可审查。

### 第 2 步：解压

1. 找到下载好的 zip。
2. **右键 → 全部解压缩**（Windows 11 可能显示「全部提取」）。
3. 选一个容易找到的位置，例如 `D:\IKUN_Cimatron_tools`，点「提取」。

> ⚠️ 必须先解压再运行。直接双击 zip 内的 `安装.cmd` 会失败。
>
> ⚠️ 不需要放进 Cimatron 安装目录，普通文件夹即可。

解压后应能看到：

```
安装.cmd
卸载.cmd
install.ps1
plugins\
README.md
```

### 第 3 步：运行安装

1. **关闭 Cimatron**（插件 DLL 被占用时无法替换）。
2. 在 `安装.cmd` 上 **右键 → 以管理员身份运行**。
3. 弹出「用户账户控制」时点 **是**。
4. 黑色窗口会逐条打印执行过程，大致如下：

   ```
   === IKUN思美创工具箱 安装程序 ===
   备份目录: C:\Users\<用户名>\NcNamer\backup_20261030_101500
   === 版本 2026.0  (D:\Program Files\Cimatron\Cimatron\2026.0) ===
      Program : D:\Program Files\Cimatron\Cimatron\2026.0\Program
      插件已编译: ...\Program\NcNamerAutoService.dll
      已注册: NcNamerAutoService.NcDumpPlugin=...@1
      图标已安装: 50468_IKUNtools.ico
      -- post: C:\ProgramData\Cimatron\Cimatron\2026.0\Data\It\var\post
         NcPostRename.exe 已部署
         钩子已写入: BJ.BAT
         BJ.EXF 已注入 NCMETA PROC
         bj.dex 已重新编译
   === 安装结果 ===
   生效条件: 重启 Cimatron
   ```

5. 脚本自动探测本机所有 2024 / 2025 / 2026 并逐个安装。
6. 窗口停在「请按任意键继续」，看完按键关闭。

> 提示 `现场编译失败` 会自动改用包内预编译 DLL，一般不影响使用。
>
> 提示 `DLL 被占用无法替换` 说明 Cimatron 还开着，关掉后重跑。

### 第 4 步：重启 Cimatron 确认

1. 重新打开 Cimatron。插件在启动时自动加载，**顶部菜单栏会多出「IKUN思美创工具箱」**。
2. 首次加载会弹出「IKUN思美创工具箱 已加载」提示（6 秒自动关闭）。
3. 菜单路径：**IKUN思美创工具箱 → NC → NC重命名**。
4. 正常做一次 BJ 后处理，输出目录应出现 `01-D10-72 nc.bj` 这类文件。

> 若文件名没变，看同目录下是否有 `NC_RENAME_FAILED.txt`，里面写明原因。

### 第 5 步：卸载（需要时）

1. 关闭 Cimatron。
2. 右键 **以管理员身份运行** `卸载.cmd`。
3. 脚本移除命令注册、菜单节点、图标与后处理钩子。安装前的备份保留在
   `%USERPROFILE%\NcNamer\backup_时间戳\`。

## NC重命名 插件说明

### 命名规则

| 字段 | 规则 |
| --- | --- |
| 序号 | 扫描输出目录已有文件，取最大序号 +1，两位显示；目标重名继续顺延 |
| 刀名 | XML「刀具_夹持」；`.` → `d`；`\ / : * ? " < > \|` 与控制字符 → `_`；最多 80 字符；为空则 `NO_TOOL` |
| 最小刀长 | XML「最小有效长度」向上取整；Cimatron 用 `-1` / `-2` / `0` 表示无最小伸出要求，记 `NA` |
| 后缀 | 空格 + `nc` + `.bj`（`03-D10-72 nc.bj`） |

设置环境变量 `NCNAMER_LEN_ZERO_AS_FAIL=1` 可切到严格模式：`-1/-2/0` 视为失败、保持原名。

命名成功后自动删除 `.origins`、`.tools`、`.cycles`、`.msg`、`print0.txt`、`print00.txt` 等后处理附属文件，并打开输出目录。

### 工作原理

```
BJ 后处理
   │
   ├─ BJ.EXF 在程序头输出 ( NCMETA PROC=编号 )
   └─ BJ.BAT / BJ.CMD 追加钩子
          │  先 /ownerpid 探测发起实例，再 start /min NcPostRename.exe /owner=<pid>
          ▼
   NcPostRename.exe（命名器）
          │  1. 在 TEMP_POST / 输出目录找新生成的 NC 文件
          │  2. 从 NCMETA 行取出程序编号
          │  3. 写 export_request_<令牌>_<编号>.txt（含 owner=<pid>）
          ▼
   NcNamerAutoService.dll（Cimatron 后台插件）
          │  FileSystemWatcher 监听请求，只回应 owner 匹配的实例
          │  IApplication → GetPdm().GetOpenDocuments() 遍历所有打开文档
          │  INcModel.GetItemAsXML(编号, xml)
          ▼
   NcPostRename.exe
          │  四重校验 → 解析 XML → 重命名 → 清理 → 打开目录
          ▼
   03-D10-72 nc.bj
```

四重校验全部通过才采用：GUID 令牌一致、程序编号一致、XML 写入时间不早于请求、SHA256 一致。

### 多个 Cimatron 同时打开

1. 命名器沿父进程链找到拉起它的 `CimatronE.exe`；钩子会先用
   `NcPostRename.exe /ownerpid` 同步探测，再以 `/owner=<pid>` 拉起命名进程。
   整条链写进 `post_rename_log.txt` 的 `ProcessChain=...`。
2. 该 PID 写入请求文件（`owner=<pid>`）。
3. 各实例比对 PID：只有一个匹配，其余静默不写响应。
4. 匹配的实例在自己的所有打开文档里查找该程序编号，切到别的文档也不会取错值。

> 实现注意：请求文件必须**直接创建**为最终文件名。若先写临时文件再改名，
> `FileSystemWatcher` 触发的是 `Renamed` 而非 `Created`，插件会收不到请求。

无法确定发起实例时退回**广播模式**；若多个实例返回不同 XML，命名器拒绝猜测，
保留原名并写 `NC_RENAME_FAILED.txt`。

## 目录结构

```
IKUN_Cimatron_tools/
├─ README.md
├─ 安装.cmd                     一键安装（自动提权）
├─ 卸载.cmd                     一键卸载
├─ install.ps1                  安装/卸载核心
├─ plugins/
│  └─ nc-rename/                NC重命名 插件
│     ├─ src/
│     │  ├─ NcNamerAutoService.cs    Cimatron 后台插件源码
│     │  └─ NcPostRenameAuto.cs      命名器源码
│     ├─ payload/
│     │  ├─ common/                  图标 + NcPostRename.exe
│     │  └─ plugins/                 prebuilt_<版本>_*.dll（现场编译失败时回退）
│     └─ doc/                        命名规则说明（html / png）
└─ dist/
   └─ IKUN_Cimatron_tools_Setup.zip  最终安装包
```

## 开发新插件

以新增 `plugins/xxx/` 为例：

1. 按上面的目录结构建文件夹：

   ```
   plugins\xxx\src\             插件 .cs（ICimApiCommandPlugin 实现）
   plugins\xxx\payload\common\  图标等资源
   plugins\xxx\payload\plugins\ prebuilt_<版本>_<名称>.dll
   ```

2. 插件里务必给 **固定的 `ToolbarName`**（留空会让 Cimatron 每次启动新建匿名分组，
   按钮越开越多）：

   ```csharp
   var cmd = new ApiCommand
   {
       Name = "你的类型名",                    // 决定命令 ID
       ToolbarName = "IKUN思美创工具箱",          // 与其他插件一致 → 合并到同一工具栏
       MenuPath = "IKUN思美创工具箱\n<分组>",     // 第1段顶级菜单，第2段分组
       Caption = "<命令名>",
       // ...
   };
   ```

   `MenuPath` 只取两段（源码按 `@` 切分）。顶级菜单不存在时 Cimatron 会新建，
   所以工具箱名写在第 1 段就会生成与 NC 并列的顶级菜单。

3. 图标：Cimatron **按命令 ID** 去 `DataDir\Images\Commands\{ID}_*.ico` 找图，
   找不到就用 `99999_Missing.ico`（黄底红叉）。命令 ID 算法：

   ```
   id = 49152 + (Σ(lower(key) 每字符 c: c-65+1，取非负项) % 4096)
   ```

   插件启动时把自带 ico 复制成 `{ID}_<名称>.ico` 即可；安装脚本也会做一次。
   注意 ICO 必须用传统 BMP 帧，PNG 压缩帧会让 `Icon.ToBitmap()` 抛异常。

4. 在 `install.ps1` 里补上该插件的注册/部署步骤（当前脚本按 `nc-rename` 写死，
   新增插件时复制对应函数块并改路径）。

## 日志与排查

| 路径 | 内容 |
| --- | --- |
| `%USERPROFILE%\NcNamer\auto_service_log.txt` | 插件启动、请求、XML 导出、图标投放 |
| `%USERPROFILE%\NcNamer\post_rename_log.txt` | 命名器每次运行日志（覆盖写） |
| `%USERPROFILE%\NcNamer\install_report.txt` | 最近一次安装报告 |
| `%USERPROFILE%\NcNamer\backup_时间戳\` | 安装前备份（ini / dll / exe / exf / 钩子 / 布局） |
| 输出目录 `NC_RENAME_FAILED.txt` | 命名失败原因，原 NC 文件保持不变 |

常见检查顺序：

1. 顶部菜单栏是否有「IKUN思美创工具箱」。没有 → 检查
   `ExternalCommands.ini` 的 `[Plugin Ext Commands]` 是否有该项且为 `@1`，
   且 `[Global Flags]` 里 `ResetApiCommands =0`（为 1 时 Cimatron 会把插件项改回
   `@0`，菜单不再插入）。
2. `auto_service_log.txt` 是否出现 `IKUN思美创工具箱 started`。
3. 菜单图标异常 → 检查 `Images\Commands\{命令ID}_IKUNtools.ico` 是否存在。
4. `post_rename_log.txt` 是否收到 `FreshXmlReady`；超时则确认 Cimatron 打开了对应文档。
5. 确认 `BJ.EXF` 含 `NCMETA PROC`，且 `bj.dex` 已重新生成。

## 设计约束

- **fail-closed**：宁可保持原名，也不使用过期或不确定的刀长。
- **实时取数**：每次命名都向 Cimatron 重新请求 XML，不复用缓存。
- **原文件可恢复**：安装前自动备份，卸载只移除本工具写入的内容。
- **不改后处理逻辑**：仅注入一行元数据输出并追加一个启动钩子。

## 版本说明

- **v3.0（当前）**：重组为「IKUN思美创工具箱」，顶级菜单独立于 NC；命名插件更名为 **NC重命名**，归入 工具箱 → NC 分组；仓库改为 `plugins/<插件名>/` 结构，便于后续扩展；安装脚本写入 `ResetApiCommands =0` 确保菜单能正常插入。
- **v2.4**：更名为 IKUN tools；自定义 IKUN 图标（7 种尺寸）；首次加载提示。
- **v2.3**：修复命令图标始终显示黄底红叉（改用 Cimatron 原生 `Images\Commands` 机制）。
- **v2.2**：修复每次启动工具栏按钮多一个（`ToolbarName` 留空导致匿名分组累积）。
- **v2.1.2**：修复请求文件被重命名导致插件收不到请求。
- **v2.1**：多实例隔离；`非空闲` 自动等待重试；新增 2026 预编译插件。
- **v2**：文件名末尾为 `空格 + nc + .bj`，无最小伸出要求时刀长记 `NA`。

## 下载链接

| 内容 | 地址 |
| --- | --- |
| 最新安装包 | https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/latest |
| 版本 v3.0 | https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/tag/v3.0 |
| 直接下载 zip | https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/download/v3.0/IKUN_Cimatron_tools_Setup.zip |

下载后可在 PowerShell 中校验（SHA256 见 Releases 页面说明）：

```powershell
Get-FileHash .\IKUN_Cimatron_tools_Setup.zip -Algorithm SHA256
```

仓库内也留有一份同内容副本：[`dist/IKUN_Cimatron_tools_Setup.zip`](dist/IKUN_Cimatron_tools_Setup.zip)。
