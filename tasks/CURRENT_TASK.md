# CURRENT TASK

当前任务：**MomoPet Phase 2A — ImageEditor Boundary**

状态：🟡 **CURRENT / AUTHORIZED**

完整任务文档：

`tasks/PHASE2A_IMAGE_EDITOR_BOUNDARY.md`

已归档阶段：

- Phase 0 — Engineering Baseline：✅ PASS
- Phase 1 — Engineering Guardrails：✅ PASS

Phase 1 归档：

`tasks/completed/PHASE1_ENGINEERING_GUARDRAILS.md`

Phase 1 报告：

`docs/PHASE1_EXECUTION_REPORT.md`

## 执行顺序

1. 同步最新 `main`
2. 完整读取 `AGENTS.md`
3. 完整读取 `tasks/PHASE2A_IMAGE_EDITOR_BOUNDARY.md`
4. 严格按“移动优先、逻辑不变”拆分 ImageEditor
5. 不进入 Messenger / Compliance / PetController 其他架构拆分
6. 完成 build / 全量 test / GitHub Actions CI
7. 生成 `docs/PHASE2A_EXECUTION_REPORT.md`
8. 等待规划角色验收

不得根据 ROADMAP 自行开始 Phase 2B。
