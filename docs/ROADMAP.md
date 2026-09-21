# MomoPet Engineering Roadmap

## 项目原则

当前项目已具备较完整产品能力，后续优化优先顺序为：

> 先建立可控性，再优化架构，最后扩展功能。

## Phase 0 — Engineering Baseline

当前状态：PASS / ARCHIVED

成果：
- 可重复依赖恢复
- 可重复构建
- 自动回归测试
- 构建产物可追踪
- CI 全链路验证
- 失败可诊断

归档任务：
tasks/completed/PHASE0_ENGINEERING_BASELINE.md

执行报告：
docs/PHASE0_EXECUTION_REPORT.md

最终 CI 基线：
dddc83b / run 35320912068 / success

## Phase 1 — Engineering Guardrails

当前状态：✅ PASS / ARCHIVED

归档任务：
tasks/completed/PHASE1_ENGINEERING_GUARDRAILS.md

执行报告：
docs/PHASE1_EXECUTION_REPORT.md

最终 CI：
d789c60 / run 35326969067 / success

目标：
- 统一日志体系
- 顶层错误与崩溃诊断
- 配置 / Secret / 状态边界
- 路径基础设施
- CI / PR 质量门禁
- 回归测试护栏
- 版本与诊断元数据
- 最小诊断包
- 供应链 / Release 后续专项登记

明确不包含：
- ImageEditor 重构
- PetController 拆分
- Messenger / Compliance 架构拆分
- UI 重做
- .NET 8 / Electron 迁移

## Phase 2 — Architecture Decomposition

当前状态：🟡 IN PROGRESS

### Phase 2A — ImageEditor Boundary

状态：CURRENT / AUTHORIZED

当前任务：
tasks/PHASE2A_IMAGE_EDITOR_BOUNDARY.md

策略：
先按职责拆 partial 文件，建立稳定边界；本轮不做 Service / MVVM 大改。

### Phase 2B — Messenger Boundary
状态：PLANNED / NOT AUTHORIZED

### Phase 2C — Compliance Boundary
状态：PLANNED / NOT AUTHORIZED

### Phase 2D — PetController Boundary
状态：PLANNED / NOT AUTHORIZED

总体优先顺序：
1. ImageEditor
2. Messenger
3. Compliance
4. PetController

原则：
- 渐进式拆分
- 不做大爆炸重写
- 每次拆分必须有回归保护
- 保持用户行为兼容

建议目标模块：
- App
- Pet
- Tasks
- Stash
- Ai
- Compliance
- Messenger
- Infrastructure
- Ui

核心原则：

> UI 不直接负责业务逻辑；业务逻辑不直接负责网络和存储。

## Phase 3 — Product Evolution

在工程基线和架构边界稳定后，再推进：
- 桌宠体验升级
- AI Agent / 工作流
- 办公 Copilot
- 知识库
- 云同步
- 插件生态
- 新 AI 模型能力
- UI/交互升级

## 后续专项

### Skill Supply Chain
- computedHash 正式规范
- deterministic directory hash
- sourceRef / commit pinning
- lock migration

### Release / Updater
- 版本策略
- Release artifacts
- 更新与回滚
- 运行中 EXE 替换
- portable / installer 策略

## 当前禁止的大动作

在 Phase 2 完成前，仍不建议：
- 全面重写
- Electron 迁移
- .NET 8 全量迁移
- 全面 MVVM 重构
- 一次性拆 PetController
- 大规模 UI 重做

## 协作模式

用户提出需求
→ 规划角色评估与拆任务
→ 更新 tasks/CURRENT_TASK.md
→ 本地执行软件 git pull
→ 读取 AGENTS.md + CURRENT_TASK
→ 执行 / build / test / CI
→ 提交执行报告与 diff
→ 规划角色 Review / 验收
→ 进入下一任务
