# MomoPet Engineering Backlog（待办登记，本轮不实现）

状态：Phase 1（P1-09）。以下内容**仅登记**，供后续阶段实现；本轮禁止实现。
相关任务文档：`tasks/PHASE1_ENGINEERING_GUARDRAILS.md` 第 12 节。

---

## A. Skill 供应链完整性

当前 `skills-lock.json` 已含 `computedHash`（64 位 hex），但**本轮不把 “SHA-256 over SKILL.md” 固化为正式标准**。待办：

- [ ] **computedHash 正式算法**：明确规范化口径（原始字节 / LF 规范化、覆盖的文件集合、编码），作为正式标准文档化。
  - 说明：此前 `docs/PHASE0_EXECUTION_REPORT.md` 曾“记录已确认 sha256(SKILL.md)”，但经核对，锁内既有哈希与当前源 SKILL.md **不一致**（raw 与 LF 两种口径均不匹配）。因此正式算法与回填动作均未定，移入本 backlog 由规划角色裁决。
- [ ] **目录级 deterministic hash**：对整技能目录做确定性哈希（覆盖新增/删除文件），而非单文件。
- [ ] **sourceRef / commit SHA 固定**：锁文件锁定来源仓库的 commit SHA，避免“默认分支漂移导致校验失败”。
- [ ] **lock 文件迁移策略**：既有锁哈希与新标准不兼容时的迁移/回填规则（含本次发现的 4 个既有哈希全不匹配的处理）。
- [ ] **hash mismatch 失败策略**：恢复失败时是阻断构建、降级为警告，还是回退到上次成功缓存；需明确并写入 `docs/CI_POLICY.md` 或专属文档。

> `scripts/restore-skills.ps1` 当前对该项保持 WARNING + TODO（不伪造校验）。

## B. Release / Updater

- [ ] **版本命名**：SemVer / 日期混合，与 `AssemblyInformationalVersion`、`build-info.json` 对齐。
- [ ] **Release artifact**：把 `artifacts\MomoPet.exe` 发布为 GitHub Release 附件的标准流程。
- [ ] **时间戳备份**：对旧产物做带时间戳备份、保留最近 N 份（此前曾“记录已确认”，本轮**不实现**；与 `artifacts\MomoPet.exe = 最新成功构建` 的 Phase 1 约定并存，由规划角色定稿）。
- [ ] **正在运行 EXE 的更新**：绕开文件占用，安全替换正在运行的 EXE。
- [ ] **回滚**：保留可用的回退版本策略。
- [ ] **安装器 / portable 模式**：明确分发形态。
- [ ] **自动更新策略**：是否需要、以及“未经用户同意不得遥测上传”的约束。