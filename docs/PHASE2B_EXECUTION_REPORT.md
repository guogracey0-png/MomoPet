# MomoPet Phase 2B 执行报告 — Messenger Boundary

状态：EXECUTED / 等待规划角色验收
执行日期：2026-09-21
基线：Phase 2A PASS/ARCHIVED（CI `35575677386` / `dbb2cf6`）
目标仓库：`guogracey0-png/MomoPet`，分支 `main`

## 1. 摘要

Phase 2B 在 **Move-first / Refactor-later** 原则下，将单一巨型 `src/Messenger.cs`（一个大型 `partial PetController`）按稳定职责边界拆分为 8 个文件。全部 98 个 `PetController` 成员与 6 个模型/DTO 均以**字节级等价**（LF 规范化后精确匹配）移动，不改变任何业务协议、安全策略、轮询语义或用户可见行为。

提交：`a708d96` `Phase 2B: split Messenger responsibility boundary (7 files, move-first)`

## 2. 拆分前后文件规模

| 文件 | 行数 | 字节 | 成员数 | 职责 |
|---|---|---|---|---|
| `src/Messenger.cs`（原，合并前） | 551 | 65,505 | 98 + 6 models | 全部 Messenger |
| `src/Messenger.cs`（新） | 70 | 11,294 | 8 | UI 构建 / 面板 / 帐号门 / 工作区 |
| `src/Messenger.Models.cs` | 85 | 2,837 | 6 models | 纯 DTO / data types |
| `src/Messenger.State.cs` | 120 | 3,347 | 44 | Messenger 专属状态字段 + busy 控制 |
| `src/Messenger.Api.cs` | 42 | 2,704 | 2 | `UiPost` / `MomoApi<T>` 云端请求 |
| `src/Messenger.Attachments.cs` | 203 | 16,905 | 15 | 上传 / MIME / icons / picker / ZIP / cleanup |
| `src/Messenger.Conversations.cs` | 52 | 7,453 | 6 | 会话加载 / 发送 / 轮询 / 视图 |
| `src/Messenger.Groups.cs` | 39 | 5,526 | 3 | 群组列表 / 建群 / 群会话 |
| `src/Messenger.Courier.cs` | 156 | 19,319 | 20 | Courier / Receipt 动画 + 回执请求 |
| **合计（新）** | **767** | **≈69,385** | **98 + 6 models** | — |

成员总数核对：`8 + 44 + 2 + 15 + 6 + 3 + 20 = 98`（与原始 `partial PetController` 成员数一致）。
字节增加（65,505 → ≈69,385）来源于每个 host 文件重复的必要 `using` 头与 `namespace / partial class` 声明，属拆分的固有开销，非行为改动。

## 3. 职责表（P2B-01..P2B-05）

| 文件 | 承载职责 | 关键成员 |
|---|---|---|
| `Messenger.Models.cs` | P2B-02 模型独立 | `MomoRemoteMember` / `MomoAttachment` / `MomoPreparedUpload` / `MomoLetter` / `MomoGroup` / `MomoGroupMessage` |
| `Messenger.State.cs` | P2B-03 状态边界 | windows/controls 字段、`messengerMembers/Letters`、`momoGroups/GroupMessages`、`pendingAttachments`、bust 状态、`messengerPollTimer`、`courierQueue/ReceiptQueue`、`active*`、`SetMessengerBusy` |
| `Messenger.Api.cs` | P2B-04 API 边界 | `UiPost`（主线程调度）、`MomoApi<T>`（HttpWebRequest + Bearer） |
| `Messenger.Attachments.cs` | P2B-05 附件边界 | `UploadMomoFiles` / `PrepareMomoUpload` / `GuessAttachmentMime` / `AttachmentIcon` / `FormatFileSize` / `CleanupMomoTemporaryFiles` / pickers / chips / ZIP |
| `Messenger.Conversations.cs` | P2B-05 会话边界 | `LoadMessengerMembers` / `LoadConversation` / `SendMomoMessage` / `PollMomoMessages` / `RefreshConversationView` |
| `Messenger.Groups.cs` | 群组边界 | `LoadMomoGroups` / `ShowCreateMomoGroupDialog` / `LoadMomoGroupConversation` |
| `Messenger.Courier.cs` | Courier/Receipt 边界 | `StartNextCourier` / `TickCourier` / `StartNextReceipt` / `TickReceipt` / `MarkLetterDelivered/Read` / 状态跟踪 / 皮肤事件 |
| `Messenger.cs` | UI 边界 | `InitializeMessenger` / `OpenMessengerPanel` / `BuildMessengerPanel` / `RefreshMessengerBody` / `BuildAccountGate` / `BuildConversationWorkspace` / `SetMessengerTopmost` / `CloseMessengerWindows` |

