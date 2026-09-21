# CURRENT TASK

当前任务：**MomoPet Phase 2D — PetController Boundary**

状态：🟡 **CURRENT / AUTHORIZED**

完整任务文档：

`tasks/PHASE2D_PETCONTROLLER_BOUNDARY.md`

已归档阶段：
- Phase 0 — Engineering Baseline：✅ PASS
- Phase 1 — Engineering Guardrails：✅ PASS
- Phase 2A — ImageEditor Boundary：✅ PASS
- Phase 2B — Messenger Boundary：✅ PASS
- Phase 2C — Compliance Boundary：✅ PASS

Phase 2C 归档：
`tasks/completed/PHASE2C_COMPLIANCE_BOUNDARY.md`

Phase 2C 报告：
`docs/PHASE2C_EXECUTION_REPORT.md`

## 执行顺序

1. 同步最新 `main`
2. 完整读取 `AGENTS.md`
3. 完整读取 `tasks/PHASE2D_PETCONTROLLER_BOUNDARY.md`
4. 先建立 `MomoPet.cs` PetController 成员守恒基线
5. 严格按 Move-first / Refactor-later 拆分剩余主控制器职责
6. 不重复重构已独立的 ImageEditor / Messenger / Compliance 等模块
7. 不改变任务、stash、market、动画、生命周期或 UI 行为
8. 完成 build / 全量 test / GitHub Actions CI
9. 生成 `docs/PHASE2D_EXECUTION_REPORT.md`
10. 等待规划角色验收

不得根据 ROADMAP 自行进入 Phase 3。
