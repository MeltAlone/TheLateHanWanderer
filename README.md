# 汉末浮生（The Late Han Wanderer）

一款以汉末为背景、以个人为观察与行动尺度的动态历史模拟游戏。

项目当前处于**世界机器技术尖峰阶段**。首要目标不是制作图形界面或堆叠剧情，而是建立一套能够自行运转、允许玩家介入、并能解释自身因果的世界规则。

## 当前里程碑

- 确立世界模拟总设计与“世界宪法”。
- 建立历史研究的来源、置信度与争议处理规范。
- 定义首个 189 年洛阳危机时空切片。
- 定义时间、空间、人物、组织、信息、行动与事件的数据边界。
- 已定义“雒阳四日”命令行 MVP、场景夹具、行为验收和规模预算。
- 已实现无界面 C#/.NET 内核的持久行动、门禁、凭证、消息谱系、认知隔离、稳定调度、首个自主 NPC 闭环和确定性群体升降格。

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

- [世界机器实施计划](docs/implementation-plan.md)
- [世界模拟总设计](docs/design/overview.md)
- [命令行 MVP 规格：雒阳四日](docs/design/mvp-spec.md)
- [历史研究入口](docs/research/README.md)
- [首个历史切片：189 年洛阳危机](docs/research/slices/189-luoyang-crisis.md)
- [世界模型](docs/architecture/world-model.md)
- [场景数据契约](docs/architecture/scenario-contract.md)
- [MVP 行为验收](tests/specs/189-luoyang-crisis.md)
- [世界规模与性能预算](tests/performance/world-scale-budgets.md)
- [P6 精度与规模报告](docs/architecture/p6-scale-report.md)
- [架构决策记录](docs/decisions/README.md)

## 运行当前技术尖峰

需要 .NET 10 SDK；`global.json` 当前锁定 `10.0.302`。

```powershell
dotnet restore LateHanWanderer.sln
dotnet build LateHanWanderer.sln --configuration Release
dotnet test tests/LateHan.Tests/LateHan.Tests.csproj --configuration Release
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release
```

除传统单文件 `save/load` 外，CLI 已可直接使用长期归档、分支和因果查询：

```text
archive save saves/luoyang.db
archive load saves/luoyang.db
branch create saves/luoyang.db saves/branches/deliver deliver
branch save saves/branches/deliver
branch load saves/branches/deliver
why event.00000004 8
```

归档追加前会审计已有历史前缀；不同历史不能接到同一文件。加载归档时会从检查点按尾事件确定性再执行，并逐事件核对事件指纹；最新快照损坏或载荷哈希不符时会只读回退到更早检查点再恢复，不改写原归档。分支基础库以只读模式共享，分支目录只保存自己的事件尾与世界检查点。

存档版本不兼容时会列出实际版本、当前场景要求的版本和可用迁移器，不会用当前规则静默加载。迁移器目录与只写新文件的迁移入口为：

```text
migrate list
migrate snapshot saves/old.json saves/migrated.json
```

源文件与已存在目标文件永不被迁移命令覆盖；当前没有已发布的跨版本迁移器时，`migrate list` 会明确显示为空。

也可以无交互执行完整送信路径：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "go place.luoyang.sili_office walk" `
  --command "give item.sealed_note_to_yuan_shao to person.yuan_shao" `
  --command "go place.luoyang.general_in_chief_office walk" `
  --command "tell person.li_wen proposition.general_office_requests_status" `
  --command "history"
```

封记可以拆封阅读；这会让玩家获得文书中的命题，但物理交付不会被阻止。交付和回报会以 `completed_with_breach` 终结，并保留“拆封 -> 交付违约 -> 回报发现”的原因链：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "read item.sealed_note_to_yuan_shao" `
  --command "go place.luoyang.sili_office walk" `
  --command "give item.sealed_note_to_yuan_shao to person.yuan_shao"
```

承诺会在各自截止分钟自动终结为 `missed`，而不是永久停留在开放状态。失约事件记录当时的债务人位置、接收人位置、目标物品持有人或前置承诺状态，但不会让远方 NPC 自动获知结果：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "wait 360" `
  --command "commitments" `
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

