# ARCHIVED — Phase 2B Messenger Boundary

状态：✅ **PASS**  
归档日期：2026-09-21  
最终执行报告：`docs/PHASE2B_EXECUTION_REPORT.md`  
最终 CI：commit `a708d96eb806a9f27b60e325c6a174707308c861` / run `35578962686` / attempt 2 / conclusion=`success`

> 本文件为历史任务归档，不得作为当前执行入口。当前任务始终以 `tasks/CURRENT_TASK.md` 为准。

---

# MomoPet Phase 2B：Messenger Boundary

状态：CURRENT / AUTHORIZED

目标仓库：`guogracey0-png/MomoPet`

前置基线：
- Phase 0：PASS / ARCHIVED
- Phase 1：PASS / ARCHIVED
- Phase 2A：PASS / ARCHIVED
- Phase 2A 最终 CI：`dbb2cf6` / run `35575677386` / success
- 正式 CI 门禁：`windows-build`

## 1. 任务定位

当前 `src/Messenger.cs` 是大型 `partial PetController`，同时承载：
- Messenger DTO / Models
- Messenger UI 与会话视图
- 云端 API 请求
- Bearer Token 使用
- 文件/文件夹上传与临时 ZIP
- 联系人 / 群组 / 私信状态
- 轮询与回执
- Courier 送信动画
- Receipt 收信回执动画
- 消息发送 / delivered / read 状态

本阶段继续采用：

> **Move-first / Refactor-later**

目标是把职责按稳定边界拆成多个 partial 文件，保持 API、Token、消息 schema、轮询语义、附件行为和 UI 完全不变。

本轮不是 Messenger 重写，也不是安全/协议升级。

## 2. 明确禁止

本阶段禁止：
- 修改云端 Base URL
- 修改任何 `/api/momo/*` endpoint
- 修改 HTTP method
- 修改 Authorization / Bearer Token 语义
- 修改 Token 的 DPAPI 存储方式
- 修改消息 / 群组 /附件 JSON schema
- 修改消息最大字数
- 修改附件数量限制
- 修改附件扩展名策略
- 修改文件大小策略
- 修改 multipart 上传格式
- 修改轮询间隔
- 修改 delivered/read 回执语义
- 修改 sender/receiver 状态逻辑
- 修改 UI 文案 / 布局 / 动画 /交互
- 修改 Courier / Receipt 动画行为
- 修改群组业务逻辑
- 引入 HttpClient 重写
- 引入 WebSocket
- 引入分页
- 引入 retry/backoff 新策略
- 引入新的 rate limit
- 修改云服务端代码
- 修改 Messenger 以外业务
- 提前进入 Compliance / PetController 拆分

发现任何安全、性能、协议问题，只登记，不顺手修。

## 3. 必须保持不变的兼容面

### API
保持现有实际使用的全部 endpoint、method、request/response 字段不变，包括但不限于：
- members
- groups
- group messages
- conversation
- inbox unread
- messages send
- delivered
- read
- profile
- files upload

### 身份与 Secret
- momoToken 使用方式不变
- Bearer header 不变
- 现有账号 / Token 存储与 DPAPI 行为不变
- Secret 不进入日志、repo、artifact 文本

### 文件与附件
- multipart/form-data 行为不变
- 文件名处理不变
- MIME 推断不变
- 文件夹 ZIP 行为不变
- 临时文件清理不变
- pendingAttachments 生命周期不变
- 当前数量 / 大小 /类型策略不变

### 消息
- 私信 / 群组消息语义不变
- send / delivered / read 状态不变
- 轮询行为不变
- courier queue / receipt queue 不变
- awaitingReceipts / knownLetterStatus 行为不变

### UI
- 联系人 / 群组 / conversation UI 不变
- Composer 行为不变
- Ctrl+V / drag-drop 文件行为不变
- Courier / Receipt 动画不变
- 用户可见文案不变

## 4. P2B-01：建立 Messenger 文件边界

建议目标结构：

