# MomoPet Phase 1  Engineering Guardrails  执行报告

状态：✅ PASS / ARCHIVED（规划角色已完成最终验收）

- 目标仓库：`guogracey0-png/MomoPet`
- 分支 / 基线：`main` @ `d46ca54`（进入本阶段）→ `d789c60`（本阶段最终提交）
- 任务文档：`tasks/PHASE1_ENGINEERING_GUARDRAILS.md`
- 执行者：执行工程师（本会话）；规划角色 / 评估方另开会话验收

> 本报告如实记录 Phase 1 全部实现、验证与待判定事项。**所有 CI 结论均来自真实 GitHub Actions run**，未用“本机未跑但应该没问题”代替。

---

## 0. 结论速览

| 验收域 | 结论 |
| --- | --- |
| 统一日志（P1-01） | 达成 |
| 顶层异常诊断（P1-02） | 达成（接入既有钩子，未改变语义） |
| 统一路径边界（P1-03） | 达成（最小集中化） |
| 配置/Secret 边界（P1-04） | 达成（文档 + 扫描零命中） |
| CI 门禁（P1-05） | 达成 |
| 回归测试护栏（P1-06） | 达成 |
| 版本/诊断元数据（P1-07） | 达成 |
| 诊断包（P1-08） | 达成 |
| 后续专项登记（P1-09） | 达成（仅登记，未实现） |
| 文档同步（P1-10） | 达成 |
| CI 全绿 | **达成**（run `35326969067`，conclusion=success） |
| 未改变业务行为 / schema / Secret | 达成 |

---

## 1. 修改文件列表与目的

| 文件 | 目的 | 对应 |
| --- | --- | --- |
| `src/AppLog.cs`（新增） | 全局最小统一日志：Info/Warn/Error/Exception，落盘 `%LOCALAPPDATA%\MomoPet\logs\momo-YYYYMMDD.log`，敏感值登记遮罩，日志失败不抛异常 | P1-01 |
| `src/AppPaths.cs`（新增） | 统一路径入口：数据根 / logs / app(runtime) / diagnostics；新代码不再散落路径字符串 | P1-03 |
| `src/AppDiagnostics.cs`（新增） | 版本/commit/branch/buildTime/OS/runtime 元数据，读取构建时嵌入的 `build-meta.txt` | P1-07 |
| `src/MomoPet.cs`（改动） | 顶层异常（Dispatcher/AppDomain/TaskScheduler）路由到统一 AppLog（在既有钩子调用点 `MomoLog` 追加一行） | P1-02 |
| `scripts/build.ps1`（改动） | 编译源列表加入三个新类；构建时生成并嵌入 `build-meta.txt`（commit/branch/buildTime/node/windowsSdk） | P1-03/07 |
| `scripts/test.ps1`（改动） | 结果行带测试名称 + PASS/FAIL + exit code；新增 Engineering guardrails 测试调用；异常退出判定 FAIL | P1-06 |
| `test-engineering-guardrails.ps1`（新增） | 轻量工程护栏回归：日志可落盘、版本元数据头、Secret 遮罩、日志失败不崩溃（共 11 项） | P1-06 |
| `scripts/collect-diagnostics.ps1`（新增） | 最小诊断包生成器，输出到 `artifacts\diagnostics\<stamp>`，生成前 Scrub 常见密钥形态，只收集环境/日志摘要 | P1-08 |
| `docs/CONFIGURATION_AND_SECRETS.md`（新增） | 普通配置 / Secret / 用户状态 / 构建配置边界 + Secret 盘点 + 规则 | P1-04 |
| `docs/CI_POLICY.md`（新增） | CI 门禁规则：何时必须通过、何时不能验收、何谓外部阻塞、如何记录 | P1-05 |
| `docs/ENGINEERING_BACKLOG.md`（新增） | 登记 Skill 供应链完整性 + Release/Updater 待办（本轮不实现） | P1-09 |
| `docs/ENGINEERING_BASELINE.md`（改动） | 同步测试清单、脚本分层、日志/诊断说明 | P1-10 |
| `README.md`（改动） | 修订产物说明（原子替换 + 时间戳备份归入 backlog），补充日志/诊断入口 | P1-10 |
| `.github/workflows/windows-build.yml`（改动） | Test 步命名更新为含 engineering guardrails（门禁本体 Phase 0 已固化，本轮确认无 `continue-on-error`、artifact 仅成功后上传） | P1-05 |

## 2. git diff --stat（`d46ca54..d789c60`）

