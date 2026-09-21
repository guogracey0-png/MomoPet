# MomoPet Phase 2C：Compliance Boundary

状态：CURRENT / AUTHORIZED

目标仓库：`guogracey0-png/MomoPet`

前置基线：
- Phase 0：PASS / ARCHIVED
- Phase 1：PASS / ARCHIVED
- Phase 2A：PASS / ARCHIVED
- Phase 2B：PASS / ARCHIVED
- Phase 2B 最终 CI：`a708d96` / run `35578962686` / success
- 正式 CI 门禁：`windows-build`

## 1. 任务定位

当前 `src/ComplianceUpgrade.cs` 同时承载：

- Compliance DTO / Models
- 规则文件加载与解析
- 本地规则扫描
- 否定语境 / 风险提示覆盖判断
- 命中位置定位
- AI 结构化复核 Prompt 与结果解析
- 人工处置
- Audit 记录
- Audit 持久化
- 内容 Hash
- 原文高亮
- HTML 报告导出
- 历史记录恢复
- 主审核 UI

本阶段继续采用：

> **Move-first / Refactor-later**

目标：先按职责拆成稳定 partial 边界，**完全保持现有合规判断逻辑与输出结果不变**。

本轮不是规则优化，也不是合规算法升级。

## 2. 最高优先级禁改项

以下任何一项本阶段都禁止修改：

- `compliance-rules.txt` 内容
- Rule ID
- Rulebook version 解析
- 规则 Term / Category / Severity / Reason / Suggestion / RequiredWarning
- `CompliancePromptVersion`
- AI Prompt 文本
- keep / dismiss / covered 语义
- AI confidence 阈值
- 本地命中算法
- 否定/解释语境判断
- disclosure anchor groups
- 风险提示覆盖判定
- 结论算法
- `通过（仍需人工复核）` / `有风险` / `不可使用` 逻辑
- Audit JSON schema
- Audit 文件名与保存路径
- Audit 最大保留数量
- SHA-256 内容 Hash 语义
- Provider / Model 选择行为
- AI request 字段 / wind_bridge 协议
- 人工处置行为
- 原文定位 / 高亮行为
- HTML 导出格式与免责声明
- UI 文案 / 布局 / 交互
- 历史记录恢复行为

如发现规则或算法问题，只登记，不顺手修。

## 3. 建议文件边界

建议结构：

```text
src/
  Compliance.Models.cs
  Compliance.State.cs
  Compliance.Rules.cs
  Compliance.Review.cs
  Compliance.Audit.cs
  Compliance.Rendering.cs
  Compliance.Export.cs
  Compliance.Ui.cs
```

允许根据依赖微调，但要求：

- 至少拆成 **6 个职责文件**
- 禁止 Part1 / Part2 / Part3
- `PetController` 继续 partial
- 不引入 Service / Repository / DI
- 不批量改签名
- 不大面积格式化

## 4. P2C-01：Models 独立

移动纯模型：

- `ComplianceRuleData`
- `ComplianceFindingData`
- `ComplianceAuditRecord`
- `ComplianceModelReview`
- `ComplianceModelReviewItem`

要求：

- 属性名不变
- 可见性不主动改变
- JSON 字段不变
- 大小写不变
- 默认序列化行为不变

## 5. P2C-02：State 边界

集中 Compliance 专属状态：

- windows / controls
- current findings
- covered findings
- audit list / current audit
- rule definitions
- mark runs
- source / image / audit path
- rulebook version

要求：

- 只移动 Compliance 状态
- 不把 ImageEditor / Messenger / 其他 PetController 状态搬入
- 初始化顺序不变

## 6. P2C-03：Rules / Local Scan 边界

集中：

- `ComplianceRules`
- `LoadComplianceRuleDefinitions`
- punctuation / normalize
- phrase location
- negation / explanatory use
- disclosure anchor groups
- required disclosure coverage
- `ScanComplianceRules`
- 本地 scan 相关 helper

要求：

- 正则表达式逐字保持
- Rule ID 生成保持
- section 解析保持
- severity / category 保持
- coverage 行为保持
- 排序保持
- 不新增规则
- 不删除规则

## 7. P2C-04：AI Review 边界

集中：

- `ComplianceInstruction`
- Prompt version
- context helper
- structured review apply / merge
- model review request 编排
- `RunComplianceAudit`
- ProviderHost 等直接相关 helper

要求：

- Prompt 文本逐字保持
- request body 字段不变
- provider routing 不变
- model 行为不变
- fallback parser 行为不变
- confidence 门槛不变
- 不修改 wind_bridge

## 8. P2C-05：Audit 边界

集中：

- audit load/save
- `ComplianceHash`
- conclusion
- clone findings
- sync current audit
- audit list 管理

要求：

- 文件名 `compliance-audits.json` 不变
- temp 写入 / copy / delete 行为不变
- .bak 行为不变
- audit JSON schema 不变
- SHA-256 输入语义不变
- 最大 500 条行为不变

## 9. P2C-06：Rendering / Source Location 边界

集中：

- finding selection
- phrase source location
- original text render
- severity brush
- mark run / highlight
- focus selected finding
- disposition UI 同步逻辑

要求：

- Start / Length 语义不变
- fallback locate 行为不变
- overlap 处理不变
- highlight 颜色不变
- 点击定位行为不变
- disposition 值不变

