# 汉末浮生（The Late Han Wanderer）

一款以汉末为背景、以个人为观察与行动尺度的动态历史模拟游戏。

项目当前处于**世界机器技术尖峰阶段**。首要目标不是制作图形界面或堆叠剧情，而是建立一套能够自行运转、允许玩家介入、并能解释自身因果的世界规则。

## 当前里程碑

- 确立世界模拟总设计与“世界宪法”。
- 建立历史研究的来源、置信度与争议处理规范。
- 定义首个 189 年洛阳危机时空切片。
- 定义时间、空间、人物、组织、信息、行动与事件的数据边界。
- 已定义“雒阳四日”命令行 MVP、场景夹具、行为验收和规模预算。
- 已实现无界面 C#/.NET 内核的首个行动、因果事件、快照、稳定调度和确定性随机流切片。

## 仓库导航

```text
docs/
  design/          产品愿景、玩法原则和系统设计
  research/        史料方法、来源目录、历史切片和研究结论
  architecture/    世界模型、技术边界和实现约束
  decisions/       需要长期保留理由的架构决策记录（ADR）
data/
  historical/      经审核的机器可读历史事实与主张
  scenarios/       可运行的世界初始状态
src/               模拟核心、场景/存档适配器与命令行界面
tests/             确定性、因果、历史约束和压力测试
tools/             史料导入、校验、模拟和调试工具
```

主要文档：

- [世界模拟总设计](docs/design/overview.md)
- [命令行 MVP 规格：雒阳四日](docs/design/mvp-spec.md)
- [历史研究入口](docs/research/README.md)
- [首个历史切片：189 年洛阳危机](docs/research/slices/189-luoyang-crisis.md)
- [世界模型](docs/architecture/world-model.md)
- [场景数据契约](docs/architecture/scenario-contract.md)
- [MVP 行为验收](tests/specs/189-luoyang-crisis.md)
- [世界规模与性能预算](tests/performance/world-scale-budgets.md)
- [架构决策记录](docs/decisions/README.md)

## 运行当前技术尖峰

需要 .NET 10 SDK；`global.json` 当前锁定 `10.0.302`。

```powershell
dotnet restore LateHanWanderer.sln
dotnet build LateHanWanderer.sln --configuration Release
dotnet test tests/LateHan.Tests/LateHan.Tests.csproj --configuration Release
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release
```

也可以无交互执行完整送信路径：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "go place.luoyang.sili_office walk" `
  --command "give item.sealed_note_to_yuan_shao to person.yuan_shao" `
  --command "go place.luoyang.general_in_chief_office walk" `
  --command "tell person.li_wen proposition.general_office_requests_status" `
  --command "history"
```

可用开发者命令复现“等待四小时，在第 95 分钟被紧急召回”：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "dev schedule 95 summary_and_notification person.player_clerk urgent_recall interrupt" `
  --command "dev queue" `
  --command "wait 4h" `
  --command "history"
```

`dev schedule` 是显式外部干预，会写入事件日志并把回放标记为 `modified`。`dev rng` 只预览流副本，不改变模拟状态。

## 基本工作流

1. 在 `docs/research/` 中提出有来源的历史主张，并记录置信度与争议。
2. 通过审核的主张进入 `data/historical/`，但仍保留来源引用。
3. 场景构建器从历史主张和显式玩法假设生成 `data/scenarios/` 中的初始状态。
4. 模拟核心只消费结构化场景，不把历史人物或专属剧情硬编码进通用规则。
5. 所有重大状态变化产生事件，并可通过事件日志追溯原因。

## 当前设计纪律

- 不把未知史实填成看似精确的数字。
- 不让人物读取其不可能知道的全局状态。
- 不让城市隔空共享粮食、消息或军队。
- 不为玩家绕过时间、空间、身份和资源约束。
- 不用生成式文本代替确定、可复现的世界规则。
