# ARCHIVED — Phase 0 Engineering Baseline

状态：✅ **PASS**  
归档时间：2026-09-18  
最终执行报告：`docs/PHASE0_EXECUTION_REPORT.md`  
最终 CI 验证：commit `dddc83b80f49de21fdbd7ed7fd1ab4f77037b7ed`，GitHub Actions run `35320912068`，conclusion=`success`

> 本文件为已完成任务归档。不得作为当前执行入口。当前任务始终以 `tasks/CURRENT_TASK.md` 为准。

---

# MomoPet Phase 0：Engineering Baseline

目标仓库：`guogracey0-png/MomoPet`

## 1. 目标

本阶段不新增产品功能，不改 UI，不进行大规模架构重构。

唯一目标：

> 让 MomoPet 在一台干净的 Windows 开发机上，从 git clone 开始，通过明确、可重复的一套步骤完成依赖恢复、构建、回归测试和产物输出。

目标流程：

```text
git clone
↓
restore
↓
build
↓
test
↓
生成可运行产物
```

## 2. 当前已知工程问题

- 根目录 `build.ps1` 手工维护 C# 源文件列表。
- .NET Framework C# compiler 路径存在硬编码。
- Windows SDK `Windows.winmd` 固定到特定 SDK 小版本。
- 构建依赖 `.agents`，但 `.agents/` 被 gitignore。
- 存在 `skills-lock.json`，但当前缺少标准化 restore。
- Node 运行时依赖开发机环境或本地文件。
- 目前没有标准 CI。
- 已有 `test-compliance.ps1` 与 `test-office-comfort.ps1`，但未进入统一验证流程。

## 3. 允许修改范围

允许：
- 根目录 `build.ps1`
- 新增 `scripts/` 下的构建辅助脚本
- 新增 `.github/workflows/`
- 新增或更新工程说明文档
- 必要的小范围 `.gitignore` 调整
- 与构建、依赖恢复、测试、产物生成直接相关的脚本

禁止修改业务行为：
- 桌宠行为
- UI 样式
- AI 功能逻辑
- ImageEditor 业务逻辑
- Messenger 业务逻辑
- Compliance 业务规则
- 用户数据格式
- API 协议
- 云端接口
- 皮肤资源
- 现有功能入口

禁止：
- 全面 MVVM 重构
- PetController 拆分
- ImageEditor 拆分
- .NET 8 迁移
- Electron/Web 迁移
- 大面积重命名或格式化

## 4. P0-01：统一脚本入口

建议新增：

```text
scripts/bootstrap.ps1
scripts/restore-skills.ps1
scripts/build.ps1
scripts/test.ps1
scripts/verify.ps1
```

### bootstrap.ps1

负责：
- 检查 Windows / PowerShell
- 检查 Git
- 检查 .NET Framework C# compiler
- 动态发现 Windows SDK
- 检查 Node
- 调用 skills restore
- 输出环境状态

不得修改业务代码。

### build.ps1

负责：
- 验证依赖
- 编译 OCR helper
- 打包 EmbeddedRuntime
- 编译 MomoPet
- 输出最终产物

根目录原有 `build.ps1` 可以保留为兼容入口，转发到 `scripts/build.ps1`。

### test.ps1

统一执行：
- `test-compliance.ps1`
- `test-office-comfort.ps1`

任何测试失败必须返回非 0 exit code。

### verify.ps1

一键执行：

```text
bootstrap
build
test
```

目标命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

## 5. P0-02：去掉 Windows SDK 小版本硬编码

禁止继续固定类似：

```text
10.0.26100.0
```

应动态搜索：

```text
C:\Program Files (x86)\Windows Kits\10\UnionMetadata\
```

从已安装版本中选择最高可用版本的 `Windows.winmd`。

要求：
- 输出实际使用 SDK 版本
- 找不到时明确报错
- 不允许 silent failure

## 6. P0-03：Node 运行时标准化

推荐优先顺序：

1. `本地部署\node.exe`
2. 系统 Node
3. 明确失败

要求：
- 输出实际 Node 路径
- 输出 Node 版本
- 明确最低兼容版本
- 找不到时给出清晰错误
- 不允许无提示 fallback

本阶段不强制自动下载安装 Node，除非现有项目机制天然适合且不会增加额外风险。

## 7. P0-04：实现 .agents Restore

新增：

`scripts/restore-skills.ps1`

读取 `skills-lock.json`，根据：
- source
- sourceType
- skillPath
- computedHash

