# MajdataPlay 中心显示扩展（Center Display）

> 给游戏 HUD 的**中心信息显示位**添加自定义显示项。
> 针对 [MajdataPlay](https://github.com/LingFeng-bbben/MajdataPlay) **Windows 运行包 2.0.3 `20260917-637bba0`（Mono 后端）** 的非官方 IL 补丁集合。

## 什么是「中心显示」

MajdataPlay 在画面中心区域提供三个信息显示位，玩家在 `settings.json` 里用 `BGInfoOption` 枚举名选择每个位置显示什么（Combo、Achievement、PCombo 等）。每个显示位由 `ObjectCounter` 驱动，结构是「标题 + 数值」两行文本，数值每帧刷新。

| 显示位 | 设置键 | 说明 |
| --- | --- | --- |
| 主显示位 | `Game.BGInfo` | 主屏中央信息位，不受判定计时条影响 |
| 副显示位 | `Game.SecondaryBGInfo` | 主屏第二个信息位；**`EnableJudgeTimingGauge=true` 时会被强制隐藏** |
| 副屏显示位 | `Game.SubScreenBGInfo` | 副屏信息位，不受判定计时条影响 |

本仓库的扩展方式是：**给 `BGInfoOption` 追加新的枚举值，并在 `ObjectCounter.UpdateInfoDisplayerOutput` 与 `SetInfoDisplayerInitText` 开头插入对应的判断块**。新内容因此能像原生选项一样，在设置里被选到任意显示位、随谱面状态实时刷新，完全复用现有 HUD 框架 —— 不新建 UI 组件、不改 prefab、不碰 `resources.assets`。

这也是本仓库与 [传感器配色工具](https://github.com/zYanzy/majdataplay-sensor-color) 的分工：那个仓库改的是「调试 → 显示传感器」的高亮颜色，属于视觉改色；**本仓库只做中心显示位的内容扩展**。

## 本仓库提供的显示项

| 显示项 | 枚举值 | HUD 标题 | 显示内容 |
| --- | --- | --- | --- |
| **Fault 计数器** | `BGInfoOption.Fault = 16` | `Fault` | 初始 0，Miss / Good / Great 时每次 -1，以负数显示 |
| **倒计时** | `BGInfoOption.Countdown = 17` | `TIME LEFT` | 当前谱面剩余时间，格式 `M:SS`（如 `1:23`、`0:00`） |

原枚举为 0..15（`None = 15`），两个新值依次追加、互不冲突。两个显示项都是**独立补丁**：可以只装一个、任意顺序两个都装，与传感器配色补丁也互不干扰。

> 仓库曾用名 `majdataplay-fault-counter`，因只含第一个显示项而得名。加入倒计时后已更名为中心显示主题；GitHub 会自动把旧链接重定向到本仓库。

## 设计：一个显示项，一个引擎

| 原则 | 做法 |
| --- | --- |
| 单一职责 | 每个显示项一个独立 C# 引擎（`engine-src/` / `countdown-src/`），各自 `--inspect` 只校验自己的 IL 锚点 |
| 链式前置块 | 两个引擎都往同一对方法开头插判断块，且「不匹配就跳到**插入前的第一条指令**」。后装的块会自然链接到先装的块，形成 `Countdown → Fault → 原 switch`，**任意安装顺序都能同时生效**（已用官方原版 DLL 双向叠加 + 反编译验证） |
| 幂等 | 检测到自己的枚举值与字段已存在就跳过注入，重复运行安全 |
| 纯托管改动 | 只改 `BGInfoOption` 与 `ObjectCounter` 的 IL；不新增 UI 资源，配色工具只改 `DummyTouchPanelRenderer` |
| 可扩展 | 新增同类型显示项 = 追加一个枚举值 + 在两方法开头插入新前置块，照 `countdown-src/Program.cs` 的范式即可 |

三个引擎互不覆盖，因此官方更新后也**各自**重新应用即可，不需要从原版 DLL 整体重建。

## 显示项详解

### Fault 计数器

- `BGInfoOption` 追加 `Fault = 16`；`ObjectCounter` 增加 `long _fault`，`Clear()` 时归零。
- Miss / Good / 六个 Great 判定时每次 **-1**；**不含** Perfect 与 TooFast。
- 数值以负数形式输出到所选显示位，HUD 标题硬编码为 `Fault`。

### 倒计时

- `BGInfoOption` 追加 `Countdown = 17`；在 `ObjectCounter.UpdateInfoDisplayerOutput` 里输出剩余时间：

  ```csharp
  TimeSpan.FromSeconds(Math.Max(_gpManager.AudioLength - _gpManager.ThisFrameSec, 0f))
          .ToString("m\\:ss")
  ```

  即 `AudioLength - ThisFrameSec`；`Math.Max(..., 0)` 保证开局 Lead-In 与谱面结束后不显示负值，格式为 `M:SS`（`1:23`、`0:05`、`0:00`）。HUD 标题硬编码为 `TIME LEFT`。
- **不新增字段、不碰任何 UI 资源**：时间数据直接取自 `ObjectCounter` 已有的 `_gpManager` 字段（`GamePlayManager` 实例，在信息位更新之前就已赋值）。游戏本身也有 `TimeDisplayer.rTimeText` 用同样的算式显示剩余时间。
- 超过 60 分钟时分钟部分会回绕（音游谱面不会达到该长度）。

## 目录

| 路径 | 说明 |
| --- | --- |
| `engine-src/` | Fault 显示项补丁引擎源码（Mono.Cecil 0.11.6） |
| `apply_fault.py` / `应用Fault补丁.bat` | Fault 一键应用：兼容性校验 → 备份 → 生成 → 双重校验 → 替换 |
| `INSTALL.md` | Fault 部署指南，供 AI Agent 阅读执行 |
| `countdown-src/` | 倒计时显示项补丁引擎源码（独立引擎） |
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

## 在设置里选显示位（改 `settings.json`，改前先备份）

三个显示位可任选，值用字符串枚举名：

| 想显示 | 设成 |
| --- | --- |
| Fault 计数器 | `"Fault"` |
| 倒计时 | `"Countdown"` |
| 不显示 | `"None"` |

例如两个显示项同时上屏：

```json
"BGInfo": "Fault",
"SecondaryBGInfo": "Countdown",
"SubScreenBGInfo": "Achievement"
```

**关键**：`Game.EnableJudgeTimingGauge` 必须为 `false`。为 `true` 时初始化阶段会把 `SecondaryBGInfo` 强制改为 `None`，副显示位被整体隐藏，设置里选中了也不显示（主显示位 `BGInfo` 与副屏不受此限）。

设置项名在中文界面可能显示英文 `Fault` / `Countdown` —— 运行包语言包由 `Resources.Load<TextAsset>("Langs/<code>")` 读取并打包进 `resources.assets`，本补丁不改；HUD 标题硬编码，功能不受影响。数值与标题颜色沿用信息位默认色。

## 注意事项

- **MajdataHub 官方增量更新会覆盖 `Assembly-CSharp.dll`，所有补丁都会丢失**；更新后需**分别**重跑每个已装显示项的应用脚本。
- **不要用官方原版 DLL 整体回滚来"恢复"**，那会同时清掉 Fault、倒计时和配色；三个引擎本身都是叠加式的。
- 只替换 `Assembly-CSharp.dll` 这一个文件；绝不整目录覆盖，不动 `MaiCharts/`、`Skins/`、`MajScores.db`。
- 不要在游戏运行时替换 DLL；改动需重启游戏才生效。
- `dev-source-changes/` 对应 **2.1.0 dev 分支**结构，**不要**用它编译出的 DLL 覆盖 2.0.3 运行包；两者语言包格式与设置枚举也不同。倒计时目前只有 IL 实现，未包含在该目录中。
- 仓库不含游戏本体、官方二进制、谱面、皮肤、成绩库或个人设置。

## 许可与声明

本项目是对 GPLv3 项目 MajdataPlay 的非官方修改。`LICENSE` 为官方 GPLv3 全文，`dev-source-changes/` 中对官方源码的修改同样按 GPLv3 提供；本仓库其余原创内容也按 GPLv3 发布。第三方依赖 [Mono.Cecil](https://github.com/jbevain/cecil) 为 MIT。

本仓库与 MajdataPlay 官方无关联。以 IL 补丁修改游戏程序集可能不符合相关使用条款，请自行评估；使用前请备份，作者不对数据丢失或账号问题负责。
