# 汉末浮生正式游戏

本目录是正式游戏实现，与仓库根目录的分钟级技术原型相互独立。

## 运行

需要仓库 `global.json` 指定的 .NET 10 SDK。

```powershell
dotnet restore game/LateHan.Game.slnx
dotnet build game/LateHan.Game.slnx --configuration Release
dotnet test game/LateHan.Game.slnx --configuration Release
dotnet run --project game/src/LateHan.Game.App/LateHan.Game.App.csproj --configuration Release
```

## 项目结构

```text
src/
  LateHan.Game.Domain/       日历、地图、人物和场景等稳定领域语言
  LateHan.Game.Simulation/   玩家行动、世界推进和快照状态
  LateHan.Game.Content/      当前方向验证场景夹具
  LateHan.Game.Persistence/  JSON 存档适配器
  LateHan.Game.App/          Avalonia 中文桌面客户端
tests/                       对应领域、内容、模拟和存档测试
```

`Domain` 不引用 Avalonia、JSON、数据库或旧 `LateHan.Core`。当前内容是用于验证产品尺度的场景夹具，其中能力数值、人物精确位置、道路耗时和部分城内设施包含玩法假设，不应被当作已经完成史料校核的历史结论。正式内容迁移必须遵循仓库已有的来源、置信度和争议记录流程。