## 10. P2C-07：Export / History 边界

可按依赖拆成一个或两个文件。

包含：

- HTML encode helper
- `HighlightedComplianceHtml`
- `ExportComplianceReport`
- History window
- Audit 恢复

要求：

- HTML 模板保持
- 文件名模式保持
- UTF-8 BOM 行为保持
- 免责声明保持
- 历史恢复字段保持

## 11. P2C-08：UI 边界

主 Compliance UI：

- `BuildCompliancePanel`
- `OpenComplianceReview`
- local/model tabs
- finding list UI
- summary/detail UI
- image/source UI
- panel layout helpers

要求：

- UI 文案、布局、尺寸、颜色、事件绑定不变
- 不做 UX 重设计

## 12. 构建同步

更新 `scripts/build.ps1`：

- 纳入所有新增 `Compliance*.cs`
- 不遗漏
- 不破坏 Phase 0/1/2A/2B 构建机制

## 13. Compliance Boundary 结构测试

新增：

`test-compliance-boundary.ps1`

至少检查：

1. Compliance >=6 个职责文件
2. 无 PartN
3. 5 个 DTO/Model 各定义一次
4. host 文件保持 `partial PetController`
5. build source list 覆盖全部 Compliance 文件
6. `CompliancePromptVersion` 仍存在且值未改
7. `compliance-audits.json` 文件名仍存在
8. `SHA256.Create()` 仍存在
9. 关键结论字符串仍存在
10. `ComplianceInstruction` / `ScanComplianceRules` / `RunComplianceAudit` 位于目标职责文件
11. 原巨型 `ComplianceUpgrade.cs` 显著缩小

接入 `scripts/test.ps1`。

CI 必须继续：

- Compliance regression
- Office comfort
- Engineering guardrail
- ImageEditor boundary
- Messenger boundary
- Compliance boundary

## 14. 规模与 Diff 验收

执行报告必须提供：

- 拆分前 `ComplianceUpgrade.cs` 行数 / 字节数
- 拆分后每个 Compliance 文件规模
- 职责表
- 最大单文件规模
- `git diff --stat`
- 成员守恒/代码移动验证方式

以下原则上必须为 **0**：

- 规则内容变化
- Prompt 变化
- Rule ID 变化
- regex / anchor 逻辑变化
- conclusion 变化
- AI request schema 变化
- Audit schema 变化
- Hash 变化
- HTML 模板变化
- UI 文案 /布局变化

若存在任何非纯移动逻辑调整，必须单独列出并解释。

## 15. 已知问题只登记，不实施

执行侧可登记但禁止本轮修：

- 规则误报/漏报
- AI reviewer Prompt 优化
- confidence 调优
- audit 数据量 / 查询性能
- audit schema 升级
- HTML 样式优化
- 更强的来源定位
- 规则版本机制升级
- 合规规则后台管理
- 其他业务合规建议

这些属于未来专项。

## 16. 验收标准

### Structure
- >=6 个职责文件
- Models / State / Rules / Review / Audit / Rendering-Export / Ui 边界清楚
- 无 PartN
- 原巨型文件显著缩小

### Behavior
- 本地扫描结果不主动变化
- AI review 输入输出协议不变
- Prompt 不变
- Audit 不变
- Hash 不变
- HTML 不变
- 人工处置不变
- UI 不变

### Tests
- Compliance regression PASS
- Office comfort PASS
- Engineering guardrail PASS
- ImageEditor boundary PASS
- Messenger boundary PASS
- Compliance boundary PASS
- OVERALL PASS

### CI
- `windows-build` success
- Build success
- Test success
- Artifact upload success

### Git
不得提交 Secret、用户审核数据、audit 实际数据、logs、diagnostics、临时产物。

## 17. 本阶段禁止继续抽 Service

即使拆分完成，也不要创建：

- ComplianceEngine
- RuleBook Service
- AiReviewer Service
- AuditStore
- Compliance Repository
- DI / interface 大改

是否抽取由 Phase 2C 验收后重新决定。

## 18. 执行报告

完成后新增：

`docs/PHASE2C_EXECUTION_REPORT.md`

必须包含：

1. 修改文件列表
2. 每个文件职责
3. 拆分前后规模
4. `git diff --stat`
5. 成员守恒 / move-only 验证
6. 非纯移动逻辑修改
7. Rule / Prompt / regex / AI schema / Audit / Hash / HTML / UI 兼容性核对
8. 全量测试
9. 真实 CI run ID / commit / conclusion
10. artifact
11. 风险与已登记问题
12. 后续 Service 候选（只分析，不实现）
13. 是否满足全部验收标准
14. 需要规划角色判定的事项

不得自行进入 Phase 2D。

## 19. 执行指令

1. 同步最新 main
2. 阅读 `AGENTS.md`
3. 阅读 `tasks/CURRENT_TASK.md`
4. 完整阅读本文档
5. 先盘点 ComplianceUpgrade.cs 的方法/状态/模型
6. 按 Move-first 拆分
7. 更新 build source list
8. 新增 Compliance Boundary 结构测试
9. 运行全部测试
10. push 并等待真实 GitHub Actions
11. 生成并提交 `docs/PHASE2C_EXECUTION_REPORT.md`
12. 等待规划角色验收

禁止自行扩展到 Phase 2D。
