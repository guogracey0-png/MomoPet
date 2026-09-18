# MomoPet Engineering Baseline

本文件描述 MomoPet 在一台干净 Windows 开发机上从 `git clone` 到构建产物的一整套可重复、可诊断、可自动测试的工程流程（对应任务 `PHASE0_ENGINEERING_BASELINE`）。

流程总览：

```text
git clone
  ↓
bootstrap   （环境检查 + Skill 依赖恢复）
  ↓
build       （编译 OCR 助手 + EmbeddedRuntime + MomoPet → artifacts\MomoPet.exe）
  ↓
test        （Compliance + Office Comfort + Engineering Guardrails → artifacts\test-results.txt）
  ↓
artifacts\  （MomoPet.exe / build-info.json / test-results.txt）
```

一键命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

## 系统要求

- Windows 10/11 64 位
- Git（在 PATH 中）
- .NET Framework 4.x（自带 `csc.exe` v4.0.30319）
- **Windows 10/11 SDK**（提供 `Windows Kits\10\UnionMetadata\<版本>\Windows.winmd`，用于本地 OCR 编译）
- Node.js ≥ 16（或把便携版放到 `本地部署\node.exe`；构建脚本自动按“本地部署 → 系统 Node”顺序解析）

## 首次 clone 后构建步骤

```powershell
git clone <repo-url> MomoPet
cd MomoPet

# 环境检查 + 自动恢复 .agents\skills（对应 skills-lock.json）
powershell -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1

# 编译
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1

# 回归测试
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

或直接：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

根目录 `build.ps1` 保留为兼容入口，会转发到 `scripts\build.ps1`。

## 依赖恢复

- 脚本入口：`scripts\bootstrap.ps1` / `scripts\restore-skills.ps1`
- `skills-lock.json` 声明每个 Skill 的 `source` / `sourceType` / `skillPath` / `computedHash`
- restore 把 Skill 恢复（克隆 → 提取）到 `.agents\skills\<skill-name>\`，`.agents` 保持 gitignore
- 已存在且完整的 Skill 会直接复用；缺失时按需下载
- 目标目录必须包含 `SKILL.md`，否则报错并中止构建
- `computedHash` 的算法当前未在锁文件中定义，脚本不做伪校验，仅给出 WARNING 与 TODO（见 `scripts\restore-skills.ps1` 与 `skills-lock.json`）

## 测试命令

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\test.ps1
```

测试内容：

- `test-compliance.ps1` — 合规规则回归
- `test-office-comfort.ps1` — Office 舒适度 / 状态保存回归
- `test-engineering-guardrails.ps1` — 工程护栏：统一日志可落盘、敏感样例被遮罩、日志失败不崩溃

任一测试失败时脚本返回非 0 exit code，并写入 `artifacts\test-results.txt`（每项含测试名、PASS/FAIL、exit code 与总体结论），此时不得标记为构建成功。

## 运行方法

产物为单文件 EXE，构建/测试通过后位于：

```text
artifacts\MomoPet.exe
```

双击即可运行；首次运行会在 `%LOCALAPPDATA%\MomoPet\app` 释放内置的 OCR 助手、便携 Node、AI 桥接、合规规则与 Skill 文件。也可发布为 GitHub Release 附件分发。

## 产物目录

`artifacts\`（已在 `.gitignore` 中，不提交到 Git）：

```text
artifacts\
  MomoPet.exe         # 始终代表最近一次成功构建
  build-info.json     # commit / branch / buildTime / nodeVersion / windowsSdk / compiler / testsPassed
  test-results.txt    # 每次测试运行结果
```

`artifacts\MomoPet.exe` 在每次成功构建后被原子替换，保证始终指向当前版本。

## 日志与诊断

- 统一应用日志：`%LOCALAPPDATA%\MomoPet\logs\momo-YYYYMMDD.log`（`src/AppLog.cs`），每条含 timestamp/level/component/message/异常；文件头含 version / commit / buildTime / os / runtime（`src/AppDiagnostics.cs`，构建时嵌入 `build-meta.txt`）。
- 路径边界：`src/AppPaths.cs`（数据根沿用 `MomoPaths.DataDir()`），禁止新代码散落 `%LOCALAPPDATA%\MomoPet` 字面量。
- 顶层异常（Dispatcher / AppDomain / TaskScheduler / 启动）已接入统一日志（`src/MomoPet.cs` Program.Main）。
- 诊断包：`scripts\collect-diagnostics.ps1` 输出到 `artifacts\diagnostics\<stamp>\`（环境/版本/最近日志/文件存在性），生成前自动过滤密钥等敏感信息。
- Secret 边界与盘点见 `docs/CONFIGURATION_AND_SECRETS.md`；CI 门禁规则见 `docs/CI_POLICY.md`。

## 脚本分层

| 脚本 | 职责 |
| --- | --- |
| `scripts\common.ps1` | 共享辅助：输出、工具链发现、commit/branch 读取 |
| `scripts\bootstrap.ps1` | 环境检查 + Skill 恢复，明确报错 |
| `scripts\restore-skills.ps1` | 按 `skills-lock.json` 恢复 `.agents\skills` |
| `scripts\build.ps1` | 编译并产出 `artifacts\MomoPet.exe` + `build-info.json` |
| `scripts\test.ps1` | 统一回归测试并产出 `artifacts\test-results.txt` |
| `scripts\verify.ps1` | `bootstrap → build → test` 一键串接 |
| `build.ps1`（根目录） | 兼容转发到 `scripts\build.ps1` |

## 常见错误

| 错误 | 原因与处理 |
| --- | --- |
| `[FAIL] Windows SDK: ... not found` | 未安装 Windows SDK。安装 Windows 10/11 SDK（含 UnionMetadata），或提供包含 `Windows.winmd` 的 SDK 版本 |
| `Node.js not found` | 把便携版 node.exe 放到 `本地部署\node.exe`，或把 node 加入 PATH |
| `C# compiler (csc.exe) not found` | 缺少 .NET Framework 4.x 运行时/编译器 |
| `restore-skills: clone failed` | 目标源不可达（如外部 git 仓库受限）。检查网络后再试 |
| `OCR helper compiler failed` | 缺少 `Windows.winmd` 或其引用的 GAC 程序集，检查 SDK |

任何步骤返回非 0 exit code 都意味着失败，脚本会打印 `[FAIL]` 与步骤信息，不允许静默通过。

## 架构约束

本工程基线和构建脚本只处理“可重复构建 / 依赖恢复 / 测试 / 产物”，**不修改业务行为**：

- 不改变桌宠行为、UI、AI、ImageEditor、Messenger、Compliance 规则、用户数据、API/云接口协议
- 不迁移 .NET 8 / Electron / Web
- 不做大面积重命名或格式化
- 不提交 API Key / Token / 用户数据 / `.agents` / `node_modules` / 临时文件 / `artifacts`