```
 .github/workflows/windows-build.yml |   2 +-
 README.md                           |   4 +-
 docs/CI_POLICY.md                   |  60 +++++++++++++++++
 docs/CONFIGURATION_AND_SECRETS.md   |  48 ++++++++++++++
 docs/ENGINEERING_BACKLOG.md         |  29 ++++++++
 docs/ENGINEERING_BASELINE.md        |  13 +++-
 scripts/build.ps1                   |  15 ++++-
 scripts/collect-diagnostics.ps1     |  90 +++++++++++++++++++++++++
 scripts/test.ps1                    |   8 ++-
 src/AppDiagnostics.cs               |  82 +++++++++++++++++++++++
 src/AppLog.cs                       | 127 ++++++++++++++++++++++++++++++++++++
 src/AppPaths.cs                     |  41 ++++++++++++
 src/MomoPet.cs                      |   2 +
 test-engineering-guardrails.ps1     |  62 ++++++++++++++++++
 14 files changed, 575 insertions(+), 8 deletions(-)
```

- 提交 1 `d0388ee`：Phase 1 实现主体（14 文件，579+/-8）
- 提交 2 `d789c60`：修复 guardrails 测试在 PowerShell 5.1 下反射 `Invoke` 参数绑定失败（1 文件，9+/13-）

## 3. 关键 diff 摘要

**`src/AppLog.cs`（核心）**
- `LogLevel{Info,Warn,Error}`；`Info/Warn/Error/Log` 与 `Exception` 重载。
- `RegisterSensitive(token,label)` + `Scrub(text)`：登记敏感值，写入前替换为 `[REDACTED:label]`。
- 每条日志含 timestamp / level / component / message / exception type/message/stacktrace；文件头写 `# version/commit/branch/buildTime/os/runtime`。
- `Write()` 全程 try/catch：**日志失败绝不影响主程序**（P1-01 硬性要求）。

**`src/MomoPet.cs`**（P1-02 接入，仅追加一行，未改变既有 catch 语义）
```csharp
static void MomoLog(string scope, Exception error){
    if(error==null)return;
    AppLog.Error(scope, error);   // 统一日志（P1-01/P1-02），自带失败隔离与遮罩
    try{ ... 原有 ui-errors.log 逻辑保留 ... }catch{}
}
```
顶层三处钩子（DispatcherUnhandledException / AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException）本就调用 `MomoLog`，现自动进入统一日志。Dispatcher 仍 `e.Handled=true`（原有行为），AppDomain 不吞致命异常（原有行为），TaskScheduler 继续 `SetObserved()`（原有行为）——均未改变语义。

**`scripts/build.ps1`**：sourceFiles 加入 `AppPaths.cs,AppLog.cs,AppDiagnostics.cs`；生成 `build-meta.txt` 并以 `/resource` 嵌入，供 `AppDiagnostics.ReadBuildMeta` 读取。

**`scripts/collect-diagnostics.ps1`**：生成前 `Scrub` 过滤 `sk-*`、`Bearer …`、`key/token/secret=…` 形态；仅拷贝最近 `momo-*.log`；只记录关键文件“是否存在”（含 `wind-key.dat` 仅记存在、不复制 DPAPI blob）；SDK/Node 缺失时降级为 `(unavailable)`（已实测输出正常）。

## 4. 日志与异常诊断实现说明

- **统一日志**：`src/AppLog.cs`，见上；已通过工程护栏测试逐项验证（落盘、元数据头、遮罩、失败不崩溃）。
- **异常诊断**：`Program.Main` 既有三处顶层钩子 → `MomoLog` → `AppLog.Error(scope, error)`；统一日志头自动携带 version/commit/buildTime/os/runtime，可定位“构建来源”。选择复用既有钩子而非新增重复注册，符合“接入所需位置”且“不改语义”。未用大量 `catch{}` 隐藏致命异常（仅日志侧失败静默，这是 P1-01 明确允许）。

## 5. 配置 / Secret 盘点结果

来源见 `docs/CONFIGURATION_AND_SECRETS.md`。要点：
- 四类边界：普通配置 / Secret（禁入 repo、log、build-info、CI artifact）/ 用户状态 / 构建配置。
- **Secret 盘点**（均 DPAPI `CurrentUser` + 熵加密，未迁移）：Wind Key（`wind-key.dat`）、AI LLM Key、图片模型/文本 Key、精确图像 Key、图像 Profile Key、账号 Token、桥接脚本 `TEXT_LLM_API_KEY`（进程环境变量）。
- **扫描结果**：对 `src/` 与 `wind_bridge/` 执行 `sk-*`、`AIza…`、`ghp_…`、`AKIA…`、长 `Bearer` 赋值、以及 `key/token/secret=` 字符串字面量扫描，**0 命中**；既有赋值均为占位符/环境变量引用。盘点“未发现硬编码 Secret”结论成立。

