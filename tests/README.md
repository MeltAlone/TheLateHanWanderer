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

- `LateHan.Tests`：88 项场景、行动、文书拆封/违约、承诺到期、事件重放、版本拒绝、访问、计划、消息、远方批处理、精度、快照、长期事件归档和分支隔离测试；
- `LateHan.Benchmarks`：`delivery`、B1-B4 缩小/目标结构、B5 百万事件归档和 B6 二十个七日分支的无界面性能与正确性入口。

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
dotnet run --project tests/LateHan.Benchmarks/LateHan.Benchmarks.csproj --configuration Release -- b6-scale
```

输出分别报告时间、分配、工作集、指纹与不变量。`b5-scale` 报告追加、查询、恢复、审计和压缩备份；`b6-scale` 报告基础/分支存储、每支实际尾事件数、20 个独立事件/物质指纹与随机游标，以及跨基础归档的 `why`。
