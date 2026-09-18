# MomoPet Engineering Roadmap

## 项目原则

当前项目已具备较完整产品能力，后续优化优先顺序为：

> 先建立可控性，再优化架构，最后扩展功能。

## Phase 0 — Engineering Baseline

目标：
- 可重复依赖恢复
- 可重复构建
- 自动回归测试
- 构建产物可追踪
- CI 可验证
- 失败可诊断

当前状态：**CURRENT**

任务文档：
`tasks/PHASE0_ENGINEERING_BASELINE.md`

## Phase 1 — Engineering Guardrails

计划目标：
- 统一日志体系
- 统一配置管理
- Provider 配置边界
- CI/PR 质量门禁
- 测试覆盖扩充
- 发布与版本策略
- 错误与崩溃诊断标准化

## Phase 2 — Architecture Decomposition

计划优先顺序：

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

## 当前禁止的大动作

在 Phase 0 / Phase 1 完成前，不建议：
- 全面重写
- Electron 迁移
- .NET 8 全量迁移
- 全面 MVVM 重构
- 一次性拆 PetController
- 大规模 UI 重做

## 协作模式

```text
用户提出需求
↓
规划角色评估与拆任务
↓
更新 tasks/CURRENT_TASK.md
↓
本地执行软件 git pull
↓
读取 AGENTS.md + CURRENT_TASK
↓
执行 / build / test
↓
提交结果与 diff
↓
规划角色 Review / 验收
↓
进入下一任务
```
