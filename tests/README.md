# 测试目录

测试将覆盖四类风险：

1. **规则正确性**：时间、旅行、资源、权限和行动前置条件。
2. **确定性**：相同场景、种子和命令产生相同事件序列。
3. **世界不变量**：资源守恒、无瞬移、死者不行动、认知不越权。
4. **涌现质量**：长期模拟不会普遍停滞、崩溃或陷入单一循环。

历史真实性不是普通单元测试能完全证明的，但可以将已确认的年代、地点、官职和不可能条件固化为数据约束测试。

当前规格：

- [189 雒阳危机 MVP 行为验收](specs/189-luoyang-crisis.md)
- [世界规模与性能预算](performance/world-scale-budgets.md)

当前可执行项目：

- `LateHan.Tests`：76 项场景、行动、访问、计划、消息、远方批处理、精度、快照，以及长期事件归档追加/重开/因果/恢复/审计测试；
- `LateHan.Benchmarks`：`delivery`、B1-B4 缩小负载、B1-B4 目标结构，以及 B5 百万事件长期归档的无界面性能与正确性入口。

日常尖峰可运行不含目标规模负载的 `all`：

```powershell
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- all
```

目标结构负载独立运行 1 次热身和 5 次样本；`scale` 会顺序执行六项：

```powershell
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b1-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b2-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b2-mixed
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b3-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b3-conflict
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b4-scale
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- scale
```

`b5-scale` 单独流式生成 100 万事件并使用临时 SQLite 归档，不并入以上五样本组合：

```powershell
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b5-scale
```

输出分别报告合成世界构造和模拟时间、p95、最大值、标准差、分配、样本工作集、GC、事件指纹与不变量。`b1-scale` 验证完整精度层级的事件驱动空闲推进；`b2-mixed` 聚合玩家中断延迟；`b3-scale`/`b3-conflict` 验证拓扑和冲突认知；`b4-scale` 验证升降格交互；`b5-scale` 报告追加、直接查询、有限 `why`、检查点恢复、全量审计和压缩备份。
