# 源代码目录

当前使用 C# / .NET 10，结构如下：

- `LateHan.Core`：纯模拟领域状态、行动、路径、事件与确定性指纹；
- `LateHan.Scenarios`：场景 JSON、校验、规范化哈希和领域构建；
- `LateHan.Persistence`：稳定快照 DTO 与原子文件持久化；
- `LateHan.Cli`：玩家命令和中文文本适配器。

依赖方向只能指向核心。持久化、CLI 和未来图形引擎不得成为领域模型依赖。技术理由和复审条件见 [ADR-0003](../docs/decisions/0003-use-dotnet-for-simulation-core.md)。
