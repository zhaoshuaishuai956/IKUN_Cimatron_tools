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
| **顶针顶面拉平** | IKUN思美创工具箱 → 模具 → 顶针顶面拉平 | 非模态对话框内操作；框选整个顶针体可自动识别顶面与轴线方向，或手动选顶面并指定 ±X/±Y/±Z |

新增插件只需在 `plugins/` 下加一个带 `plugin.json` 的目录（见「开发新插件」）。

### 插件图标库

`assets/icons/ikun/` 存放 32 张待用的 IKUN 梗图插件图标（512 PNG / 64 PNG / 16–256 多尺寸 ICO），并附 `README.md` 编号清单和 `preview.png` 总览。

NC重命名 使用 **#59**（原 IKUN 图标）；顶针顶面拉平使用 **#64**（皮卡丘头套篮球），源文件在 `plugins/ejector-pin-flatten/payload/common/EjectorPin_icon64.ico`。后续新插件从图标库选一个 ICO，复制到 `plugins/<插件>/payload/common/<名称>_icon64.ico`，再把 `plugin.json` 的 `iconSource` 指向它即可。

- 图标库说明：安装包内 `assets/icons/ikun/README.md`（约 32 张待用图标）
- 图标来源：[wzwtt/ikun](https://github.com/wzwtt/ikun)

### 启动自动更新

启动时 NC 插件会异步请求 GitHub 最新 Release，与 `ToolboxUpdate.cs` 里的 `CurrentVersion` 比较；只有最新 Release **附带了对应大版本的原子更新资源**时才弹确认框。

- 资源名：`IKUN_Cimatron_Plugins_2024.zip`（2025/2026 对应改数字）。
- 确认后启动 `%USERPROFILE%\NcNamer\IKUNUpdater.exe`（UAC），关闭当前 Cimatron；更新器等待进程退出，下载资源包、校验 `manifest.txt` 版本、把 DLL/图标/更新器暂存到目标目录并备份，再用 `File.Replace` 原子替换；任一步失败全部回滚，成功后重启 Cimatron。
- 选「本次跳过」只在本次 Cimatron 进程内跳过，下次启动会重新检查。
- 更新器只替换插件 DLL、图标和自身，不改 `ExternalCommands.ini`、`CommandsLayout.xml`、后处理钩子，避免破坏 NC 挂载。
- 发布新版本：先把 `plugins/nc-rename/src/ToolboxUpdate.cs` 的 `CurrentVersion` 改成 Release 版本，运行安装，再运行 `tools\build_payload.ps1`，把 `dist\IKUN_Cimatron_Plugins_2024.zip` 上传到 tag 对应的 Release。资源包里的 `manifest.txt` 必须与 tag 版本一致，否则更新器会拒绝。
- 当前最新 Release 是 **v3.3.0**，已附带 `IKUN_Cimatron_Plugins_2024.zip`；旧版本启动时会检测并提示更新。发布新版本时把资源包上传到对应 Release，`manifest.txt` 版本必须与 tag 一致，否则更新器会拒绝。

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

> v3.3.0 Release 有两个 zip：首次安装下载 `IKUN_Cimatron_tools_Setup.zip`；`IKUN_Cimatron_Plugins_2024.zip` 是自动更新器用的增量资源，不需要手动下载。

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
      ── 顶针顶面拉平
      插件已编译: ...\Program\EjectorPinFlatten.dll
      已注册: EjectorPinFlatten.EjectorPinFlattenPlugin=...@1
      图标已安装: 50897_IKUNtools.ico
      ── NC重命名
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
2. 每次启动 Cimatron 都会弹出「IKUN工具箱已加载」提示（6 秒自动关闭，可点「知道了」立即关闭）。
3. 菜单路径：**IKUN思美创工具箱 → NC → NC重命名**；新插件的路径是 **IKUN思美创工具箱 → 模具 → 顶针顶面拉平**。
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

> NC重命名 的菜单在 NC / NcPart 和普通 Part 文档下都会显示；普通 Part 下只作为工具箱入口，实际命名仍由 BJ 后处理触发。

## 顶针顶面拉平 插件说明

### 使用步骤

#### 自动识别（框选整个顶针体）

1. 打开包含顶针的 **Part** 文档。
2. 把 Cimatron 的选择过滤器设为 **体**，框选所有顶针体；也可以只框选顶针顶面，插件会自动找到所属顶针体补全平面端。
3. 运行 **IKUN思美创工具箱 → 模具 → 顶针顶面拉平**。自动识别默认开启；打开命令时没选面也没关系，随后在对话框里框选顶针体或顶面，点「执行」即可。需要手动指定方向时再取消勾选。
4. 插件按每个顶针体的圆柱轴线识别顶面方向：
   - 一端是平面、另一端是组合面 / 非圆面 → 顶面方向就是组合面 / 非圆面那一端；
   - 两端都是平面 → 跳过，不做拉平；
   - 无法判断 → 列表中显示原因，可改用下面的手动模式。
5. 核对列表里的轴方向、顶面数和目标坐标，点「执行」。

> 自动识别按**世界 X/Y/Z 轴线**判断。顶针轴线倾斜时请取消自动识别，用手动模式配合当前 UCS 方向。

#### 手动模式

1. 关闭「自动识别顶针」。
2. 在模型窗口中框选顶针顶面（建议在顶视图框选，或按住 Ctrl 逐个点选；不要选侧壁）。选好后：
   - 点「重新读取」刷新识别结果和每根顶针的目标坐标；
   - 或直接点「执行」，插件会读取当前选择（若当前选择为空，会沿用上一次预览）。
3. 在方向下拉框选择向上方向（默认世界 +Z）：
   - 世界 +X / -X / +Y / -Y / +Z / -Z
   - 当前 UCS +X / -X / +Y / -Y / +Z / -Z
   
   如果所选方向与顶面不匹配（顶面在该方向上的厚度明显大于其宽度），插件会在列表中提示并阻止执行；换到顶针轴线方向，或勾选自动识别。
4. 核对识别到的顶针数量和每根顶针的目标坐标，点「执行」。

### 规则

- 每根顶针独立取**自己的最高点**，不取整个选择范围的全局最高点。
- 同一根顶针的顶面被拆成多个面时，按投影包围盒重叠自动合并为一组。
- 自动识别：从顶针体包围盒判断圆柱轴线（世界最长轴），找零厚度的平面端；平面端在另一端时，顶面方向指向剩下的组合面 / 非圆面端。两端都是平面则跳过。
- 方向按所选坐标系的 X / Y / Z 轴计算；选负向时取该方向上的最高点（即该轴坐标最小值）。
- 方向检查：若顶面组在所选方向上的厚度 > 顶面宽度 / 高度的 95%，判定方向与顶针轴线不一致，插件会拒绝替换并提示换方向。
- 替换面使用插件生成的拉平面作为目标；执行成功后辅助面会自动隐藏（名称形如 `IKUN_顶针拉平面_N`）；替换失败的尝试也会隐藏本次生成的辅助面。
- 只支持 B-rep 实体面；网格 / STL 面不支持替换面。
- 只支持 Part 文档（Assembly / NC 文档请先在 Part 中使用）。

### 注意事项

- 对话框不抢焦点（WS_EX_NOACTIVATE），在对话框按钮上点击不会丢失模型里的框选结果。
- 「识别顶针」数量与预期不符时，只框选顶针顶面后点「重新读取」；不要选中侧壁。
- 执行前会显示每根顶针的目标坐标，确认无误再执行。
- 替换面由 Cimatron 内核执行；单个顶针失败不会影响其他顶针，结果里会列出失败原因，原顶面保持不变。
- 执行过程写入 `%USERPROFILE%\NcNamer\pin_flatten_log.txt`。

## 目录结构

```
IKUN_Cimatron_tools/
├─ README.md
├─ 安装.cmd                     一键安装（自动提权）
├─ 卸载.cmd                     一键卸载
├─ install.ps1                  安装/卸载核心（自动扫描 plugins/*/plugin.json）
├─ plugins/
│  ├─ nc-rename/                NC重命名 插件
│  │  ├─ plugin.json
│  │  ├─ src/
│  │  │  ├─ NcNamerAutoService.cs    Cimatron 后台插件源码
│  │  │  └─ NcPostRenameAuto.cs      命名器源码
│  │  ├─ payload/
│  │  │  ├─ common/                  图标 + NcPostRename.exe
│  │  │  └─ plugins/                 prebuilt_<版本>_*.dll（现场编译失败时回退）
│  │  └─ doc/                        命名规则说明（html / png）
│  └─ ejector-pin-flatten/      顶针顶面拉平 插件
│     ├─ plugin.json
│     ├─ src/EjectorPinFlatten.cs
│     └─ payload/
│        ├─ common/EjectorPin_icon64.ico
│        └─ plugins/prebuilt_2024_EjectorPinFlatten.dll
└─ dist/
   └─ IKUN_Cimatron_tools_Setup.zip  最终安装包
```

## 开发新插件

以新增 `plugins/xxx/` 为例：

1. 按上面的目录结构建文件夹，并添加 `plugin.json` 清单：

   ```json
   {
     "folder": "xxx",
     "displayName": "插件显示名",
     "dll": "XxxPlugin.dll",
     "sources": ["src/XxxPlugin.cs"],
     "commandClass": "XxxPlugin.XxxPluginClass",
     "iconSource": "payload/common/Xxx_icon64.ico",
     "prebuiltPattern": "payload/plugins/prebuilt_{version}_XxxPlugin.dll",
     "iniPrefix": "XxxPlugin",
     "postProcess": ""
   }
   ```

   - `sources`：安装时按目标 Cimatron 版本现场编译的源码，可多个。
   - `commandClass`：`ExternalCommands.ini` 里注册的完整类型名，也用于计算命令 ID。
   - `iconSource`：命令图标源文件，安装时复制到 `Images\Commands\{命令ID}_IKUNtools.ico`。
   - `prebuiltPattern`：现场编译失败时的回退 DLL，`{version}` 会替换成 2024 / 2025 / 2026。
   - `iniPrefix`：安装/卸载时从 `ExternalCommands.ini` 清理的旧条目前缀。
   - `postProcess`：只有需要专属后处理的插件才填，目前 `nc-rename` 使用它部署 EXF / BJ 钩子。

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

4. 不需要改 `install.ps1`：它会自动扫描 `plugins/*/plugin.json`，按清单编译、
   注册、投放图标和生成预编译回退。NC重命名 的 EXF / 钩子等专属步骤由清单里的
   `postProcess: "nc-rename"` 触发，其他插件不填即可。

## 日志与排查

| 路径 | 内容 |
| --- | --- |
| `%USERPROFILE%\NcNamer\auto_service_log.txt` | NC重命名 插件启动、请求、XML 导出、图标投放 |
| `%USERPROFILE%\NcNamer\post_rename_log.txt` | 命名器每次运行日志（覆盖写） |
| `%USERPROFILE%\NcNamer\pin_flatten_log.txt` | 顶针顶面拉平：插件加载、选择/分组、目标高度、替换结果 |
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
6. 顶针顶面拉平：`pin_flatten_log.txt` 是否出现 `EjectorPinFlatten plugin loaded`；
   菜单项是否在 `ExternalCommands.ini` 的 `[Plugin Ext Commands]` 里。
   首次安装为 `@1`；Part 菜单写入 `CommandsLayout.xml` 后，安装器会按 Part 菜单自动注册 `@0`
   （`@0` 只表示不再重复插入菜单，插件仍会加载，菜单已在布局中）。

## 设计约束

- **fail-closed**：宁可保持原名，也不使用过期或不确定的刀长。
- **实时取数**：每次命名都向 Cimatron 重新请求 XML，不复用缓存。
- **原文件可恢复**：安装前自动备份，卸载只移除本工具写入的内容。
- **不改后处理逻辑**：仅注入一行元数据输出并追加一个启动钩子。

## 版本说明

- **v3.3（开发中）**：新增「顶针顶面拉平」插件：非模态对话框内选择；支持框选整个顶针体或只框选顶面自动识别顶针、顶面与轴线方向（仅选顶面时会自动关联所属实体，两端都是平面跳过）；方向支持世界 / 当前 UCS 的 ±X / ±Y / ±Z；方向与顶面不匹配时拒绝替换并提示；执行后隐藏辅助面，失败尝试的辅助面也会隐藏。NC重命名 的菜单在普通 Part 文档下也显示；两个插件使用各自独立的图标；启动提示只显示「IKUN工具箱已加载」；新增启动时 GitHub Release 自动更新（原子替换 DLL/图标/更新器）；安装脚本改为扫描 `plugins/*/plugin.json`，新增插件不再需要改脚本；NC重命名 的注册和后处理钩子保持不变。
- **v3.2**：修复安装时图标步骤报错（PowerShell `+` 在左操作数为数字时会做数值加法，`$id + '_x.ico'` 抛异常）；去掉 DLL 里多余的嵌入图标。
- **v3.1**：启动提示改为**每次打开 Cimatron 都弹出**（去掉「只弹一次」的标记文件），同一进程内不会重复弹。
- **v3.0**：重组为「IKUN思美创工具箱」，顶级菜单独立于 NC；命名插件更名为 **NC重命名**，归入 工具箱 → NC 分组；仓库改为 `plugins/<插件名>/` 结构，便于后续扩展；安装脚本写入 `ResetApiCommands =0` 确保菜单能正常插入。
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
| 版本 v3.3.0 | https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/tag/v3.3.0 |
| 直接下载 zip | https://github.com/zhaoshuaishuai956/IKUN_Cimatron_tools/releases/download/v3.3.0/IKUN_Cimatron_tools_Setup.zip |

下载后可在 PowerShell 中校验（SHA256 见 Releases 页面说明）：

```powershell
Get-FileHash .\IKUN_Cimatron_tools_Setup.zip -Algorithm SHA256
```

安装包以 Releases 页面为准；仓库内 `dist` 副本可能滞后于最新 Release。
