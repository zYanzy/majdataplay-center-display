# 部署指南：在另一台电脑上只安装 Fault 计数器

> 本文件供 AI Agent 阅读执行。请严格按顺序操作，**任何一步校验失败就停止并向用户报告**，不要猜测、不要跳过、不要强行写入程序集。
>
> 本指南**只装 Fault 计数器**，不会改动倒计时显示项或传感器高亮颜色。同仓库新增的倒计时补丁见 `INSTALL-COUNTDOWN.md`；传感器配色工具见 `majdataplay-sensor-color` 仓库的 INSTALL.md。三份补丁可任意顺序叠加。

## 1. 适用范围（硬性前提）

| 项目 | 要求 |
| --- | --- |
| 游戏 | MajdataPlay **Windows 运行包 2.0.3 `20260917-637bba0`，Mono 后端** |
| 目标文件 | `<GAME_DIR>\MajdataPlay_Data\Managed\Assembly-CSharp.dll` |
| 官方原版指纹 | SHA256 `139817f1e0147f8fe21fdc6c0a24303f3a93c98a7fd049c4eebf54a09c875ad9`，大小 `1,241,600` 字节 |
| 版本标识 | `<GAME_DIR>\MajdataPlay_Data\app.info` |

**版本不匹配必须停止。** IL 锚点（`BGInfoOption` 枚举、`ObjectCounter.Clear` / `UpdateComboCount` / `UpdateInfoDisplayerOutput` / `SetInfoDisplayerInitText`、`TMP_Text::set_text` 引用）绑定这一版程序集；其他版本（含 dev 分支 2.1.0）结构不同，强行注入会损坏程序集。**不要**拿 2.1.0 源码编译出的 DLL 覆盖 2.0.3 运行包。

指纹不同**不一定**是版本不对：DLL 可能已被其他补丁（如传感器配色）改过，或已被本补丁打过。此时以第 5 节 `--inspect` 的结果为准，能正常输出 JSON 就说明锚点兼容。

## 2. 依赖

- Windows
- .NET 10 **SDK**（clone 路线需要 `dotnet build`）；若拿到已含 `engine/` 编译产物的离线目录，只需 .NET 10 **运行时**
- Python 3（**仅标准库，不需要 tkinter**），用于一键脚本；也可完全手工调用引擎
- git（可选）

## 3. 获取代码

**方式 A — 公开仓库（推荐）**

```bash
git clone https://github.com/zYanzy/majdataplay-fault-counter.git
```

仓库**不含** `engine/`（编译产物被 `.gitignore` 排除），必须执行第 4 步构建。

**方式 B — 离线拷贝**

让用户把整个仓库目录拷过来（含 `engine/patcher.dll` 与 Mono.Cecil 的 DLL），可跳过第 4 步。

下文用 `<TOOL_DIR>` 表示该目录。

## 4. 构建补丁引擎（方式 A 必做）

```bat
dotnet build "<TOOL_DIR>\engine-src\patcher.csproj" -c Release -o "<TOOL_DIR>\engine"
```

依赖 NuGet 包 `Mono.Cecil 0.11.6`。若因无 SDK / 无网络构建失败，改用方式 B 获取已编译的 `engine/`，**不要**自行改写 csproj 的目标框架，也不要编译游戏根目录的 `.sln`。

## 5. 兼容性校验（写入前必做）

1. 找到含 `MajdataPlay.exe` 的目录，记为 `<GAME_DIR>`。**向用户确认，不要猜**。
2. 确认游戏已完全退出；DLL 被占用时不能替换。
3. 执行：

```bat
set "DLL=<GAME_DIR>\MajdataPlay_Data\Managed\Assembly-CSharp.dll"
dotnet "<TOOL_DIR>\engine\patcher.dll" "%DLL%" --inspect
```

期望输出**一行 JSON**：

- 未安装过：`{"hasFault":false}`
- 已安装过：`{"hasFault":true}`

**只有成功输出 JSON 才能继续**。抛异常（找不到类型/方法/`TMP_Text::set_text`）、返回码非 0、输出不是 JSON → 版本不兼容，立即停止并报告。

## 6. 生成并写入补丁

**推荐：一键脚本**（内含校验、备份、替换后复检，任何一步失败都不会写入运行包）：

```bat
py -3 "<TOOL_DIR>\apply_fault.py" "<GAME_DIR>"
```

预期输出依次包含 `source inspect`、`backup`、`generate`（`OK hasFault=injected` 或 `existing`）、`staged inspect`、`final inspect`、末尾 `DONE`。脚本会：

