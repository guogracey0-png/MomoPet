# MomoPet Phase 2A：ImageEditor Boundary 执行报告

状态：✅ 执行完成 / ⏳ 待规划角色验收（未归档，不进入 Phase 2B）

- 目标仓库：`guogracey0-png/MomoPet`
- 分支 / 基线：`main` @ `ae43756`（进入本阶段）→ `dbb2cf6`（本阶段最终提交）
- 任务文档：`tasks/PHASE2A_IMAGE_EDITOR_BOUNDARY.md`
- 核心原则：**Move-first / Refactor-later**（只移动代码，不重建设计）
- 执行者：执行工程师（本会话）；规划角色 / 评估方另开会话验收

> 本报告如实记录 Phase 2A 的文件拆分、兼容性核对、测试与 **真实 GitHub Actions run** 结论。凡结论均来自真实 CI，未用本机推演替代。

---

## 0. 结论速览

| 验收项 | 结论 |
| --- | --- |
| P2A-01 建立 ImageEditor 文件边界 | ✅ 达成（7 个职责文件 + 基础 Ui 文件；≥5 要求满足） |
| P2A-02 模型 / DTO 独立 | ✅ 达成（`ImageEditor.Models.cs`，DTO 仅定义一次） |
| P2A-03 状态与 UI 边界 | ✅ 达成（`State.cs` 承载状态/历史；`ImageEditor.cs` 承载 UI） |
| P2A-04 AI 配置 / Secret 边界 | ✅ 达成（`AiConfig.cs` 承载路径/Provider/DPAPI/设置面板） |
| PetController 保持 partial | ✅ 达成（单类型跨文件拆分，可见性不变） |
| 用户行为 / schema / Secret / 默认值不变 | ✅ 达成（逐项核对，均为纯移动） |
| 更新 build source list | ✅ 达成（`scripts/build.ps1` 纳入全部 8 个文件） |
| ImageEditor Boundary 结构测试 | ✅ 达成（`test-image-editor-boundary.ps1`，接入 `test.ps1`） |
| 全量测试 | ✅ 4/4 套件通过（compliance / office-comfort / engineer-guardrail / image-editor-boundary） |
| 真实 CI 全绿 | ✅ **run `35575677386`，commit `dbb2cf6`，conclusion=success**（windows-build / build-test） |
| Artifact | ✅ `momopet-artifacts`（≈59.3 MB，未过期） |

---

## 1. 拆分前后文件规模

### 拆分前
- `src/ImageEditor.cs`：**1575 行**，实体承载 **7 个模型/DTO 类型 + 264 个 PetController 成员**（字段、方法、属性），职责混杂：状态 / UI 构建 / Provider–Profile / DPAPI / 裁切尺寸压缩 / OCR / AI 任务 / 精确编辑 / 图层 / 历史与临时文件管理。

### 拆分后（8 个文件，均保持 `partial PetController`，除纯 DTO 文件外）

| 文件 | 行数 | 字节 | 说明 |
| --- | --- | --- | --- |
| `ImageEditor.cs` | 340 | 60,040 | **Ui**：窗口构建 `BuildImageEditor`、tab/面板/layout helper、工具栏切换、上传/下载/参考图 UI |
| `ImageEditor.Models.cs` | 87 | 4,420 | **DTO**：`ImageTextRegion` / `PrecisionLayer` / `PsdExportLayer` / `ImageTextCache` / `ImageAiConfig` / `ImageProviderProfile` / `ImageHistoryEntry` |
| `ImageEditor.State.cs` | 164 | 9,761 | **状态与历史**：编辑器状态字段 + Undo/Redo/Restore/纵切历史（`PushImageState` 等） |
| `ImageEditor.LocalEditing.cs` | 259 | 25,070 | **本地编辑**：裁切 / 尺寸 / 压缩 / 编码（`ApplyCrop`、`EncodeBitmap`、`CompressPreview` 等） |
| `ImageEditor.AiConfig.cs` | 348 | 49,841 | **AI 配置 / Secret**：路径初始化、Provider/Profile 迁移与选择、DPAPI 密钥读写、AI 设置面板 |
| `ImageEditor.AiTasks.cs` | 111 | 15,631 | **AI 任务**：生成 / 改字 / 辅助请求编排（`RunImageGenerate`、`RunImageEdit`、`RunImageHelper`） |
| `ImageEditor.Ocr.cs` | 171 | 16,312 | **OCR / 文字选择**：本地/远程 OCR、文字缓存、文字层选择交互 |
| `ImageEditor.Precision.cs` | 383 | 51,681 | **精确编辑**：标记 / 图层 / PSD/ZIP 导出 / 图层合成请求与响应 |

合计拆分后源码 ≈ 1863 行（含各文件重复的 using/namespace 头，约 +160 行为拆分必需的样板开销）；业务成员正文逐字节保留。

---

## 2. 每个文件的职责边界（详见上表右侧列）

