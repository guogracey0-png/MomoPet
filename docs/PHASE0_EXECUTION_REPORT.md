# Phase 0 Engineering Baseline — 执行报告

> 本文件由执行工程师自动生成。结论以规划角色的 Code Review / 验收为准，本文件只记录执行产出、运行结果与“需要判定”的待定项。

## 1. 执行环境

- 仓库：`guogracey0-png/MomoPet`，分支 `main`（已同步）
- Windows 11 Pro；PowerShell 5.1.26100；Git 2.55.0
- 本机 **未安装 Windows SDK**，且执行会话无管理员权限、无 winget
- Node：系统 PATH `node.exe` v22.16.0（最低兼容 16.0 已校验通过）

## 2. 修改文件清单

### 新增
| 文件 | 用途 |
| --- | --- |
| `scripts/common.ps1` | 共享辅助：状态输出、csc/SDK/Node 动态发现、commit/branch 读取 |
| `scripts/bootstrap.ps1` | 环境检查 + Skill 恢复，缺失依赖非零退出 |
| `scripts/restore-skills.ps1` | 按 `skills-lock.json` 恢复 `.agents/skills/*` |
| `scripts/build.ps1` | 编译 OCR 助手 + 便携运行时打包 + 编译 MomoPet → `artifacts/MomoPet.exe` |
| `scripts/test.ps1` | 统一回归测试 → `artifacts/test-results.txt` |
| `scripts/verify.ps1` | `bootstrap → build → test` 一键串联 |
| `.github/workflows/windows-build.yml` | CI：push / PR / workflow_dispatch，bootstrap→build→test→upload artifact |
| `docs/ENGINEERING_BASELINE.md` | 首次 clone 构建步骤、依赖恢复、测试、产物、常见错误、架构约束 |

### 修改
| 文件 | 用途 |
| --- | --- |
| `build.ps1`（根目录） | 改为兼容转发器，指向 `scripts/build.ps1` |
| `.gitignore` | 追加 `artifacts/` |
| `README.md` | 构建入口改为 `scripts/verify.ps1`，补充目录说明 |

未改动任何业务源码（`src/*.cs`、`wind_bridge`、`assets`、`cloud`、`tools`、`compliance-rules.txt`）。

## 3. 运行结果

### 本机 bootstrap — 通过（除 SDK）
- Git ✓ / csc.exe ✓ / `Windows.winmd` ✗
- Node ✓ v22.16.0
- 4 个 Skill 恢复成功（github 3 + gitee 1）：`intraday_abnormal_move_alert_skill` `wind-alice` `wind-find-finance-skill` `wind-mcp-skill`
- 二次运行 `restore-skills.ps1`：幂等复用，exit 0

### CI（GitHub Actions `windows-build`，windows-latest）— 全绿通过 ✅
- 运行期限：commit `dddc83b`，Job `build-test` conclusion=**success**
- 步骤：Checkout ✓ → Bootstrap ✓ → Build ✓ → Test ✓ → Upload artifacts ✓
- 构建成功产出 `artifacts\MomoPet.exe`（约 60 MB）
- 回归测试 **全部通过**：Compliance 7/7，Office Comfort 7/7，`OVERALL: PASS`
- `build-info.json`：`testsPassed=true`、`windowsSdk=10.0.26100.0`、`nodeVersion=v22.23.2`、`compiler=csc.exe`

> 本机因未安装 Windows SDK 且无管理员权限，无法本地生成 EXE；按决策由 CI（自带 SDK）完成构建与测试，已完成并通过。

### CI 过程中修复的两个工程问题
1. **SDK 目录过滤**：`UnionMetadata` 下存在非版本目录 `Facade`，脚本误用 `[version]` 解析报错 → 改为仅接受能解析为版本的目录（`scripts/common.ps1`）。
2. **CJK 字面量编码**：`test-office-comfort.ps1` 含原始中文且无 UTF-8 BOM，PS 5.1 在 CI 的 CP1252 代码页下把文件按 ANSI 解析导致 L20 解析失败 → 为全部管道脚本追加 UTF-8 BOM。

## 4. 待判断项（交由规划角色 / 其他软件判定）

1. **SDK 阻塞处理** ✅ **已交由 CI 解决**：由 GitHub Actions（windows-latest 自带 SDK）完成构建与测试，CI 已全绿。本机仍可在安装 SDK 后跑 `.\scripts\verify.ps1` 验证（需管理员）。
2. **P0-04 `computedHash` 校验算法** ⏭️ **转入后续工程任务，不阻塞 Phase 0**：Phase 0 已按原任务要求做到“算法无法确认时不伪造校验，并明确 WARN + TODO”。规划角色**未批准**“仅对 `SKILL.md` 做 SHA-256”作为最终规范。后续应单独设计 Skill 供应链完整性方案，优先评估**目录级确定性 hash + 固定 `sourceRef`（commit SHA）**，再决定是否启用强制校验。现有 4 个 `computedHash` 与当前源内容不一致的问题作为后续任务输入，不在 Phase 0 内回填或强制开启。
3. **`wind-alice`（gitee 源）在 CI 可达性** ✅ **已验证**：CI 中 `wind-alice` 恢复成功，网络可达性无阻塞。
4. **`artifacts` 覆盖策略** ✅ **Phase 0 维持当前方案**：`artifacts\MomoPet.exe` 继续代表“最新一次成功构建的标准产物”，当前原子覆盖策略无需在 Phase 0 修改。运行中程序的版本备份、时间戳副本、回滚和更新机制属于后续 **Release / Updater** 设计，不在 Phase 0 实现。
5. **CI 首次运行结果** ✅ **已确认**：commit `dddc83b` 对应 run 35320912068 conclusion=success，构建与测试均成功，产物已上传。

## 5. 验收结论
- Build：统一流程 / 无固定 SDK 小版本 / Node 明确 / `.agents` 自动恢复 ✅（本地校验 + CI 实测）
- 「成功生成 EXE / test / artifacts」✅ **CI 实测通过**：`artifacts/MomoPet.exe`、`build-info.json`、`test-results.txt` 已由 windows-latest 生成；Compliance 7/7、Office Comfort 7/7 全过，`testsPassed=true`
- 任何测试失败不得标记 Baseline Success —— 已遵守
- ✅ **规划角色最终验收：Phase 0 PASS，可归档。** CI 已完成 bootstrap → build → test → upload artifact 全链路验证；剩余 `computedHash` 供应链强化与生产更新/回滚策略转入后续阶段，不阻塞 Phase 0。

## 6. 风险项
- `computedHash` 当前仍为 `[WARN]+TODO`，这是 Phase 0 允许的已知后续项；后续需先定义正式供应链校验规范，再决定锁文件迁移方式。
- 生产运行中的版本备份 / 回滚 / Updater 机制尚未设计，后续单独进入发布工程任务；不影响当前构建产物基线。
- 未提交 `.agents` 内容本身（已 gitignore），只提交恢复逻辑；SKILL 文件不进入仓库
- 未提交任何 Key / Token / 用户数据