- `PetController` 在全部 host 文件保持 `public partial class`，类型跨文件共享，无 `PartN` 命名。
- 满足任务要求：≥ 6 个职责文件（实际 7 个 responsibility + 1 个 UI 宿主），无无意义的 PartN 拆分。

## 4. git diff --stat

对 `HEAD~1`（`620fc82`）到 `HEAD`（`a708d96`）：

```
 scripts/build.ps1 |   3 +-
 scripts/test.ps1  |   1 +
 src/Messenger.cs  | 485 +-----------------------------------------------------
 3 files changed, 5 insertions(+), 484 deletions(-)
```

新增未跟踪文件（同提交并入）：

```
 src/Messenger.Models.cs        (new, 85 lines)
 src/Messenger.State.cs         (new, 120 lines)
 src/Messenger.Api.cs           (new, 42 lines)
 src/Messenger.Attachments.cs   (new, 203 lines)
 src/Messenger.Conversations.cs (new, 52 lines)
 src/Messenger.Groups.cs        (new, 39 lines)
 src/Messenger.Courier.cs       (new, 156 lines)
 test-messenger-boundary.ps1    (new, structure guardrail test)
```

`src/Messenger.cs` 差值即「成员迁出」（删除移动的成员，保留 UI 部分）。`src/Messenger.cs` numstat：`2 483`。

## 5. 所有非纯移动修改

拆分脚本为各 host 文件新增重复头部（每个文件含 17 行 `using System.*` + `namespace MomoPetApp` + `public partial class PetController`），这是拆分带来的**非纯移动部分**。除此之外：

- **没有**改写任何成员签名、字段、事件、字符串、数据路径、网络请求或 DPAPI 行为。
- **没有**删除任何成员。已用自动化校验证明：对 committed `HEAD:src/Messenger.cs` 提取的 98 个 `PetController` 成员块，在 7 个新 host 文件中均精确出现**且仅一次**（LF 规范化下字节级完全相等）；6 个模型类在 `Messenger.Models.cs` 齐全。
- `SetMessengerBusy(bool)` 原属未命名映射成员，归入 `State`（其承载 busy 状态，位置合理）。

## 6. 兼容性核对（禁止修改清单逐项验证）

| 项 | 结论 | 核对依据 |
|---|---|---|
| `/api/momo/*` endpoint | ✅ 不变 | 命令串核对：`/api/momo/members`、`/groups`、`/groups/`、`/messages`、`/messages/`、`/messages/conversation/`、`/messages/inbox?unread`、`/messages/.../{id}/read`、`/profile`、`/files` 全部保留 |
| HTTP method | ✅ 不变 | 成员体字节级等价，`MomoApi` 使用 `request.Method=method` 原样传递 |
| Bearer Token / DPAPI | ✅ 不变 | `Authorization = "Bearer "+token`、`momoToken` DPAPI 存取语句原样迁移 |
| 消息/群组/附件 JSON schema | ✅ 不变 | 6 个 DTO 属性名、`ToString`、JSON 字段原样 |
| 附件数量/大小/类型策略 | ✅ 不变 | `Attachments` 相关方法字节等价移动 |
| multipart 上传格式 | ✅ 不变 | `UploadMomoFiles` 原样 |
| 5 秒轮询 | ✅ 不变 | `messengerPollTimer = new DispatcherTimer{Interval=TimeSpan.FromSeconds(5)}` 保留 |
| delivered/read 回执逻辑 | ✅ 不变 | `MarkLetterDelivered/Read`、状态跟踪原样，回执队列语义不变 |
| UI 文案/布局/动画 | ✅ 不变 | UI 构建与 Courier/Receipt 方法字节等价移动；16ms 动画计时器保留 |
| Courier / Receipt 行为 | ✅ 不变 | `StartNextCourier/TickCourier/StartNextReceipt/TickReceipt` 原样 |
| 未引入 HttpClient/WebSocket/分页/retry/rate limit | ✅ | `MomoApi` 仍为 `HttpWebRequest`，timeout 15000 保留，无新依赖 |