恢复：

`.agents/skills/<skill-name>/`

至少覆盖现有 skills。

要求：
- `.agents` 继续保持不提交到 Git
- restore 可重复执行
- 已存在依赖允许复用
- restore 失败必须终止构建
- 校验 skill 目录完整性
- 如果无法确认 computedHash 算法，不得伪造 hash 校验；应明确 warning 并记录 TODO

## 8. P0-05：构建失败可诊断

重点治理构建、依赖恢复和打包阶段的静默失败。

关键步骤至少输出：

```text
Step
Result
Path
Error
```

示例：

```text
[OK] Node runtime: ...
[OK] Windows SDK: ...
[OK] Skill: wind-mcp-skill
[FAIL] Skill: wind-alice
Reason: ...
```

本阶段不要求清理所有业务源码中的 `catch {}`。

## 9. P0-06：统一构建产物

建立：

`artifacts/`

至少输出：

```text
artifacts/
  MomoPet.exe
  build-info.json
  test-results.txt
```

如果现有逻辑仍生成 `MomoPet.next-*.exe` 可以保留，但 `artifacts/MomoPet.exe` 必须始终代表当前成功构建版本。

`build-info.json` 建议包含：
- commit
- branch
- buildTime
- nodeVersion
- windowsSdk
- compiler
- testsPassed

## 10. P0-07：统一回归测试

必须运行：
- `test-compliance.ps1`
- `test-office-comfort.ps1`

流程：

```text
Build Success
↓
Compliance Test
↓
Office Comfort Test
↓
Baseline Success
```

任何测试失败，不得标记 Baseline Success。

测试结果写入：

`artifacts/test-results.txt`

## 11. P0-08：GitHub Actions

新增：

`.github/workflows/windows-build.yml`

环境：

`windows-latest`

触发：
- push
- pull_request
- workflow_dispatch

流程：
- checkout
- bootstrap
- build
- test
- upload artifact

如果外部 skill 源或私有能力导致完整 CI 暂时不可执行：
- 不得偷偷跳过
- 明确记录阻塞原因
- 尽可能完成不依赖私有凭证的 build validation
- 禁止提交任何 Key / Token

## 12. P0-09：工程基线文档

新增：

`docs/ENGINEERING_BASELINE.md`

至少写明：
- 系统要求
- 首次 clone 后构建步骤
- 依赖恢复
- 测试命令
- 运行方法
- 产物目录
- 常见错误
- 架构约束

README 中同步增加首次构建入口。

## 13. P0-10：业务行为保护

完成后确认本阶段没有主动改变：
- 任务/提醒
- 桌宠基本行为
- 中转袋
- AI 搜索入口
- 图像编辑入口
- 合规审核入口
- Messenger 入口
- 资源中心入口

要求：
- 无非必要业务源码 diff
- 不修改用户数据 schema
- 不修改 API
- 不修改 Provider 配置格式

## 14. 验收标准

### Build
- clean clone 可进入统一构建流程
- 无固定 SDK 小版本依赖
- Node 路径明确
- `.agents` 有自动恢复流程
- 成功生成 EXE

### Test
以下全部通过：
- `test-compliance.ps1`
- `test-office-comfort.ps1`

### Artifact
存在：
- `artifacts/MomoPet.exe`
- `artifacts/build-info.json`
- `artifacts/test-results.txt`

### Git
不得提交：
- API Key
- Token
- 用户数据
- `.agents`
- `node_modules`
- 临时文件

### CI
GitHub Actions 至少能够：
- 启动
- 执行构建验证
- 明确报告成功或失败

若外部依赖阻塞完整 CI，必须留下明确 TODO 和原因。

## 15. 执行结果必须返回

完成后必须提供：

1. 修改文件列表
2. 每个文件修改目的
3. `git diff --stat`
4. build 输出摘要
5. test 输出摘要
6. artifacts 文件列表
7. 未完成项
8. 风险项
9. 是否满足全部验收标准

建议同时提供完整 `git diff` 或 patch。

## 16. 最终执行指令

你是 MomoPet Phase 0 的执行工程师。

严格遵守 `AGENTS.md` 和本文档。

不要扩展任务范围。
不要新增产品功能。
不要主动重构业务模块。

优先保证：
1. 可重复构建
2. 依赖可恢复
3. 测试可自动运行
4. 失败可诊断
5. 产物可追踪

执行完成后，按第 15 节完整返回结果，交由规划角色 Review 和验收。