```text
src/
  Messenger.Models.cs
  Messenger.State.cs
  Messenger.Api.cs
  Messenger.Attachments.cs
  Messenger.Conversations.cs
  Messenger.Groups.cs
  Messenger.Ui.cs
  Messenger.Courier.cs
```

允许根据真实依赖微调，但要求：
- 至少拆成 **6 个职责文件**
- 禁止 Part1 / Part2 / Part3
- `PetController` 继续 partial
- 不为了美观批量改签名
- 不要求建立 Service / DI

## 5. P2B-02：Models 独立

将纯 DTO / data types 移到 Models 文件，例如实际存在的：
- `MomoRemoteMember`
- `MomoAttachment`
- `MomoPreparedUpload`
- `MomoLetter`
- `MomoGroup`
- `MomoGroupMessage`

要求：
- 属性名不变
- 可见性不主动改变
- ToString 行为不变
- JSON 字段不变
- 默认行为不变

## 6. P2B-03：State 边界

将 Messenger 专属字段集中到 State 文件，包括：
- windows / controls
- selected member/group
- members/letters/groups/groupMessages
- pending attachments
- request/send busy 状态
- poll timer
- courier / receipt 状态与 queues

要求：
- 只搬 Messenger 相关字段
- 不把 PetController 其他模块状态搬入
- 初始化顺序不改变

## 7. P2B-04：API 边界

将以下职责移动到 Api 文件：
- `UiPost`
- `MomoApi<T>`
- 与远端 Messenger API 请求直接相关的通用方法
- delivered / read / profile 等轻量请求可按职责放 Api 或 Conversation/Courier

要求：
- `HttpWebRequest` 保持
- timeout 保持
- header 保持
- JSON serializer 保持
- error message 行为保持
- UI callback 调度方式保持
- 不替换 HttpClient
- 不新增 retry/backoff

## 8. P2B-05：Attachments 边界

集中：
- `UploadMomoFiles`
- MIME 推断
- attachment icon / file size
- drag/drop / clipboard file input
- `PrepareMomoUpload`
- 临时 ZIP
- temp cleanup
- picker
- attachment chips

要求：
- 文件上传协议不变
- 数量限制不变
- MIME 映射不变
- temp 生命周期不变
- 当前安全策略不改；发现风险仅写报告

## 9. P2B-06：Conversation / Groups 边界

建议：
- `Messenger.Conversations.cs`：私信加载、刷新、发送、状态追踪、轮询
- `Messenger.Groups.cs`：群组加载、创建、群组消息加载/发送相关 UI 编排

如果依赖过强，可以合并，但职责必须清楚。

要求：
- requestId 竞争保护不变
- selection 行为不变
- TextSelection 保护不变
- 排序 / 时间显示不变
- message 状态行为不变
- polling 5 秒语义不变

## 10. P2B-07：UI 边界

将主 Messenger 面板构建和纯视图 helper 放到 Ui 文件。

至少包含实际存在的：
- Messenger 主窗口构建
- signed-out / conversation workspace
- conversation rendering
- UI helper 与附件 chip 展示

要求：
- UI 文案、布局、颜色、尺寸、事件绑定不变
- 不做任何 UX 重设计

## 11. P2B-08：Courier / Receipt 边界

集中：
- courier queue
- courier window
- courier animation
- receive letter
- delivered/read interaction
- receipt queue
- receipt window
- receipt animation
- preview courier

要求：
- 动画时长 /方向 /位置逻辑不变
- read/delivered API 时机不变
- pet interaction 不变
- 文案不变

## 12. P2B-09：构建同步

更新 `scripts/build.ps1`：
- 纳入所有新增 `Messenger*.cs`
- 不遗漏文件
- 保持 Phase 0/1/2A 构建机制不变
- 不改变 SDK / Node / build-meta 逻辑

## 13. P2B-10：结构回归测试

新增：

`test-messenger-boundary.ps1`

