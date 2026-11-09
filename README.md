# MajdataPlay 信息显示项补丁：Fault 计数器 + 倒计时

针对 [MajdataPlay](https://github.com/LingFeng-bbben/MajdataPlay) **Windows 运行包 2.0.3 `20260917-637bba0`（Mono 后端）** 的非官方 IL 补丁。本仓库提供**两个彼此独立、与 Combo 同类型**的 HUD 信息显示项：

| 功能 | 显示内容 | 枚举值 |
| --- | --- | --- |
| **Fault 计数器** | 初始 0，Miss / Good / Great 时每次 -1 | `BGInfoOption.Fault = 16` |
| **倒计时**（新增） | 当前谱面剩余时间，格式 `M:SS`（如 `1:23`） | `BGInfoOption.Countdown = 17` |

> 仓库名 `majdataplay-fault-counter` 沿用历史，实际含两个独立补丁。

两个补丁各自只有一个引擎，**互不依赖**：可以只装一个、也可以按任意顺序两个都装；与独立的 [传感器配色工具](https://github.com/zYanzy/majdataplay-sensor-color) 也互不干扰。两个引擎都只改 `BGInfoOption` 与 `ObjectCounter`，配色工具只改 `DummyTouchPanelRenderer`。

仓库不含游戏本体、官方二进制、谱面、皮肤、成绩库或个人设置。

## 功能一：Fault 计数器

- `BGInfoOption` 追加 `Fault = 16`（原枚举 0..15，`None = 15`）。
- `ObjectCounter` 增加 `long _fault`，`Clear()` 时归零。
- Miss / Good / 六个 Great 判定时每次 **-1**；**不含** Perfect 与 TooFast。
- 数值以负数形式输出到信息显示位，HUD 标题硬编码为 `Fault`。

## 功能二：倒计时（本次新增）

- `BGInfoOption` 追加 `Countdown = 17`（不与 Fault 冲突）。
- 在 `ObjectCounter.UpdateInfoDisplayerOutput` 里输出剩余时间：

  ```csharp
  TimeSpan.FromSeconds(Math.Max(_gpManager.AudioLength - _gpManager.ThisFrameSec, 0f))
          .ToString("m\\:ss")
  ```

  即 `AudioLength - ThisFrameSec`，`Math.Max(..., 0)` 保证开局 Lead-In 与谱面结束后不会显示负值；格式为 `M:SS`（`1:23`、`0:05`、`0:00`）。
- HUD 标题硬编码为 `TIME LEFT`。
- **不新增字段、不碰任何 UI 资源**：时间数据直接取自 `ObjectCounter` 已有的 `_gpManager` 字段（`GamePlayManager` 实例，在信息位更新之前就已赋值）。完全落在托管 C# 逻辑里，纯 IL 即可实现。
- 超过 60 分钟时分钟部分会回绕（音游谱面不会达到该长度）。

两个引擎往 `UpdateInfoDisplayerOutput` / `SetInfoDisplayerInitText` 开头插入的判断块都采用「不匹配就跳到**当前第一条指令**」的写法，因此后装的块会自然链接到先装的块，**任意安装顺序都能同时生效**（已用官方原版 DLL 双向叠加 + 反编译验证）。

## 目录

| 路径 | 说明 |
| --- | --- |
| `engine-src/` | Fault 补丁引擎源码（Mono.Cecil 0.11.6） |
| `apply_fault.py` / `应用Fault补丁.bat` | Fault 一键应用：兼容性校验 → 备份 → 生成 → 双重校验 → 替换 |
| `INSTALL.md` | Fault 部署指南，供 AI Agent 阅读执行 |
| `countdown-src/` | **倒计时补丁引擎源码**（独立引擎） |
| `apply_countdown.py` / `应用倒计时补丁.bat` | 倒计时一键应用（同样的六步安全流程） |
| `INSTALL-COUNTDOWN.md` | 倒计时部署指南 |
| `restore.py` | 用自备的官方原版 DLL 回滚（只换 DLL） |
| `dev-source-changes/` | 按 2.1.0 dev 分支结构修改的 10 个源码文件（供未来完整 Build 参考，仅含 Fault） |
| `LICENSE` | GPLv3 全文，来自官方仓库 |

两个引擎的命令行（**输出到不同目录，不要混用**）：

```bat
:: Fault
dotnet engine\patcher.dll <input.dll> --inspect          :: {"hasFault":true|false}
dotnet engine\patcher.dll <input.dll> <output.dll>      :: 注入（幂等）

:: 倒计时
dotnet countdown-engine\patcher.dll <input.dll> --inspect      :: {"hasCountdown":true|false}
dotnet countdown-engine\patcher.dll <input.dll> <output.dll>  :: 注入（幂等）
```

`--inspect` 会校验自己的全部 IL 锚点，因此同时充当**版本兼容性检查**；输出不了 JSON 就说明程序集版本不对，必须停止。三份 `--inspect` 的格式各不相同（Fault / Countdown / 配色的 RGBA），不要用错。

## 环境与首次构建

- Windows + .NET 10 **SDK**（从仓库 clone 时需要 `dotnet build`；拿到已含 `engine/` 与 `countdown-engine/` 的离线包则只需 .NET 10 **运行时**）
- Python 3（仅标准库，**不需要 tkinter**）用于一键脚本；也可完全按各自的 INSTALL 指南手工调用引擎

```bat
dotnet build engine-src\patcher.csproj -c Release -o engine
dotnet build countdown-src\patcher.csproj -c Release -o countdown-engine
```

然后按需双击对应的 bat，或直接：

```bat
py -3 apply_fault.py "D:\Games\MajdataPlay_Build-LATEST"
py -3 apply_countdown.py "D:\Games\MajdataPlay_Build-LATEST"
```

## 让功能显示出来（改 `settings.json`，改前先备份）

三个显示位 `Game.BGInfo` / `Game.SecondaryBGInfo` / `Game.SubScreenBGInfo` 可任选，值用字符串枚举名：

| 想显示 | 设成 |
| --- | --- |
| Fault 计数器 | `"Fault"` |
| 倒计时 | `"Countdown"` |
| 不显示 | `"None"` |

例如同时显示两个：

```json
"BGInfo": "Fault",
"SecondaryBGInfo": "Countdown",
"SubScreenBGInfo": "Achievement"
```

**关键**：`Game.EnableJudgeTimingGauge` 必须为 `false`。为 `true` 时初始化阶段会把 `SecondaryBGInfo` 强制改为 `None`，副显示位被整体隐藏，设置里选中了也不显示（主显示位 `BGInfo` 与副屏不受此限）。

设置项名在中文界面可能显示英文 `Fault` / `Countdown` —— 运行包语言包由 `Resources.Load<TextAsset>("Langs/<code>")` 读取并打包进 `resources.assets`，本补丁不改；HUD 标题硬编码，功能不受影响。数值与标题颜色沿用信息位默认色（与 Fault 一致）。

## 注意事项

- **MajdataHub 官方增量更新会覆盖 `Assembly-CSharp.dll`，所有补丁都会丢失**；更新后需**分别**重跑每个已装补丁的应用脚本。
- **不要用官方原版 DLL 整体回滚来"恢复"**，那会同时清掉 Fault、倒计时和配色；三个引擎本身都是叠加式的。
- 只替换 `Assembly-CSharp.dll` 这一个文件；绝不整目录覆盖，不动 `MaiCharts/`、`Skins/`、`MajScores.db`。
- 不要在游戏运行时替换 DLL；改动需重启游戏才生效。
- `dev-source-changes/` 对应 **2.1.0 dev 分支**结构，**不要**用它编译出的 DLL 覆盖 2.0.3 运行包；两者语言包格式与设置枚举也不同。倒计时目前只有 IL 实现，未包含在该目录中。

## 许可与声明

本项目是对 GPLv3 项目 MajdataPlay 的非官方修改。`LICENSE` 为官方 GPLv3 全文，`dev-source-changes/` 中对官方源码的修改同样按 GPLv3 提供；本仓库其余原创内容也按 GPLv3 发布。第三方依赖 [Mono.Cecil](https://github.com/jbevain/cecil) 为 MIT。

本仓库与 MajdataPlay 官方无关联。以 IL 补丁修改游戏程序集可能不符合相关使用条款，请自行评估；使用前请备份，作者不对数据丢失或账号问题负责。