- **ImageEditor.cs**：只保留 UI 构建与交互编排，不承载数据 / 密钥 / 算法。
- **ImageEditor.Models.cs**：纯数据类型，无行为；JSON 序列化字段 / 属性名 / 默认构造不变。
- **ImageEditor.State.cs**：编辑器专属字段 + 撤销/恢复状态机；未迁入任何非 ImageEditor 的 PetController 状态。
- **ImageEditor.LocalEditing.cs**：本地像素处理（裁切/尺寸/JPEG-PNG 压缩/Alpha 展平），不含网络。
- **ImageEditor.AiConfig.cs**：配置/密钥/Profile 解析与持久化；DPAPI `CurrentUser` 与熵在此边界的唯一归属。
- **ImageEditor.AiTasks.cs**：AI 请求的生命周期（track/finish/retry）与生成/改字编排。
- **ImageEditor.Ocr.cs**：本地 OCR 进程 + 远程识别 + 文字缓存，产出 `LocalOcrOutput` 消费逻辑。
- **ImageEditor.Precision.cs**：精确标记坐标、图层模型、图层/PSD/ZIP 导出、图层合成请求。

---

## 3. git diff --stat（commit `dbb2cf6` vs 父提交）

```
 scripts/build.ps1               |    2 +-
 scripts/test.ps1                |    1 +
 src/ImageEditor.AiConfig.cs     |  347 +++++++++++
 src/ImageEditor.AiTasks.cs      |  110 ++++
 src/ImageEditor.LocalEditing.cs |  258 ++++++++
 src/ImageEditor.Models.cs       |   86 +++
 src/ImageEditor.Ocr.cs          |  170 ++++++
 src/ImageEditor.Precision.cs    |  382 ++++++++++++
 src/ImageEditor.State.cs        |  163 +++++
 src/ImageEditor.cs              | 1248 +--------------------------------------
 test-image-editor-boundary.ps1  |   64 ++
 11 files changed, 1588 insertions(+), 1243 deletions(-)
```

新增为职责文件的 `+` 与 `src/ImageEditor.cs` 的大段 `-` 严格一一对应（净移动，无逻辑改写）。

---

## 4. 非纯移动逻辑修改（须如实登记）

| 修改点 | 类型 | 说明 |
| --- | --- | --- |
| 各职责文件追加 `using` 头 + `namespace/class` 壳 | 结构性 | 为实现跨文件 partial 拆分所必需，无功能影响 |
| 成员之间以空行分隔 | 结构性 | 仅空白，不改变语法/语义 |
| 拆分为 8 个文件后在 `ImageEditor.cs` 保留 Ui | 结构性 | 该文件行数由 1575 → 340 |
| `scripts/build.ps1` $sourceFiles 增加 7 个新文件 | 编译列表 | 必须纳入新文件方可编译 |
| `test-image-editor-boundary.ps1`（新增测试） | 新增 | 纯结构回归，不触碰运行逻辑 |

**无任何业务逻辑改动**：无 DTO、无算法、无字符串、无数据路径、无网络请求、无密钥行为的改写；全部为“移动代码”。

---

## 5. 兼容性核对（禁改面逐项确认）

| 兼容面 | 核对结果 |
| --- | --- |
| 配置/密钥文件名（`image-ai-settings.json`、`image-text-key.dat`、`image-model-key.dat`、`toapis-text-key.dat`、`toapis-image-key.dat`、`precision-image-key.dat`、`image-profile-{id}.key`） | 原样保留（位于 `AiConfig.cs`） |
| 目录语义（`ImageBackups`、`ImageAiTemp`、`ImageOcrCache`） | 原样保留 |
| DPAPI `DataProtectionScope.CurrentUser` | 原样保留（仅 `AiConfig.cs` 持有） |
| DPAPI 熵（`"MomoPet.ImageAi."+kind+".v1"`、`"MomoPet.PrecisionImage.v1"`） | 原样保留 |
| 默认 Provider/URL/Model/质量/尺寸（`"官方 API"`、`https://toapis.com/v1` 等） | 原样保留，未触碰默认值 |
| JSON schema / Profile 序列化字段 | 原样保留（DTO 仅移动） |
| OCR / 裁切 / 压缩 / 图层算法 | 原样保留（正文逐字节移动） |
| UI 文案 / 布局 / 事件绑定 | 原样保留（`BuildImageEditor` 等原样移动） |
| Secret 不入日志 / 不入 repo/artifact 文本 | 沿用既有策略，本阶段未引入新 Secret |

---

## 6. ImageEditor Boundary 结构测试

新增 `test-image-editor-boundary.ps1`（仓库根，接入 `scripts/test.ps1`）。校验项（全部通过）：

- 存在 ≥5 个职责文件（实测 7 个）。
- 无 `PartN` 平凡切分文件。
- 除纯 DTO 文件外，每个文件均声明 `public partial class PetController`。
- 7 个模型/DTO 各在源码集中**恰好定义一次**。
- `scripts/build.ps1` 的 source list 覆盖全部 8 个 `ImageEditor*.cs`。
- 每个职责文件均非空（携带实际类成员）。

