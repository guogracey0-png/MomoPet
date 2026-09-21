# ARCHIVED — Phase 1 Engineering Guardrails

状态：✅ **PASS**  
归档日期：2026-09-21  
最终执行报告：`docs/PHASE1_EXECUTION_REPORT.md`  
最终 CI：commit `d789c6023e4e14a7a6c12c34d29f5b34e92e4cdc` / run `35326969067` / conclusion=`success`

> 本文件为历史任务归档，不得作为当前执行入口。当前任务始终以 `tasks/CURRENT_TASK.md` 为准。

---

# MomoPet Phase 1：Engineering Guardrails

状态：CURRENT / AUTHORIZED

目标仓库：guogracey0-png/MomoPet

前置基线：
- Phase 0：PASS / ARCHIVED
- 最终 CI 基线：commit dddc83b80f49de21fdbd7ed7fd1ab4f77037b7ed
- GitHub Actions run：35320912068
- 后续修改不得破坏 Phase 0 的 bootstrap → build → test → artifact

## 1. 本阶段目标

Phase 1 不做产品功能开发，不做大规模架构拆分。

本阶段建立工程护栏，让后续修改具备：
1. 统一日志
2. 统一错误诊断
3. 配置与路径边界
4. CI / PR 质量门禁
5. 更可靠的回归测试入口
6. 版本与构建信息可追踪
7. 明确的后续供应链与发布工程待办

本阶段完成后，任何执行软件应能明确回答：改了什么、是否能构建、是否通过测试、失败在哪里、用户机器上如何定位问题、产物来自哪个 commit。

## 2. 允许修改范围

允许：
- src/ 中与日志、异常诊断、配置/路径基础设施直接相关的最小修改
- scripts/
- .github/workflows/
- docs/
- README.md
- 必要的测试脚本
- 必要的基础设施类

允许新增类似：
- src/AppLog.cs
- src/AppPaths.cs
- src/AppDiagnostics.cs
- src/AppConfig.cs

实际命名可根据现有代码风格调整。

## 3. 明确禁止

本阶段禁止：
- 重构 ImageEditor
- 拆分 PetController
- 重构 Messenger
- 重构 Compliance
- 修改 UI 视觉
- 修改业务流程
- 修改 AI Provider 行为
- 修改云端 API
- 修改用户数据 schema
- 全面 MVVM
- .NET 8 迁移
- Electron/Web 迁移
- 大规模格式化
- 大规模重命名

如发现相关问题，只记录到 Phase 1 执行报告，不处理。

## 4. P1-01：统一应用日志

目标：建立一个全局最小日志基础设施。

推荐新增 src/AppLog.cs。

至少支持：
- Info
- Warn
- Error
- Exception

日志默认目录：
%LOCALAPPDATA%\MomoPet\logs\

建议文件：
momo-YYYYMMDD.log

每条日志至少包含：
- timestamp
- level
- component
- message
- exception type（如有）
- exception message（如有）
- stack trace（如有）

要求：
- 日志失败不得导致主程序崩溃
- 不记录 API Key / Token / AccessKey
- 不记录完整敏感请求体
- 不把聊天内容、用户文件内容作为默认日志
- 允许必要的路径和错误上下文
- 保持实现轻量，不引入大型第三方 logging framework
- 保留现有专用日志，不强制删除已有机制

## 5. P1-02：顶层异常诊断

目标：确保未处理异常尽可能落盘，而不是直接闪退无信息。

在符合当前 WPF 启动方式的位置接入：
- WPF Dispatcher 未处理异常
- AppDomain 未处理异常
- TaskScheduler 未观察异常（适用时）

要求：
- 记录到统一 AppLog
- 尽量附加当前版本 / commit 信息
- 不吞掉无法安全继续执行的致命异常
- 不改变现有业务异常处理语义
- 不用大量 catch {} 把错误隐藏掉

如项目启动方式不适合某一个钩子，记录原因，不得硬套。

## 6. P1-03：统一 AppPaths / 路径边界

目标：逐步消除新的路径散落，但不做大规模迁移。

建立统一路径入口，至少覆盖新基础设施需要使用的：
- LocalApplicationData 根目录
- logs
- app/runtime
- diagnostics
- artifacts（仅构建脚本侧）

要求：
- 不改变现有用户数据目录
- 不迁移现有用户数据
- 不修改业务数据 schema
- 新代码不得继续新增散落的 %LOCALAPPDATA%\MomoPet 字符串
- 本阶段只做基础路径集中化，不要求一次性替换所有旧代码

## 7. P1-04：配置边界与 Secret 规则

目标：明确普通配置、Secret、运行时状态之间的边界。

要求执行侧：
1. 盘点当前项目中主要配置来源。
2. 输出分类：普通配置、Secret、用户状态、构建配置。
3. 保留现有 DPAPI Secret 存储方式，不做迁移。
4. 新增 docs/CONFIGURATION_AND_SECRETS.md。
5. 禁止把 Secret 放入 repo、log、build-info、CI artifact 文本。
6. 如发现硬编码 Secret，立即停止扩散并在报告中标为高风险；不得把真实 Secret 写进报告。

本任务以建立边界和规则为主，不要求一次性改造所有历史配置代码。

## 8. P1-05：CI / PR 质量门禁

Phase 0 已有 windows-build。Phase 1 将其固化成后续修改的质量门禁。

要求：
- 保持 push / pull_request / workflow_dispatch
- 保持 bootstrap → build → test → upload artifact
- 任何 build/test 失败必须导致 workflow failure
- artifact 只在成功构建后上传
- 不引入 continue-on-error 绕过关键步骤
- CI 输出必须能定位失败阶段

