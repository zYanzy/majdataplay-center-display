# 部署指南：安装中心显示项 —— 倒计时

> 本文件供 AI Agent 阅读执行。请严格按顺序操作，**任何一步校验失败就停止并向用户报告**，不要猜测、不要跳过、不要强行写入程序集。
>
> 本仓库是 **MajdataPlay 中心显示扩展**：通过追加 `BGInfoOption` 枚举值，给 HUD 的三个中心信息显示位添加自定义显示项（概念与三个显示位见 `README.md` 的「什么是中心显示」）。本指南**只装其中的倒计时显示项**（`BGInfoOption.Countdown = 17`），不会改动 Fault 计数器或传感器高亮颜色。三个补丁彼此独立，可只装任意一个、也可按任意顺序全部安装。
>
> 若还要装 Fault 计数器，见同仓库的 `INSTALL.md`；传感器配色工具见 `majdataplay-sensor-color` 仓库的 INSTALL.md。

## 1. 适用范围（硬性前提）

| 项目 | 要求 |
| --- | --- |
| 游戏 | MajdataPlay **Windows 运行包 2.0.3 `20260917-637bba0`，Mono 后端** |
| 目标文件 | `<GAME_DIR>\MajdataPlay_Data\Managed\Assembly-CSharp.dll` |
| 官方原版指纹 | SHA256 `139817f1e0147f8fe21fdc6c0a24303f3a93c98a7fd049c4eebf54a09c875ad9`，大小 `1,241,600` 字节 |
| 版本标识 | `<GAME_DIR>\MajdataPlay_Data\app.info` |

**版本不匹配必须停止。** IL 锚点（`BGInfoOption` 枚举、`ObjectCounter._gpManager` / `UpdateInfoDisplayerOutput` / `SetInfoDisplayerInitText` / `SetInfoDisplayerActive`、`GamePlayManager.get_AudioLength` / `get_ThisFrameSec`、`TMP_Text::set_text`）绑定这一版程序集；其他版本（含 dev 分支 2.1.0）结构不同，强行注入会损坏程序集。**不要**拿 2.1.0 源码编译出的 DLL 覆盖 2.0.3 运行包。

指纹不同**不一定**是版本不对：DLL 可能已被 Fault / 配色 / 本补丁改过。此时以第 5 节 `--inspect` 的结果为准，能正常输出 JSON 就说明锚点兼容。

## 2. 依赖

- Windows
- .NET 10 **SDK**（clone 路线需要 `dotnet build`）；若拿到已含 `countdown-engine/` 编译产物的离线目录，只需 .NET 10 **运行时**
- Python 3（**仅标准库，不需要 tkinter**），用于一键脚本；也可完全手工调用引擎
- git（可选）

## 3. 获取代码

**方式 A — 公开仓库（推荐）**

```bash
git clone https://github.com/zYanzy/majdataplay-center-display.git
```

同一个仓库里含**两个独立引擎**：`engine-src/`（Fault）与 `countdown-src/`（倒计时）。仓库**不含** `engine/` 与 `countdown-engine/`（编译产物被 `.gitignore` 排除），必须执行第 4 步构建。

**方式 B — 离线拷贝**

让用户把整个仓库目录拷过来（含 `countdown-engine/patcher.dll` 与 Mono.Cecil 的 DLL），可跳过第 4 步。

下文用 `<TOOL_DIR>` 表示该目录。

## 4. 构建倒计时引擎（方式 A 必做）

```bat
dotnet build "<TOOL_DIR>\countdown-src\patcher.csproj" -c Release -o "<TOOL_DIR>\countdown-engine"
```

依赖 NuGet 包 `Mono.Cecil 0.11.6`。若因无 SDK / 无网络构建失败，改用方式 B 获取已编译的 `countdown-engine/`，**不要**自行改写 csproj 的目标框架，也不要编译游戏根目录的 `.sln`。

> 注意：本引擎与 Fault 引擎的名字都叫 `patcher.dll`，但**输出到不同目录**（`countdown-engine/` vs `engine/`），互不影响。不要把它们混用。

## 5. 兼容性校验（写入前必做）

1. 找到含 `MajdataPlay.exe` 的目录，记为 `<GAME_DIR>`。**向用户确认，不要猜**。
2. 确认游戏已完全退出；DLL 被占用时不能替换。
3. 执行：

```bat
set "DLL=<GAME_DIR>\MajdataPlay_Data\Managed\Assembly-CSharp.dll"
dotnet "<TOOL_DIR>\countdown-engine\patcher.dll" "%DLL%" --inspect
```

期望输出**一行 JSON**：

- 未安装过：`{"hasCountdown":false}`
- 已安装过：`{"hasCountdown":true}`

**只有成功输出 JSON 才能继续**。抛异常（找不到类型/方法/字段）、返回码非 0、输出不是 JSON → 版本不兼容，立即停止并报告。

> 三份 `--inspect` 的格式各不相同：本引擎 `{"hasCountdown":..}`，Fault 引擎 `{"hasFault":..}`，配色引擎 `{"r","g","b","a"}`。不要用错。

## 6. 生成并写入补丁

**推荐：一键脚本**（内含校验、备份、替换后复检，任何一步失败都不会写入运行包）：

```bat
py -3 "<TOOL_DIR>\apply_countdown.py" "<GAME_DIR>"
```

预期输出依次包含 `source inspect`、`backup`、`generate`（`OK hasCountdown=injected` 或 `existing`）、`staged inspect`、`final inspect`、末尾 `DONE`。脚本会：

