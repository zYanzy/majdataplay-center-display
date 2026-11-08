# MajdataPlay Fault 计数器补丁

针对 [MajdataPlay](https://github.com/LingFeng-bbben/MajdataPlay) **Windows 运行包 2.0.3 `20260917-637bba0`（Mono 后端）** 的非官方 IL 补丁：新增一个与 Combo 同类型的信息显示项 `Fault`。

**本仓库只做这一件事。** 它与独立的 [传感器配色工具](https://github.com/zYanzy/majdataplay-sensor-color) 互不依赖：可以只装本补丁、只装配色工具，或两个都装（顺序随意）。两个引擎分别只改 `BGInfoOption`/`ObjectCounter` 与 `DummyTouchPanelRenderer`，不会覆盖对方的改动。

仓库不含游戏本体、官方二进制、谱面、皮肤、成绩库或个人设置。

## 功能

- `BGInfoOption` 追加 `Fault = 16`（原枚举 0..15，`None = 15`）。
- `ObjectCounter` 增加 `long _fault`，`Clear()` 时归零。
- Miss / Good / 六个 Great 判定时每次 **-1**；**不含** Perfect 与 TooFast。
- 数值以负数形式输出到信息显示位，HUD 标题硬编码为 `Fault`。
- 设置里把 `Game.BGInfo` 设为 `"Fault"` 即可显示（也可放 `SecondaryBGInfo`）。

## 目录

| 路径 | 说明 |
| --- | --- |
| `engine-src/` | C# 补丁引擎源码（Mono.Cecil 0.11.6，只注入 Fault） |
| `apply_fault.py` | 命令行一键应用：兼容性校验 → 备份 → 生成 → 双重校验 → 替换 |
| `应用Fault补丁.bat` | 双击入口，输入游戏目录即可 |
| `restore.py` | 用自备的官方原版 DLL 回滚（只换 DLL） |
| `dev-source-changes/` | 按 2.1.0 dev 分支结构修改的 10 个源码文件（供未来完整 Build 参考） |
| `INSTALL.md` | 在另一台同版本客户端部署的完整指南，供 AI Agent 阅读执行 |
| `LICENSE` | GPLv3 全文，来自官方仓库 |

引擎命令行：

```bat
dotnet engine\patcher.dll <input.dll> --inspect          :: {"hasFault":true|false}
dotnet engine\patcher.dll <input.dll> <output.dll>      :: 注入（幂等）
```

`--inspect` 会校验全部 IL 锚点，因此同时充当**版本兼容性检查**；输出不了 JSON 就说明程序集版本不对，必须停止。

## 环境与首次构建

- Windows + .NET 10 **SDK**（从仓库 clone 时需要 `dotnet build`；拿到已含 `engine/` 的离线包则只需 .NET 10 **运行时**）
- Python 3（仅标准库，**不需要 tkinter**）用于一键脚本；也可完全按 `INSTALL.md` 手工调用引擎

```bat
dotnet build engine-src\patcher.csproj -c Release -o engine
双击 应用Fault补丁.bat
```

或直接：

```bat
py -3 apply_fault.py "D:\Games\MajdataPlay_Build-LATEST"
```

## 让计数器显示出来（改 `settings.json`，改前先备份）

| 路径 | 值 | 说明 |
| --- | --- | --- |
| `Game.BGInfo` | `"Fault"` | 推荐放主显示位 |
| `Game.SecondaryBGInfo` | `"None"` | 也可设为 `"Fault"`，但见下行 |
| `Game.EnableJudgeTimingGauge` | `false` | **若 Fault 放在 `SecondaryBGInfo`，此项必须为 false**，否则初始化时副显示位被强制改为 `None`，设置里选中了也不显示 |

设置项名在中文界面可能显示英文 `Fault`——运行包语言包由 `Resources.Load<TextAsset>("Langs/<code>")` 读取，打包进 `resources.assets`，本补丁不改；HUD 标题与功能不受影响。

## 注意事项

- **MajdataHub 官方增量更新会覆盖 `Assembly-CSharp.dll`，补丁会丢失**；更新后重新运行一次 `apply_fault.py` 即可。
- 只替换 `Assembly-CSharp.dll` 这一个文件；绝不整目录覆盖，不动 `MaiCharts/`、`Skins/`、`MajScores.db`。
- 不要在游戏运行时替换 DLL；改动需重启游戏才生效。
- `dev-source-changes/` 对应 **2.1.0 dev 分支**结构，**不要**用它编译出的 DLL 覆盖 2.0.3 运行包；两者语言包格式与设置枚举也不同。
- 若同时安装配色工具：先装哪个都行；回滚时若用官方原版 DLL 整体覆盖，会**同时**清掉两个补丁，需要各自重新应用。

## 许可与声明

本项目是对 GPLv3 项目 MajdataPlay 的非官方修改。`LICENSE` 为官方 GPLv3 全文，`dev-source-changes/` 中对官方源码的修改同样按 GPLv3 提供；本仓库其余原创内容也按 GPLv3 发布。第三方依赖 [Mono.Cecil](https://github.com/jbevain/cecil) 为 MIT。

本仓库与 MajdataPlay 官方无关联。以 IL 补丁修改游戏程序集可能不符合相关使用条款，请自行评估；使用前请备份，作者不对数据丢失或账号问题负责。