新增 docs/CI_POLICY.md，至少明确：
- 什么情况下 CI 必须通过
- 什么情况下任务不能验收
- 哪些失败允许认定为外部阻塞
- 外部阻塞必须如何记录

如 GitHub 仓库设置允许读取/配置 branch protection，执行侧可以提出建议，但不得擅自修改仓库保护规则。由规划角色后续决定。

## 9. P1-06：回归测试护栏

目标：不追求测试数量，而是提高测试入口稳定性和失败可读性。

保留现有：
- Compliance 7/7
- Office Comfort 7/7

要求：
- scripts/test.ps1 输出清晰的单项 PASS / FAIL
- artifacts/test-results.txt 必须包含测试名称、结果、exit code、总体 PASS / FAIL
- 如测试脚本异常退出，也必须被视为 FAIL
- 不允许因为测试脚本本身解析失败而误标通过

新增至少一个轻量工程级回归检查，覆盖：
- 统一日志可以创建日志文件
- Secret 样例不会被日志函数主动输出

可以使用 PowerShell 测试，不要求引入正式测试框架。

## 10. P1-07：版本与诊断元数据

目标：让用户提供日志时，可以定位它来自哪个构建。

至少让日志或 diagnostics 中能够获得：
- 当前 EXE 路径
- 应用版本（如果现有程序集版本可取）
- build commit（若可用）
- build time（若可用）
- OS version
- runtime / Node version（只在相关诊断时）

要求：
- 优先复用 Phase 0 的 build-info.json 思路
- 不把机器用户名、用户文件内容、Secret 写入诊断元数据
- 不强制增加联网遥测
- 本阶段禁止加入任何未经用户同意的 telemetry / analytics 上传

## 11. P1-08：诊断包设计

新增一个最小诊断脚本，例如 scripts/collect-diagnostics.ps1。

输出到 artifacts/diagnostics/ 或临时目录。

诊断包允许包含：
- 最近日志
- build/version 信息
- OS / PowerShell / Node / SDK 检测结果
- 文件是否存在
- 关键组件状态

禁止包含：
- API Key
- Token
- AccessKey
- 聊天正文
- 用户文档正文
- DPAPI 加密后的 Secret blob（无必要）
- 浏览器 Cookie 等私人信息

脚本必须在生成前做明确的敏感信息过滤。

## 12. P1-09：后续专项登记，不在本轮实现

以下项目必须记录到 docs/ENGINEERING_BACKLOG.md，但本轮禁止实现。

A. Skill 供应链完整性
- computedHash 正式算法
- 目录级 deterministic hash
- sourceRef / commit SHA 固定
- lock 文件迁移策略
- hash mismatch 失败策略

当前不要把 SHA256(SKILL.md) 直接固化为正式标准。

B. Release / Updater
- 版本命名
- Release artifact
- 时间戳备份
- 正在运行 EXE 的更新
- 回滚
- 安装器 / portable 模式
- 自动更新策略

Phase 1 本轮仍保持：
artifacts\MomoPet.exe = 最新成功构建标准产物

## 13. P1-10：文档同步

至少更新：
- README.md
- docs/ENGINEERING_BASELINE.md

新增：
- docs/CONFIGURATION_AND_SECRETS.md
- docs/CI_POLICY.md
- docs/ENGINEERING_BACKLOG.md

文档必须与实际实现一致。禁止写“已实现”但代码不存在。

## 14. 验收标准

只有全部满足，Phase 1 才允许提交规划角色验收。

Logging：
- 统一日志基础设施存在
- 至少在启动/关键异常路径实际使用
- 日志可落盘
- 日志失败不会导致应用崩溃
- Secret 不进入日志

Diagnostics：
- 顶层异常有可诊断记录
- 有最小 diagnostics 收集脚本
- diagnostics 不收集敏感正文和 Secret

Configuration：
- 配置 / Secret / 状态边界有文档
- 现有 DPAPI Secret 行为未被破坏
- 用户数据 schema 未改变

CI：
- windows-build 全绿
- Build PASS
- Compliance PASS
- Office Comfort PASS
- 新增工程护栏测试 PASS
- Artifact 上传 PASS

Git：
不得提交：
- Secret
- 用户数据
- .agents
- node_modules
- diagnostics 实际采集包
- 本机日志
- 临时构建文件

Behavior：
确认没有主动改变：
- 桌宠行为
- 任务/提醒
- 中转袋
- AI
- ImageEditor
- Messenger
- Compliance
- 资源中心
- 用户数据格式
- 云 API

## 15. 执行报告

完成后新增 docs/PHASE1_EXECUTION_REPORT.md。

必须包含：
1. 修改文件列表
2. 每个修改目的
3. git diff --stat
4. 日志实现说明
5. 异常诊断实现说明
6. 配置/Secret 盘点结果
7. 测试结果
8. CI run ID / commit / conclusion
9. artifact 结果
10. 未完成项
11. 风险项
12. 是否满足全部 Phase 1 验收标准
13. 需要规划角色判定的事项

不要自行宣布 Phase 1 归档。

## 16. 执行方式

执行软件必须：
1. 先读取 AGENTS.md
2. 再读取 tasks/CURRENT_TASK.md
3. 再完整读取本文档
4. 同步最新 main
5. 确认工作区状态
6. 严格按 Phase 1 范围执行
7. 完成 build / test / CI
8. 将真实 CI 结果写入 docs/PHASE1_EXECUTION_REPORT.md
9. 提交最终报告
10. 等待规划角色 Review

如出现外部阻塞，如实记录并停止对应子项，不得用伪产物、跳过测试或 silent failure 代替完成。