至少检查：
1. Messenger 拆为 >=6 个职责文件
2. 不存在 PartN 命名
3. DTO 只定义一次
4. partial PetController 结构正确
5. build source list 包含所有 Messenger 文件
6. 关键 endpoint 字符串仍存在且没有被意外改名
7. Bearer header / HttpWebRequest 仍存在
8. 5 秒 polling 配置仍存在
9. delivered/read endpoint 仍存在
10. 原 `Messenger.cs` 明显缩小

接入 `scripts/test.ps1`。

CI 必须继续运行：
- Compliance
- Office Comfort
- Engineering Guardrails
- ImageEditor Boundary
- Messenger Boundary

## 14. 规模与 Diff 验收

执行报告必须提供：
- 拆分前 `Messenger.cs` 行数 / 字节数
- 拆分后所有 `Messenger*.cs` 行数 / 字节数
- 文件职责表
- 最大单文件规模
- `git diff --stat`

以下原则上必须为 **0**：
- endpoint 变化
- request/response 字段变化
- Token / DPAPI 行为变化
- attachment 规则变化
- polling 变化
- UI 文案变化
- Courier / Receipt 行为变化
- 消息 schema 变化

若因编译必须存在非纯移动调整，逐项解释。

## 15. 已知问题只登记，不实施

执行侧应在报告中单独列出但不得修复：
- 附件安全 / 类型策略
- 上传大小 / abuse 风险
- URL metadata 信任边界
- 轮询效率
- conversation / inbox 分页
- session / server-side 生命周期
- HttpWebRequest 技术债
- Gitee / CI 等其他无关事项

是否处理由规划角色另开任务。

## 16. 验收标准

### Structure
- Messenger 至少 6 个职责文件
- Models / State / Api / Attachments / Conversation/Groups / Ui / Courier-Receipt 有可识别边界
- 无 PartN 切分
- 原巨型文件显著缩小

### Compatibility
- API URL / endpoint / method 不变
- JSON schema 不变
- Token / DPAPI 不变
- attachment 行为不变
- polling 语义不变
- delivered/read 语义不变
- UI /动画 /文案不变
- 业务行为不主动改变

### Tests
- Compliance PASS
- Office Comfort PASS
- Engineering Guardrails PASS
- ImageEditor Boundary PASS
- Messenger Boundary PASS
- OVERALL PASS

### CI
- `windows-build` success
- Build success
- Test success
- Artifact upload success

### Git
不得提交：
- Secret
- Token
- 用户数据
- `.agents`
- node_modules
- logs
- diagnostics 实际包
- 临时 ZIP /产物

## 17. 本阶段禁止继续抽 Service

即使拆分成功，本轮也不要创建：
- MessengerClient Service
- AccountStore
- MessageStore
- AttachmentService
- CourierService
- Repository 层
- DI / interface 大改

这些等 Phase 2B 验收后再由规划角色判断。

## 18. 执行报告

完成后新增：

`docs/PHASE2B_EXECUTION_REPORT.md`

必须包含：
1. 修改文件列表
2. 每个文件职责
3. 拆分前后规模
4. `git diff --stat`
5. 非纯移动逻辑修改
6. endpoint / Token / DPAPI / schema / attachment / polling / UI 兼容性核对
7. 全量测试
8. 真实 CI run ID / commit / conclusion
9. artifact
10. 未完成项
11. 风险与已登记问题
12. 后续可抽 Service 候选（只分析，不实现）
13. 是否满足全部验收标准
14. 需要规划角色判定的事项

不得自行进入 Phase 2C。

## 19. 执行指令

1. 同步最新 main
2. 阅读 `AGENTS.md`
3. 阅读 `tasks/CURRENT_TASK.md`
4. 完整阅读本文档
5. 先盘点 Messenger.cs 职责
6. 按 Move-first 拆分
7. 更新 build source list
8. 新增 Messenger Boundary 结构测试
9. 运行全部测试
10. push 并等待真实 GitHub Actions
11. 生成并提交 `docs/PHASE2B_EXECUTION_REPORT.md`
12. 等待规划角色验收

禁止自行扩展到 Phase 2C。