- 备份当前 DLL 到 `<TOOL_DIR>\backups\Assembly-CSharp_<时间戳>.dll`
- 生成到 `<TOOL_DIR>\output\`，二次 `--inspect` 确认 `hasFault=true`
- **只替换** `%DLL%` 这一个文件，再对运行包做最终 `--inspect`
- 检测到 `MajdataPlay.exe` 正在运行会直接拒绝

**或手工执行**（效果相同，请自行完成同等校验）：

```bat
dotnet "<TOOL_DIR>\engine\patcher.dll" "%DLL%" "<TOOL_DIR>\output\Assembly-CSharp.patched.dll"
dotnet "<TOOL_DIR>\engine\patcher.dll" "<TOOL_DIR>\output\Assembly-CSharp.patched.dll" --inspect
copy /Y "<TOOL_DIR>\output\Assembly-CSharp.patched.dll" "%DLL%"
dotnet "<TOOL_DIR>\engine\patcher.dll" "%DLL%" --inspect
```

最后一行必须输出 `{"hasFault":true}`。Git Bash 下把 `copy /Y` 换成 `cp -f`。

## 7. 让功能在游戏里可见（改 `settings.json`，改前先备份）

只改这几个键，其余内容一律保留：

| 路径 | 值 | 说明 |
| --- | --- | --- |
| `Game.BGInfo` | `"Fault"` | 推荐放主显示位 |
| `Game.SecondaryBGInfo` | `"None"` | 也可设为 `"Fault"`，但见下行 |
| `Game.EnableJudgeTimingGauge` | `false` | **若 Fault 放在 `SecondaryBGInfo`，此项必须为 false**，否则初始化时副显示位被强制改为 `None`，设置里选中也不显示 |

设置项名在中文界面可能显示英文 `Fault`（语言包打包在 `resources.assets`，本补丁不改），HUD 标题硬编码 `Fault`，功能不受影响。

## 8. 验收

1. 手动双击 `MajdataPlay.exe` 启动游戏（**不要**由 Agent 的命令会话启动，进程常被回收且容易双开抢串口/音频），进入任意谱面。
2. 信息位显示 `Fault`，初始 0；出现 GREAT / GOOD / MISS 时每次 -1；Perfect 与 TooFast 不变。
3. 游戏日志中无 `InvalidProgramException` / `BadImageFormatException` / 程序集加载失败。
4. `MaiCharts/`、`Skins/`、`MajScores.db` 未被改动。
5. 传感器高亮仍为官方原版灰（本补丁不碰颜色）——这正好证明两个功能彼此独立。

## 9. 禁止事项

- **只替换 `Assembly-CSharp.dll`**，绝不整目录覆盖；不要动 `MaiCharts/`、`Skins/`、`MajScores.db`、`Logs/` 及 `settings.json` 的其他键。
- 不要用 `dotnet build` / VS / MSBuild 编译游戏根目录的 `.sln` / `.csproj`（那些只给 IDE 提示用）。
- 不要在游戏运行时替换 DLL。
- 不要把任何游戏二进制、谱面、成绩库、个人设置上传或提交到版本库。
- **MajdataHub 官方增量更新会覆盖 `Assembly-CSharp.dll` 导致补丁丢失**；更新后重跑第 5–6 步。
- 如果之后还要装传感器配色补丁，不要为了"恢复"而用官方原版 DLL 整体覆盖——那会清掉已装好的 Fault；配色引擎本身是叠加式的。

## 10. 回滚

用第 6 步生成的 `backups\Assembly-CSharp_<时间戳>.dll` 覆盖回 `%DLL%`，或：

```bat
py -3 "<TOOL_DIR>\restore.py" "<GAME_DIR>" --dll "<你的官方原版 Assembly-CSharp.dll>"
```

`restore.py` 只替换 DLL，不动谱面/皮肤/成绩库/设置；回滚前会再备份一份当前 DLL。

## 11. 与其他补丁叠加

| 补丁 | 位置 | `--inspect` |
| --- | --- | --- |
| **Fault 计数器**（本指南） | `engine-src/` + `apply_fault.py` | `{"hasFault":bool}` |
| 倒计时显示项 | 同仓库 `countdown-src/` + `apply_countdown.py`，详见 `INSTALL-COUNTDOWN.md` | `{"hasCountdown":bool}` |
| 传感器配色 | `majdataplay-sensor-color` 独立仓库 | `{"r","g","b","a"}` |

倒计时与 Fault 都往 `ObjectCounter.UpdateInfoDisplayerOutput` / `SetInfoDisplayerInitText` 开头插入判断块，但两块都用「不匹配就跳到**当前第一条指令**」的写法，因此后装的块会自然链接到先装的块，**任意安装顺序都能同时生效**。传感器配色补丁只改 `DummyTouchPanelRenderer.OnPreUpdate()` 的 4 个颜色常量，与两者完全无交集。

三者针对同一版本 DLL；官方更新后需各自重新应用一次，不要为了"恢复"用官方原版 DLL 整体覆盖运行包。
