# 汉末浮生正式游戏

本目录是正式游戏实现，与仓库根目录的分钟级技术原型相互独立。

## 运行

需要仓库 `global.json` 指定的 .NET 10 SDK。

```powershell
dotnet restore game/LateHan.Game.slnx
dotnet build game/LateHan.Game.slnx --configuration Release
dotnet test game/LateHan.Game.slnx --configuration Release
dotnet run --project game/src/LateHan.Game.App/LateHan.Game.App.csproj --configuration Release
dotnet run --project game/tools/LateHan.Game.Content.Tool/LateHan.Game.Content.Tool.csproj -- game/src/LateHan.Game.Content/Data/189-central-plains.v1.json
```

## 项目结构

```text
src/
  LateHan.Game.Domain/       日历、地图、人物和场景等稳定领域语言
  LateHan.Game.Simulation/   玩家行动、世界推进和快照状态
  LateHan.Game.Content/      外部场景加载、来源审计和内容校验
  LateHan.Game.Persistence/  JSON 存档适配器
  LateHan.Game.App/          Avalonia 中文桌面客户端
tests/                       对应领域、内容、模拟和存档测试
tools/                       场景内容校验等作者工具
```

`Domain` 不引用 Avalonia、JSON、数据库或旧 `LateHan.Core`。默认场景位于 `src/LateHan.Game.Content/Data/189-central-plains.v1.json`，也可通过 `LATEHAN_SCENARIO_PATH` 指向另一份场景文件，无需修改领域代码。

每个敏感实体必须记录来源、字段覆盖、置信度、争议和玩法假设。当前能力数值、人物精确位置、道路耗时和地方初态中仍有大量 C/D 级推断，它们是可替换的模拟标定，不是已经完成逐人考据的历史结论。具体契约见 `docs/architecture/formal-scenario-content.md`。
