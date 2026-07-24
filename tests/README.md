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

- `LateHan.Tests`：73 项场景加载、规范化哈希、持久旅行、多次玩家中断、门禁、竞争访问队列、计划资源锁定/替换/取消、同刻权限变化、消息谱系、版本化转述失真、冲突认知、远方批处理、随机流、精度策略、增量脏集、群体升降格守恒和快照续跑测试；
- `LateHan.Benchmarks`：`delivery`、B1-B4 缩小负载，以及 B1 目标空闲世界、B2 目标人口核心/混合城市危机、B3 目标消息拓扑/冲突语义和 B4 目标交互的无界面性能与正确性入口；目标数量达成不代表完整 B1-B6 语义和性能已经达标。

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

输出分别报告合成世界构造和模拟时间、p95、最大值、标准差、分配、样本工作集、GC、事件指纹与不变量。`b1-scale` 验证完整精度层级的事件驱动空闲推进；`b2-mixed` 另行聚合推进至玩家中断的延迟；`b3-scale`/`b3-conflict` 验证拓扑和冲突认知；`b4-scale` 验证多群体随机选择、跨地点回并和互动后保留。事件指纹审计单独输出。正式对比应分别启动六个目标负载，避免前一负载的 JIT 与进程内存影响后一项。
