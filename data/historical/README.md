# 历史主张数据

本目录保存经过审核的结构化研究数据，而不是一个假装绝对正确的“历史真相表”。

- `sources.json`：来源登记。
- `claims/`：允许冲突的历史主张。
- `calendar-mappings/`：从原始纪年到不同西历口径的派生映射。

每条主张至少需要：

```yaml
id: claim-unique-id
subject: 主体实体 ID
predicate: 关系或属性
value: 值或值域
valid_time: 该主张对应的历史时间
source_ids: [source-id]
confidence: A | B | C | D
status: asserted | disputed | inferred | gameplay_assumption
notes: 解释、冲突或推导过程
```

正式 schema 会在世界模型和首个切片稳定后加入，避免过早锁死字段。
