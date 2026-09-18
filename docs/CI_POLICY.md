# MomoPet CI 策略

状态：Phase 1（P1-05）。本文件把 `windows-build` 固化成本仓库后续一切修改的质量门禁，并明确“什么情况算通过 / 不能验收 / 允许外部阻塞”。

## 1. 门禁本体

工作流：`.github/workflows/windows-build.yml`

触发：`push` / `pull_request` / `workflow_dispatch`

阶段（任一步失败即 workflow failure）：

```text
Checkout
  → Bootstrap   （环境检查 + .agents\skills 依赖恢复；skills-lock.json）
  → Build       （OCR 助手 + EmbeddedRuntime + MomoPet → artifacts\MomoPet.exe + build-meta 嵌入 + build-info.json）
  → Test        （Compliance 7/7 + Office Comfort 7/7 + Engineering Guardrails）
  → Upload artifacts （仅在前序全部成功时执行；if-no-files-found: error）
```

规则：
- 任何 `bootstrap / build / test` 失败都会让该 job 失败（脚本返回非 0，工作流步骤 `exit LASTEXITCODE`）。
- **不引入 `continue-on-error`** 绕过关键步骤；失败必须如实呈现。
- artifact 只在成功构建后上传；无文件则上传步骤报错。
- 错误定位：每个阶段是独立命名步骤，日志可据此定位失败点；脚本统一输出 `[OK]/[FAIL]` 并写明 exit code。
- 顶层 `.gitignore` 挡住 `artifacts/`、`*.log`、`.agents/`、`node_modules/` 等，严禁上传诊断采集包、本机日志、临时构建文件。

## 2. 什么时候 CI 必须通过

- 任何将要合并到 `main` 的修改，其对应 workflow 必须为 **全绿（conclusion=success）**。
- 报告 `build PASS / test PASS / artifact PASS` 必须以**真实 CI run** 为准，不允许用“本机未跑但应该没问题”代替。
- 只有满足任务文档全部“验收标准”且 CI 全绿，才允许提请规划角色验收。

## 3. 什么情况下任务不能验收

- `Build` 或任一 `Test` 阶段失败。
- 测试脚本自身异常退出 / 解析失败（会被判定为 FAIL，见 test.ps1）。
- 用伪产物 / 跳过测试 / silent failure 假装通过。
- 用 `continue-on-error` 遮掩关键步骤失败。

## 4. 哪些失败允许认定为“外部阻塞”

可认定为外部阻塞的情形（仍需记录在案）：
- 目标外部依赖不可达且与本次改动无关（例如某 Skill git 源在 CI 网络不可达）。
- 运行环境缺少本次改动未涉及的、且无法在 CI 安装的组件。
- 上游服务/授权导致无法验证（如仓库保护规则禁止某项操作）。

不可认定为外部阻塞的情形：
- 本机缺少 Windows SDK 且拒绝走 CI —— 这不构成阻塞（CI 自带 SDK），见下节。
- 与本次改动直接相关的编译/测试失败。

## 5. 外部阻塞必须如何记录

- 在执行报告 `docs/*_EXECUTION_REPORT.md` 中写明：阻塞点、证据（CI run ID / commit / conclusion）、复现命令、影响哪些验收项。
- 阻塞项对应子项标记“未完成（阻塞）”，不得把整任务标记为通过。
- 若阻塞可改由 GitHub Actions（自带 Windows SDK）验证，默认走 CI 完成验证，只把本机缺失作为“记录”而非“阻塞结论”。

## 6. 分支保护

如仓库设置允许，可建议由规划角色为 `main` 开启 branch protection（要求 `windows-build` 通过）。执行侧**不得擅自修改仓库保护规则**（AGENTS.md / P1-05）。