# CURRENT TASK

当前任务：**MomoPet Phase 2B — Messenger Boundary**

状态：🟡 **CURRENT / AUTHORIZED**

完整任务文档：

`tasks/PHASE2B_MESSENGER_BOUNDARY.md`

已归档阶段：
- Phase 0 — Engineering Baseline：✅ PASS
- Phase 1 — Engineering Guardrails：✅ PASS
- Phase 2A — ImageEditor Boundary：✅ PASS

Phase 2A 归档：
`tasks/completed/PHASE2A_IMAGE_EDITOR_BOUNDARY.md`

Phase 2A 报告：
`docs/PHASE2A_EXECUTION_REPORT.md`

## 执行顺序

1. 同步最新 `main`
2. 完整读取 `AGENTS.md`
3. 完整读取 `tasks/PHASE2B_MESSENGER_BOUNDARY.md`
4. 严格按 Move-first / Refactor-later 拆分 Messenger
5. 不修改协议、安全策略、轮询、UI 或消息行为
6. 不进入 Compliance / PetController 其他架构拆分
7. 完成 build / 全量 test / GitHub Actions CI
8. 生成 `docs/PHASE2B_EXECUTION_REPORT.md`
9. 等待规划角色验收

不得根据 ROADMAP 自行开始 Phase 2C。