- 备份当前 DLL 到 `<TOOL_DIR>\backups\Assembly-CSharp_<时间戳>.dll`
- 生成到 `<TOOL_DIR>\output\`，二次 `--inspect` 确认 `hasCountdown=true`
- **只替换** `%DLL%` 这一个文件，再对运行包做最终 `--inspect`
- 检测到 `MajdataPlay.exe` 正在运行会直接拒绝

**或手工执行**（效果相同，请自行完成同等校验）：

```bat
dotnet "<TOOL_DIR>\countdown-engine\patcher.dll" "%DLL%" "<TOOL_DIR>\output\Assembly-CSharp.pending.dll"
dotnet "<TOOL_DIR>\countdown-engine\patcher.dll" "<TOOL_DIR>\output\Assembly-CSharp.pending.dll" --inspect
copy /Y "<TOOL_DIR>\output\Assembly-CSharp.pending.dll" "%DLL%"
dotnet "<TOOL_DIR>\countdown-engine\patcher.dll" "%DLL%" --inspect
```

最后一行必须输出 `{"hasCountdown":true}`。Git Bash 下把 `copy /Y` 换成 `cp -f`。

## 7. 把倒计时选到中心显示位（改 `settings.json`，改前先备份）

三个中心显示位 `Game.BGInfo` / `Game.SecondaryBGInfo` / `Game.SubScreenBGInfo` 可任选其一，值用字符串枚举名。只改这几个键，其余内容一律保留：

| 路径 | 值 | 说明 |
| --- | --- | --- |
| `Game.BGInfo` 或 `Game.SecondaryBGInfo` | `"Countdown"` | 选一个显示位。若用户已用 `BGInfo` 显示 Fault，就放 `SecondaryBGInfo`（或反之） |
| `Game.EnableJudgeTimingGauge` | `false` | **若放在 `SecondaryBGInfo`，此项必须为 false**，否则初始化时副显示位被强制改为 `None`，设置里选中也不显示 |

`SubScreenBGInfo` 也可设为 `"Countdown"`（副屏不受判定计时条影响）。设置项名在中文界面可能显示英文 `Countdown`（语言包打包在 `resources.assets`，本补丁不改），HUD 标题硬编码 `TIME LEFT`，功能不受影响。

显示格式为 **M:SS**（如 `1:23`、`0:05`），随歌曲播放递减，归零后保持 `0:00`。格式为 `TimeSpan.ToString("m\\:ss")`，超过 60 分钟时分钟部分会回绕（音游谱面不会达到该长度）。

## 8. 验收

1. 手动双击 `MajdataPlay.exe` 启动游戏（**不要**由 Agent 的命令会话启动，进程常被回收且容易双开抢串口/音频），进入任意谱面。
2. 所选中心显示位显示标题 `TIME LEFT`，下方为剩余时间 `M:SS`，随播放递减，开局 Lead-In 阶段不小于 0。
3. 游戏日志中无 `InvalidProgramException` / `BadImageFormatException` / 程序集加载失败。
4. `MaiCharts/`、`Skins/`、`MajScores.db` 未被改动。
5. 若此前已装 Fault 计数器，把另一个显示位设为 `"Fault"` 应仍正常显示负数 —— 两个 prologue 是链式的，互不遮挡。
6. 若此前已装传感器配色补丁，高亮色应保持不变。

## 9. 禁止事项

- **只替换 `Assembly-CSharp.dll`**，绝不整目录覆盖；不要动 `MaiCharts/`、`Skins/`、`MajScores.db`、`Logs/` 及 `settings.json` 的其他键。
- 不要用 `dotnet build` / VS / MSBuild 编译游戏根目录的 `.sln` / `.csproj`（那些只给 IDE 提示用）。
- 不要在游戏运行时替换 DLL。
- 不要把任何游戏二进制、谱面、成绩库、个人设置上传或提交到版本库。
- **MajdataHub 官方增量更新会覆盖 `Assembly-CSharp.dll` 导致补丁丢失**；更新后需**分别**重跑每个已装补丁的应用脚本。
- **不要为了"恢复"用官方原版 DLL 整体覆盖运行包** —— 那会同时清掉 Fault、配色和本补丁。三个引擎都是叠加式的。

## 10. 回滚

用第 6 步生成的 `backups\Assembly-CSharp_<时间戳>.dll` 覆盖回 `%DLL%`，或：

```bat
py -3 "<TOOL_DIR>\restore.py" "<GAME_DIR>" --dll "<你的官方原版 Assembly-CSharp.dll>"
```

`restore.py` 只替换 DLL，不动谱面/皮肤/成绩库/设置；回滚前会再备份一份当前 DLL。注意用官方原版 DLL 回滚会清掉**全部** IL 补丁。

## 11. 与其他补丁叠加

| 补丁 | 所在仓库 / 目录 | `--inspect` |
| --- | --- | --- |
| Fault 显示项 | 本仓库 `engine-src/` + `apply_fault.py` | `{"hasFault":bool}` |
| **倒计时显示项** | 本仓库 `countdown-src/` + `apply_countdown.py` | `{"hasCountdown":bool}` |
| 传感器配色 | `majdataplay-sensor-color` 仓库 | `{"r","g","b","a"}` |

Fault 与倒计时都往 `UpdateInfoDisplayerOutput` / `SetInfoDisplayerInitText` 开头插入判断块，但两块都采用「不匹配就跳到**当前第一条指令**」的写法，因此后插入的块会自然链接到先插入的块，**任意安装顺序都能同时生效**。配色补丁改的是 `DummyTouchPanelRenderer`，与两者完全无交集。

三个补丁针对同一版本 DLL，官方更新后需各自重新应用一次。
