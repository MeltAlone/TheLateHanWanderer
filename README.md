# 汉末浮生（The Late Han Wanderer）

一款以汉末为背景、以个人为观察与行动尺度的动态历史模拟游戏。

项目当前处于**首个可执行垂直切片的契约与数据阶段**。首要目标不是制作图形界面或堆叠剧情，而是建立一套能够自行运转、允许玩家介入、并能解释自身因果的世界规则。

## 当前里程碑

- 确立世界模拟总设计与“世界宪法”。
- 建立历史研究的来源、置信度与争议处理规范。
- 定义首个 189 年洛阳危机时空切片。
- 定义时间、空间、人物、组织、信息、行动与事件的数据边界。
- 已定义“雒阳四日”命令行 MVP、场景夹具、行为验收和规模预算。
- 下一步以同一契约实现并验证无界面的最小世界内核。

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
src/               模拟核心与表现适配器（尚未开工）
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
