# MomoPet 配置与 Secret 边界

状态：Phase 1（P1-04）。本文件明确“普通配置 / Secret / 用户状态 / 构建配置”的边界与规则，**不要求一次性改造历史配置代码**。

## 1. 边界总览

| 类别 | 说明 | 存放位置 | 可提交？ |
| --- | --- | --- | --- |
| 普通配置 | 非敏感运行参数、路径、功能开关 | 源码常量 / AppPaths / AppLog 设置；`docs/` 中的工程配置 | 可提交 |
| Secret | API Key / Token / AccessKey / 口令 | 运行期内存 + DPAPI 加密文件（当前用户作用域） | **禁止** |
| 用户状态 | 任务、中转袋、标记状态、社区数据、日志 | `%LOCALAPPDATA%\MomoPet`（`MomoPaths.DataDir()`，支持 `MOMOPET_DATA_DIR` 覆盖） | 禁止（本机私有） |
| 构建配置 | 编译、依赖恢复、CI、技能锁 | `scripts/`、`.github/workflows/`、`skills-lock.json`、`compliance-rules.txt` | 可提交 |

## 2. Secret 盘点（来源与加密方式）

所有用户第三方密钥均在应用内按需保存，使用 Windows **DPAPI（`DataProtectionScope.CurrentUser`）** 加密到 `%LOCALAPPDATA%\MomoPet`，并带进程级熵字符串。**不写入仓库、日志、build-info、CI artifact**。

| 密钥 | 组件 | 存储文件（DPAPI 加密） | 熵常量 |
| --- | --- | --- | --- |
| Wind 行情/授权 Key | `MomoPet.cs` | `wind-key.dat` | `MomoPet.Wind.ApiKey.v1` |
| AI 语言模型 Key | `AiSearch.cs` | 运行期保存 | `MomoPet.Llm.ApiKey.v1` |
| 图片模型 / 文本 Key | `ImageEditor.cs` | 图像/文本密钥文件 | `MomoPet.ImageAi.*.v1` |
| 精确图像 Key | `ImageEditor.cs` | `precisionImageKeyFile` | `MomoPet.PrecisionImage.v1` |
| 图像 Profile Key | `ImageEditor.cs` | `image-profile-<id>.key` | `MomoPet.ImageAi.Profile.*.v1` |
| 账号 Token | `MomoAccount.cs` | Token 文件 | `MomoPet.Account.Token.v1` |
| 桥接脚本中的 LLM Key | `wind_bridge/image_ai.mjs` | 运行期环境变量 `TEXT_LLM_API_KEY` | —（内存态，不下发日志） |

> 注：`wind_bridge` 中的密钥来自 `process.env.TEXT_LLM_API_KEY` 与调用方传入（由 C# 侧 DPAPI 解密），未见硬编码密钥。

## 3. 盘点结论

- **未发现**硬编码的真实 Secret / Token / AccessKey 提交在仓库跟踪文件中（已对 `src/`、`wind_bridge/`、仓库根扫描 `sk-*`、`Bearer *`、长密钥赋值与凭据文件后缀，均无命中）。
- 若读者发现疑似硬编码 Secret，**立即停止扩散**，在执行报告中标注为高风险；不要在任何报告/Issue/PR 中粘贴真实值。
- 保留现有 DPAPI 存储方式，Phase 1 不做迁移。

## 4. 规则（对后续所有改动强制）

1. 新增密钥一律走 DPAPI + 熵，不写进源码字面量。
2. `AppLog` 不记录 Secret；渲染日志的字段若可能与密钥混同，先 `AppLog.RegisterSensitive(token, label)` 登记，写日志自动遮罩为 `[REDACTED:label]`。
3. `scripts/collect-diagnostics.ps1` 生成诊断包前强制执行 Scrub，只拷贝 `momo-*.log`，绝不收集 DPAPI 加密文件、聊天/用户文档正文、Cookie。
4. `build-info.json`、`.github/workflows`、README、各类报告一律不得含真实 Secret。
5. 日志目录/%LOCALAPPDATA% 内文件都被 `.gitignore` 覆盖（`*.log`、`artifacts/` 等），不得 `git add -f` 强行提交。

## 5. 相关实现

- 路径集中化：`src/AppPaths.cs`（数据根沿 `MomoPaths.DataDir()`），日志目录 `%LOCALAPPDATA%\MomoPet\logs`，禁止新代码再散落 `%LOCALAPPDATA%\MomoPet` 字符串（P1-03）。
- 统一日志 + 遮罩：`src/AppLog.cs`（P1-01）。
- 诊断包：`scripts/collect-diagnostics.ps1`（P1-08）。