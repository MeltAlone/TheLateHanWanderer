# 正式游戏场景内容管线

> 适用项目：`game/LateHan.Game.slnx`
>
> 当前契约版本：schema 1，内容版本 1.0.0

## 目标与边界

正式游戏以日、旬、月为时间尺度。`LateHan.Game.Content` 负责从外部 JSON 创建 `GameScenario`；Domain 和 Simulation 不知道 JSON、文件路径、历史数据库或作者工具。

默认场景是 `game/src/LateHan.Game.Content/Data/189-central-plains.v1.json`。运行时按以下优先级选取文件：

1. `DemoScenarioFactory.Create(path)` 的显式路径；
2. 环境变量 `LATEHAN_SCENARIO_PATH`；
3. 程序输出目录中的 `Data/189-central-plains.v1.json`。

场景文件使用 UTF-8、camelCase、字符串枚举，允许注释和尾逗号。`schemaVersion` 表示结构契约，`contentVersion` 表示内容修订；修改显示名不需要修改稳定 ID。

## 审计记录

每个场景、城邑、城内地点、道路、人物、身份和话题都有一条 `audit` 记录。每个 `origin` 必须包含：

- `kind`：`historical_claim`、`bounded_inference`、`gameplay_assumption` 或 `simulation_seed`；
- `appliesTo`：该记录覆盖的业务字段；
- `sourceIds`：指向场景内 `sources` 目录的来源 ID；
- `confidence`：A、B、C 或 D；
- `dispute`：史料冲突、时间差或精度局限；
- `gameplayAssumption`：为了形成可运行世界所作的裁决。

首版数据的地点方向和人物大体身份受史料约束，但画布坐标、旅行日数、六维能力、性格、动机、人物精确位置以及治安、粮价等 0..100 数值多为 D 级玩法标定。外部化和审计完成不等于逐人考据完成。

## 加载校验

加载器先完整反序列化和校验，全部通过后才创建领域世界。当前稳定错误类别包括：

| 错误码 | 含义 |
|---|---|
| `SCN-IO-*` / `SCN-JSON-*` | 文件不可读、JSON 或日期结构无效 |
| `SCN-VERSION-*` | schema 或内容版本无效 |
| `SCN-ID-*` | ID 为空或重复 |
| `SCN-REF-*` | 开局、道路、人物或初态引用无效 |
| `SCN-GRAPH-*` | 没有城邑或道路图不连通 |
| `SCN-RANGE-*` | 行程、能力、地方状态或风险越界 |
| `SCN-L10N-*` | 玩家可见内容缺少中文 |
| `SCN-SOURCE-*` / `SCN-AUDIT-*` | 来源、置信度、说明或字段覆盖不完整 |

作者修改 JSON 后运行：

```powershell
dotnet run --project game/tools/LateHan.Game.Content.Tool/LateHan.Game.Content.Tool.csproj -- game/src/LateHan.Game.Content/Data/189-central-plains.v1.json
dotnet test game/tests/LateHan.Game.Content.Tests/LateHan.Game.Content.Tests.csproj --configuration Release
```

校验通过只证明数据结构、自洽性、汉化和审计元数据完整，不证明历史主张已经得到最终学术裁决。新增人物仍需按 `docs/research/methodology.md` 建立独立主张和互证。