## 6. 测试结果（真实 CI 输出）

来自 `artifacts\test-results.txt`（run `35326969067`），OVERALL: **PASS**：

- **Compliance regression tests**：7/7 PASS，RESULT=PASS (exit 0)
- **Office comfort regression tests**：7/7 PASS，RESULT=PASS (exit 0)
- **Engineering guardrail tests**（Phase 1 新增）：11/11 PASS，RESULT=PASS (exit 0)，覆盖：
  AppLog 创建日志目录 / 创建 `momo-YYYYMMDD.log` / 日志头含版本元数据 / Info 写入 / Error 异常 message 写入 / Warn 写入 / 登记 Secret 不原文出现 / 遮罩为 `[REDACTED:test-secret]` / Scrub API 遮罩 / Scrub 去除原文 / 写入不可写目录不崩溃

另：`test-results.txt` 每行包含 测试名 + PASS/FAIL + exit code，末尾含 OVERALL（P1-06 要求）。

## 7. CI run / commit / conclusion

- 首次推送（实现主体 `d0388ee`）：工作流 **Build PASS，Test FAIL**（run `35326708544`）——失败根因见下，非产物问题。
- 修复后推送（`d789c60`）：**全绿**。

| 字段 | 值 |
| --- | --- |
| run ID | `35326969067` |
| head sha / commit | `d789c6023e4e14a7a6c12c34d29f5b34e92e4cdc` |
| trigger | push / main |
| workflow | windows-build |
| job | build-test（ID `105542162635`） |
| **conclusion** | **success** |
| 各步 | Checkout ✓ / Bootstrap ✓ / Build ✓ / Test ✓ / Upload artifacts ✓ |

## 8. Artifact 结果（真实下载核验）

run `35326969067` 上传的 `momopet-artifacts` 已用 `gh run download` 拉取核验：

- `MomoPet.exe`（CI 用 Windows SDK `10.0.26100.0` 构建）
- `build-info.json`：`commit=d789c6023e4e14a7a6c12c34d29f5b34e92e4cdc`、`branch=main`、`buildTime=2026-09-18 08:57:39`、`nodeVersion=v22.23.2`、`windowsSdk=10.0.26100.0`、`compiler=v4.0.30319\csc.exe`、`testsPassed=true`
- `test-results.txt`：三项全 PASS，OVERALL PASS（内容见第 6 节）

artifact 仅在 Build+Test 全部成功后上传（`if-no-files-found: error`+步骤顺序保证），符合 P1-05。

## 9. 期间发现并修复的问题

- **`collect-diagnostics.ps1` 语法错误（编码）**：初始版本写脚本时缺 UTF-8 BOM，PowerShell 5.1 按 ANSI 解析中文注释导致 `Unexpected token '}'`。已补 BOM（与其余脚本一致）并重跑全部脚本解析校验。
- **guardrails 测试 CI 失败**：第一版 `test-engineering-guardrails.ps1` 用 `GetMethod(name, flags, binder, types, modifiers)`+`Invoke(…, argsList)` 反射调用 `AppLog`，在 GitHub Actions 的 Windows PowerShell 5.1 下报 `Exception calling "Invoke"…Parameter count mismatch`。改为**直接静态方法调用** `[MomoPetApp.AppLog]::xxx`（AppLog 为 public static，无需反射），`AppPaths` 仅做类型存在性检查（internal 类型经 `assembly.GetType` 可查）。修复后 CI 全绿。

## 10. 未完成项（本轮范围内均已达成；仅登记性待办）

- Phase 1 全部验收标准达成，无未完成实现项。
- 继承的登记性待办（`docs/ENGINEERING_BACKLOG.md`，**本轮禁止实现**）：Skill 供应链完整性（computedHash 正式算法、目录级 hash、sourceRef 锁定、lock 迁移、mismatch 失败策略）与 Release/Updater（版本命名、Release artifact、时间戳备份、运行中更新、回滚、安装器、自动更新）。

## 11. 风险项

1. **既有 `skills-lock.json` 的 `computedHash` 与源 SKILL.md 不一致**（raw / LF 两种口径均不匹配）：当前 `restore-skills.ps1` 只 WARNING + TODO，不做伪校验，故不阻塞构建。正式算法未定，已登记 backlog 由规划角色裁决（不信任旧哈希，不强行启用校验）。
2. **本机 Windows SDK 缺失**：本地无法直接 build/test，验证全部依赖 CI（windows-latest 自带 SDK `10.0.26100.0`）。不构成阻塞（CI_POLICY 第 4 节已归类）。
3. **既有哈希校验在未立规前不启用**：避免因口径未定造成误报/误拦，符合 P1-09 要求。
4. Node 20 弃用告警（`actions/checkout@v4`/`upload-artifact@v4` 在 Node 24 上运行）为 CI 平台侧提示，不影响本次结论，可在后续流水线升级处理。

