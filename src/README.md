# 源代码目录

当前使用 C# / .NET 10，结构如下：

- `LateHan.Core`：纯模拟领域状态、行动、路径、稳定调度、确定性随机流、事件与指纹；
- `LateHan.Scenarios`：场景 JSON、校验、规范化哈希和领域构建；
- `LateHan.Persistence`：稳定快照 DTO 与原子文件持久化；
- `LateHan.Cli`：玩家命令和中文文本适配器。

当前 `wait` 已按事件边界推进并可精确中断；移动、交付和陈述仍是同步尖峰实现，下一步需要改造成可保存的持久行动阶段。

依赖方向只能指向核心。持久化、CLI 和未来图形引擎不得成为领域模型依赖。技术理由和复审条件见 [ADR-0003](../docs/decisions/0003-use-dotnet-for-simulation-core.md)。