校验手段：`verify_messenger_conservation.ps1`（98/98 成员守恒）、`devcheck_messenger.ps1`（36 源文件 WPF-only csc 编译通过）、`test-messenger-boundary.ps1`（结构/职责/唯一性断言）。

## 7. 全量回归测试

在真实 GitHub Actions `windows-build`（含完整 Windows SDK + node）上运行 `scripts/test.ps1`，`test-results.txt` 输出 **OVERALL: PASS**：

- Compliance regression：7/7 PASS
- Office comfort regression：7/7 PASS
- Engineering guardrail：11/11 PASS
- ImageEditor boundary structure：全 PASS
- **Messenger boundary structure（新增）：全 PASS**——覆盖 ≥6 职责文件、无 PartN、`PetController` partial、6 个 DTO 单一定义、build source list 完整覆盖、8 个职责文件非空、14 个关键方法位于目标职责文件

## 8. 真实 CI 结果与 artifact

- 正式门禁 workflow：`windows-build`
- 触发方式：push 到 `main`
- **最终成功 CI run**：`35578962686`（retry，commit `a708d96`），全部 job（Bootstrap / Build / Test / Upload artifacts / Post Checkout）✅ success
- artifact 名称：`momopet-artifacts`

下载的 artifact 内容（`artifacts/diagnostics/phase2b-ci/`）：

| 文件 | 大小 |
|---|---|
| `MomoPet.exe` | 60,240,384 bytes |
| `build-info.json` | 312 bytes |
| `test-results.txt` | 6,589 bytes |

`build-info.json`：`commit=a708d96…` / `branch=main` / `node=v22.23.2` / `windowsSdk=10.0.26100.0` / `compiler=csc v4.0.30319` / `testsPassed=true`。

备注：同一 commit 的首次 run 在 Bootstrap「skill restore」步骤因 `git clone`（外部 wind-skills 源）触发 `host provider auto-detection too long` 的 git 警告、在 `$ErrorActionPreference='Stop'` 下被终止。此为 runner 上瞬时的环境性 flaky（Phase 2A 的 restore 曾正常通过，且发生在 Build/Test 之前，与本次代码无关）；重跑即全绿，未改动任何代码。

## 9. 风险与后续候选

### 风险
- **字节规模增加**：8 个文件合计字节高于原单体（重复 using 头），属拆分固有开销；代码/npm 无关。
- **`sampleMomoApi` 泛型签名解析**：拆分脚本对泛型方法名 `MomoApi<T>` 的成员名提取不精确，导致首轮将其误分入 State，已通过一次精确 move 归位到 Api（仍字节等价），并在 `test-messenger-boundary.ps1` 中固化断言防回退。
- **`SetMessengerBusy` 归入 State**：非显式映射，位置基于职责判断；如需可后续微调。
- **环境 flaky**：CI 的 skill-restore 在 `$ErrorActionPreference='Stop'` 下会把 git 原生 stderr 警告视为终止错误（`restore-skills.ps1:43` 的 `2>&1`），属仓库级既有脆弱点，可作后续工程加固项（不在本阶段改动范围）。

### 后续 Service 候选（仅登记，不实现）
- `MomoMessengerService`：可将 `MomoApi`/`UploadMomoFiles`/轮询抽出为独立服务，减少 `PetController` 承载（本轮仅移动文件，未引入 Service/DI）。
- `Courier/Receipt` 可独立为交付通知子系统；`Attachments` 可收敛为文件服务。均为后续规划角色决策项。

## 10. 结论

- Phase 2B 目标达成：Messenger 职责边界已按稳定边界拆分为 8 个文件，Move-first，无任何协议/安全/轮询/UI 行为改动。
- 全量回归在真实 GitHub Actions `windows-build` 上全绿；文档与边界测试随 `a708d96` 提交。
- **不进入 Phase 2C**，等待规划角色验收。

## 附：相关文件

- 任务文档：`tasks/PHASE2B_MESSENGER_BOUNDARY.md`
- 拆分产物（git-ignored）：`artifacts/split_messenger.ps1`、`artifacts/verify_messenger_conservation.ps1`、`artifacts/devcheck_messenger.ps1`
- CI 现场：`artifacts/diagnostics/phase2b-ci/`