## 12. 是否满足全部 Phase 1 验收标准

逐项对照 `tasks/PHASE1_ENGINEERING_GUARDRAILS.md` §14：

| 验收域 | 要求 | 达成 |
| --- | --- | --- |
| Logging | 基础设施存在 / 启动+关键异常路径实际使用 / 可落盘 / 失败不崩溃 / Secret 不进入日志 | ✅ 全部 |
| Diagnostics | 顶层异常可诊断记录 / 有最小收集脚本 / 不收集敏感正文与 Secret | ✅ 全部 |
| Configuration | 边界有文档 / DPAPI 未被破坏 / 用户数据 schema 未改变 | ✅ 全部 |
| CI | windows-build 全绿 / Build PASS / Compliance PASS / Office Comfort PASS / 新增护栏测试 PASS / Artifact 上传 PASS | ✅ 全部（run 35326969067） |
| Git | 不提交 Secret / 用户数据 / `.agents` / node_modules / 诊断包 / 本机日志 / 临时构建文件 | ✅（草案扫描 + `.gitignore` 挡位 + 提交核对） |
| Behavior | 未改变桌宠/任务/中转袋/AI/ImageEditor/Messenger/Compliance/资源中心/用户数据格式/云 API | ✅ 仅新增基础设施并追加一行到既有钩子，未改业务逻辑 |

**结论：Phase 1 全部验收标准满足。**

## 13. 需要规划角色判定的事项

1. **`skills-lock.json` computedHash**：是否定义正式算法（建议：规范 `SHA-256 over SKILL.md` 的编码/规范化口径）、现有 4 个不匹配哈希的回填/迁移策略、mismatch 失败策略（阻断 vs 警告 vs 缓存回退）。见 `docs/ENGINEERING_BACKLOG.md` §A。
2. **时间戳备份与分发策略**：`artifacts\MomoPet.exe = 最新成功构建` 已固化；时间戳备份/保留 N 份具体策略待 Release 阶段定稿（是否采纳此前“timestamp backup、保留最近 5 版”的记录）。见 backlog §B。
3. **分支保护**：是否由规划角色为 `main` 启用 branch protection（要求 `windows-build` 通过）。执行侧未擅自修改仓库保护规则。
4. **本机 SDK 缺失**：是否维持“依赖 CI 验证”作为执行侧默认路径，还是需在本机安装 SDK 以支持离线验证。
5. **`windows-build` 是否正式取代旧入口**：工作流已作为质量门禁，规划角色可确认是否在仓库设置引入 required check。
6. **本阶段是否归档**：由规划角色依据本报告与 CI 结果决定；执行侧不自行归档。

---

> 执行流程符合 AGENTS.md → CURRENT_TASK.md → PHASE1_ENGINEERING_GUARDRAILS.md；未进入 Phase 2，未扩大范围。所有改动均已推送到 `main`，CI 以真实 run `35326969067`（conclusion=success）验证。

---

## 14. 规划角色最终验收与判定

最终验收：✅ **Phase 1 PASS，可归档。**

真实 CI 校准：
- commit：`d789c6023e4e14a7a6c12c34d29f5b34e92e4cdc`
- GitHub Actions run：`35326969067`
- conclusion：`success`
- Checkout / Bootstrap / Build / Test / Upload artifacts：全部 success
- `momopet-artifacts`：已成功生成

对第 13 节待判定事项的正式结论：

1. **skills-lock.json / computedHash**：继续进入 Engineering Backlog，不在 Phase 1 追做；不批准简单 `SHA-256(SKILL.md)` 直接成为最终规范。后续专项优先评估 deterministic directory hash + 固定 `sourceRef` / commit SHA。
2. **时间戳备份 / 分发策略**：进入 Release / Updater 专项；当前继续保持 `artifacts\\MomoPet.exe` 代表最新成功构建标准产物。
3. **main 分支保护**：建议后续启用 `windows-build / build-test` required check，但本次验收不修改仓库保护规则。
4. **本机 Windows SDK**：不阻塞；正式验收默认依赖 GitHub Actions。需要离线本机构建时再人工安装 SDK。
5. **windows-build**：正式确认为远端 CI 质量门禁；本地 `scripts/verify.ps1` 继续作为本地验证入口，两者并存。
6. **Phase 1**：批准归档，下一阶段进入 Phase 2A — ImageEditor Boundary。

Phase 1 不再追加新实现。后续新增工程能力必须进入新的任务文件。
