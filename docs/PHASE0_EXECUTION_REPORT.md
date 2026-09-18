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

## 3. 本机运行结果

### bootstrap — 通过（除 SDK）
- Git ✓ / csc.exe ✓ `/ Windows Kits` 中 `Windows.winmd` ✗
- Node ✓ v22.16.0
- 4 个 Skill 恢复成功（github 3 + gitee 1）：`intraday_abnormal_move_alert_skill` `wind-alice` `wind-find-finance-skill` `wind-mcp-skill`
- 二次运行 `restore-skills.ps1`：幂等复用，exit 0

### build — 被外部阻塞
- 脚本正确输出 `[FAIL] Windows SDK (Windows.winmd) not found` 并 exit 1（非静默失败，符合 P0-02/05）
- 阻塞根因：**本机未安装 Windows SDK，且无管理员权限安装**
- 结论：**本机无法生成 EXE；未伪造产物**

### test / artifacts
- 因无 EXE 未在本机执行；未提交伪造的 `artifacts/MomoPet.exe`、`build-info.json`、`test-results.txt`
- CI（`windows-latest`，自带 SDK）可作为完整构建的承载

## 4. 待判断项（交由规划角色 / 其他软件判定）

1. **SDK 阻塞处理**：本地确认缺失 `Windows.winmd`。判定：安装 Windows 10/11 SDK（需管理员）后在本机跑 `.\scripts\verify.ps1`，或直接依赖 GitHub Actions（windows-latest）完成构建。
2. **P0-04 `computedHash` 算法**：`skills-lock.json` 未定义 `computedHash` 的计算方式，脚本做 `[WARN] + TODO`，**未做伪校验**。判定：定义规范算法（例如对 `SKILL.md` 做 sha256）并由规划角色批准后启用强制校验。
3. **`wind-alice`（gitee 源）在 CI 可达性**：本机可 clone；但 GitHub CI 网络到 gitee 可能受限。判定：如 CI 失败是否接受如实失败（不静默跳过），或更换/镜像该源。
4. **`artifacts` 原子覆盖策略**：采用 `Move-Item -Force`；原 build 提及的“外部 safe-delete 钩子”在本环境不存在。判定：生产中是否需要改为时间戳备份，防止覆盖正在运行的程序。
5. **CI 首次运行结果**：push 后由 GitHub Actions 给出构建与测试成功/失败，需规划角色据此验收。

## 5. 验收结论（占位，待规划角色确认）
- Build：统一流程 / 无固定 SDK 小版本 / Node 明确 / `.agents` 自动恢复 ✅（本地工具链发现已实测）
- 「成功生成 EXE / test / artifacts」：⚠️ 本机被 SDK 阻塞，未实测；依赖 SDK 环境或 CI
- 任何测试失败不得标记 Baseline Success —— 已遵守

## 6. 风险项
- 未提交 `.agents` 内容本身（已 gitignore），只提交恢复逻辑；SKILL 文件不进入仓库
- 未提交任何 Key / Token / 用户数据