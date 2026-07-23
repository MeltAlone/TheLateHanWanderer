# 架构决策记录

本目录保存 Architecture Decision Records（ADR）。只有满足以下条件的决定需要 ADR：

- 会长期约束多个模块；
- 存在合理替代方案；
- 日后仅看代码不容易理解选择理由；
- 撤销决定会产生显著迁移成本。

每份 ADR 使用 `NNNN-short-title.md` 命名，并包含状态、背景、决定、理由、后果和复审条件。

当前决定：

- [ADR-0001：分离史料主张、场景状态与模拟规则](0001-separate-research-scenario-and-simulation.md)
- [ADR-0002：使用玩家定步的事件驱动世界时间](0002-use-player-paced-event-driven-time.md)
- [ADR-0003：使用 C# / .NET 10 构建模拟核心](0003-use-dotnet-for-simulation-core.md)