---

## 7. 全量测试（本地 + 真实 CI）

- 本地编译门禁 `artifacts/devcheck.ps1`（WPF 引用子集）——**通过**（本机无 Windows SDK）。
- **真实 CI**（`windows-build`，windows-latest，含 SDK 的完整构建）四套件全过：

```
[OK] Compliance regression tests              - passed (exit 0)
[OK] Office comfort regression tests          - passed (exit 0)
[OK] Engineering guardrail tests              - passed (exit 0)
[OK] ImageEditor boundary structure tests     - passed (exit 0)
[OK] test: all regression tests passed.
```

---

## 8. 真实 CI run 与 Artifact（非本机推演）

| 项目 | 值 |
| --- | --- |
| CI run | `35575677386` |
| 触发提交 | `dbb2cf6`（push → main） |
| 工作流 / Job | `windows-build` → `build-test`（ID `106256896111`） |
| 结论 | **success（59s）** |
| 跳转 | https://github.com/guogracey0-png/MomoPet/actions/runs/35575677386 |
| Artifact | `momopet-artifacts`（≈59,285,669 bytes，未过期） |

> 说明：本机未安装 Windows 10/11 SDK（`Windows.winmd` 缺失），无法本地完整构建；因此本地以 `devcheck.ps1` 作为编译门禁，**最终全绿以真实 GitHub Actions 为准**，符合 AGENTS 的“CI 结论必须来自真实 run”要求。

---

## 9. 风险项与注意事项

1. **🚫 非阻塞警告（Annotation）**：CI 提示 `actions/checkout@v4`、`actions/upload-artifact@v4` 使用已弃用的 Node.js 20，被强制运行于 Node 24。目前不影响结果，但属上游依赖生命周期事项，建议规划角色决定是否在后续阶段升级 action 版本。
2. **本机 SDK 缺失**：本地只能做 WPF 参考子集编译，无法本地完整构建；后续阶段若需本地完整构建，需预装 Windows 10/11 SDK。
3. **文件数增长、diff 偏大**：拆分使单文件历史行数大幅波动（`ImageEditor.cs` 1575→340），git blame/diff 追溯需跨文件，属预期代价。
4. **各文件重复 using 头**：部分文件存在未使用的 `using`，仅产生编译器警告（CS8 client 级，非 error），不影响编译/运行。
5. **拆分面即未来接缝**：当前仅建立边界，尚无运行时抽象；任何后续重构都不得突破 `tasks/PHASE2A_IMAGE_EDITOR_BOUNDARY.md` 的禁改清单。

---

## 10. 下一步可抽 Service 候选边界（仅登记，本阶段不实现）

本次拆分已为 Phase 2B 起的真正 Service 抽取建立安全接缝，候选边界如下（后续由规划角色决定是否纳入 Phase 2B+，**不在本阶段执行**）：

| 候选 Service | 现归属文件 | 边界理由 |
| --- | --- | --- |
| `ImageAiConfigStore` / Provider–Secret 服务 | `AiConfig.cs` | 路径加载/持久化/Profile 解析/DPAPI 已与 UI 隔离 |
| `ImageOcrService` | `Ocr.cs` | 本地/远程 OCR + 文字缓存，UI 与识别解耦 |
| `PrecisionPipelineService` | `Precision.cs` | 标记坐标、图层模型、PSD/ZIP 导出、图层合成 |
| `ImageCodecService` | `LocalEditing.cs` | 编码/压缩/Alpha 展平/量化，纯像素处理 |
| `ImageEditorState` / `ImageHistoryStore` | `State.cs` | 撤销/恢复状态机独立为可测单元 |
| `ImageTaskRunner` | `AiTasks.cs` | 生成/改字/`RunImageHelper` 请求编排 |

> 以上仅登记候选接缝，**本阶段未抽取任何 Service / 未引入 MVVM / 未改签名**，符合 Move-first / Refactor-later。

---

## 11. 登记事项（发现但本阶段不改）

拆分过程中未发现需要“顺手修复”的业务行为缺陷；若评估方在审阅 `AiConfig.cs` / `Precision.cs` 时发现个别可读性问题，均属于登记范畴，按 `tasks/PHASE2A_IMAGE_EDITOR_BOUNDARY.md` 第 3 节“只登记，不顺手修业务行为”处理。

---

## 12. 完成状态

- [x] 建立 ImageEditor 职责边界（≥5 文件，Move-first）
- [x] 模型/DTO 独立
- [x] 状态与 UI 边界
- [x] AI 配置 / Secret 边界
- [x] 更新 build source list
- [x] 新增 ImageEditor Boundary 结构测试
- [x] 全量测试通过（4/4）
- [x] 真实 GitHub Actions 全绿（run 35575677386）
- [x] 生成并提交本报告
- [ ] 规划角色验收（**待完成；验收前不进入 Phase 2B，不归档**）