也可复现“袁绍在玩家到署前离开，门吏拒绝代收”：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "dev relocate 10 person.yuan_shao place.luoyang.henan_office" `
  --command "go place.luoyang.sili_office walk" `
  --command "give item.sealed_note_to_yuan_shao to person.yuan_shao" `
  --command "commitments"
```

旅行照常耗时 22 分钟，到署后的交付尝试再耗时 5 分钟；CLI 明确显示门吏拒绝代收，封记仍由玩家持有，两项承诺保持开放。

`dev schedule` 是显式外部干预，会写入事件日志并把回放标记为 `modified`。`dev rng` 只预览流副本，不改变模拟状态。

使用 `beliefs person.wang_yun` 和 `plans person.wang_yun` 可以查看王允等待正式报告时的运行时认知与计划状态：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "beliefs person.wang_yun" `
  --command "plans person.wang_yun"
```

门禁和消息谱系也可以直接从 CLI 验证：非相邻进入不会耗时，面对面陈述只更新实际接收者的信念。

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "enter place.luoyang.changle_palace" `
  --command "tell person.li_wen proposition.palace_credential_required" `
  --command "messages person.player_clerk" `
  --command "beliefs person.li_wen"
```

地点访问状态可以单独查询；支持排队的地点只让具备资格、但因临时关闭或同分钟受理容量竞争而受阻的人进入持久队列：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "access place.luoyang.north_gate"
```

计划可以声明独占资源、优先级和低优先级替换权；CLI 会显示当前锁，也可以显式取消计划并释放其行动和资源：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "plans person.wang_yun" `
  --command "plan cancel plan.wang_yun.review_gate_security priority_changed"
```

也可以逐步运行一段可中断旅行：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "travel start place.luoyang.eastern_road horse" `
  --command "dev interrupt-travel action.00000001 40 horse_injured" `
  --command "advance action.00000001" `
  --command "actions" `
  --command "resume action.00000001 walk" `
  --command "history"
```

`travel start` 只创建行动和第一项路段事件，因此可以在 `advance` 前或中断后保存。`resume` 保留已行进时间和路段进度，并按新的交通方式计算剩余路程。

可以直接观察 L3 群体，升格一个临时人物并检查其稳定身份：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "groups" `
  --command "dev promote group.market_population l0" `
  --command "detail person.promoted.00000001" `
  --command "dev demote person.promoted.00000001"
```

缩小版 B1-B4 和确定性交付基准可用 `all` 一次运行：

```powershell
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- all
```

B1 目标空闲世界、B2 目标人口核心、B2 混合城市危机、B3 目标消息拓扑、B3 冲突语义与 B4 目标交互使用独立入口；每项执行 1 次热身和 5 次全新世界样本，`scale` 顺序运行六项：

```powershell
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b1-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b2-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b2-mixed
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b3-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b3-conflict
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b4-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- scale
```

B5 是独立的磁盘 I/O 负载，不并入五样本 `scale`：

```powershell
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b5-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b6-scale
```

这些入口已经覆盖 B1-B6 的目标语义：B1 验证完整精度层级的事件驱动推进；B2 验证混合城市危机；B3 验证消息拓扑与冲突认知；B4 验证多群体升降格交互；B5 验证 100 万事件长期归档；B6 验证 20 个七日分支共享只读基础，同时隔离可变状态、事件尾与随机流。P6 规模架构验证已闭合，下一步进入 P7 雒阳四日可玩整合。

精度策略可以按玩家注意空间与人物因果负债显式重平衡：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "dev rebalance-detail" `
  --command "detail person.player_clerk" `
  --command "detail person.chen_zhi"
```

开始行动后可以检查增量脏集，只刷新真正受影响的人物：

```powershell
dotnet run --project src/LateHan.Cli/LateHan.Cli.csproj --configuration Release -- `
  --command "dev rebalance-detail" `
  --command "travel start place.luoyang.west_market walk" `
  --command "dev detail-dirty" `
  --command "dev rebalance-detail dirty"
```

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
