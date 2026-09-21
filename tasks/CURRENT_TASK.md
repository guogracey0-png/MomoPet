# CURRENT TASK

当前任务：**MomoPet Phase 2C — Compliance Boundary**

状态：🟡 **CURRENT / AUTHORIZED**

完整任务文档：

`tasks/PHASE2C_COMPLIANCE_BOUNDARY.md`

已归档阶段：
- Phase 0 — Engineering Baseline：✅ PASS
- Phase 1 — Engineering Guardrails：✅ PASS
- Phase 2A — ImageEditor Boundary：✅ PASS
- Phase 2B — Messenger Boundary：✅ PASS

Phase 2B 归档：
`tasks/completed/PHASE2B_MESSENGER_BOUNDARY.md`

Phase 2B 报告：
`docs/PHASE2B_EXECUTION_REPORT.md`

## 执行顺序

1. 同步最新 `main`
2. 完整读取 `AGENTS.md`
3. 完整读取 `tasks/PHASE2C_COMPLIANCE_BOUNDARY.md`
4. 严格按 Move-first / Refactor-later 拆分 Compliance
5. 不修改规则、Prompt、判定、Audit、Hash、HTML、UI 行为
6. 不进入 PetController 其他架构拆分
7. 完成 build / 全量 test / GitHub Actions CI
8. 生成 `docs/PHASE2C_EXECUTION_REPORT.md`
9. 等待规划角色验收

不得根据 ROADMAP 自行开始 Phase 2D。
