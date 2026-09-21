# MomoPet Phase 2A：ImageEditor Boundary

状态：CURRENT / AUTHORIZED

目标仓库：`guogracey0-png/MomoPet`

前置基线：
- Phase 0：PASS / ARCHIVED
- Phase 1：PASS / ARCHIVED
- Phase 1 最终功能基线：`d789c6023e4e14a7a6c12c34d29f5b34e92e4cdc`
- 正式 CI 门禁：`windows-build`

---

## 1. 任务定位

这是 Phase 2 架构拆分的第一步。

当前 `src/ImageEditor.cs` 是一个大型 `partial PetController`，同时承载：

- 图像编辑器状态
- WPF UI 构建与事件绑定
- 图像配置
- Provider / Profile
- DPAPI 密钥读写
- 本地裁切 / 尺寸 / 压缩
- OCR / 文字选择
- AI 图像任务
- 精确局部编辑
- 图层合成
- 撤销 / 恢复历史
- 临时文件与任务状态

本阶段目标不是重写 ImageEditor，而是：

> **先把单文件职责按稳定边界拆开，保持同一个 partial PetController 和完全相同的用户行为，为后续真正的 Service 抽取建立安全接缝。**

---

## 2. 核心原则

本轮采用 **Move-first / Refactor-later**。

优先“移动代码”，而不是“重新设计代码”。

执行侧应尽量做到：

```text
原逻辑
→ 原签名
→ 原字段
→ 原事件
→ 原字符串
→ 原数据路径
→ 原网络请求
→ 原 DPAPI 行为
```

只改变代码所在文件和必要的编译文件列表。

本阶段不要求立即形成完美架构。

---

## 3. 明确禁止

本阶段禁止：

- 把 ImageEditor 改写成新的完整 Service 架构
- 修改用户可见 UI / 文案 / 布局
- 修改 ImageEditor 功能
- 修改 AI Provider 行为
- 修改 Base URL / Model 默认值
- 修改请求 JSON / wind_bridge 协议
- 修改 DPAPI 加密方式、熵、文件名
- 修改用户配置 schema
- 修改已有 image settings JSON schema
- 修改临时目录 / 备份目录语义
- 重写 OCR
- 重写裁切算法
- 重写压缩算法
- 重写图层合成
- 重写历史记录机制
- 修改 Messenger / Compliance / PetController 其他职责
- 引入 MVVM 大改
- 引入第三方框架
- .NET 迁移
- 大面积格式化

如发现问题，只登记，不顺手修业务行为。

---

## 4. 当前必须保持不变的兼容面

至少明确核对并保持：

### 配置与文件

- `image-ai-settings.json`
- `image-text-key.dat`
- `image-model-key.dat`
- `toapis-text-key.dat`
- `toapis-image-key.dat`
- `precision-image-key.dat`
- profile key 文件命名
- `ImageBackups`
- `ImageAiTemp`
- `ImageOcrCache`

### 安全

- DPAPI `DataProtectionScope.CurrentUser`
- 现有 entropy 生成方式
- Secret 不进入日志
- Secret 不进入 repo / artifact 文本

### 行为

- 打开图片
- 本地裁切 / 尺寸 / 压缩
- 下载当前画布
- Undo / Redo / Restore
- OCR / 图片改字
- AI 创作
- 参考图
- 精确局部编辑
- 图层相关操作
- Provider / Profile 切换
- Prompt 记忆
- Retry last image task
- 临时文件清理

### 默认配置

不得主动改写当前默认 Provider / URL / Model / quality / size / precision 等默认值。

---

## 5. P2A-01：先建立 ImageEditor 文件边界

将当前 `src/ImageEditor.cs` 中的职责按 partial 文件拆分。

建议目标结构如下；允许根据实际代码依赖微调文件名，但职责必须清晰：

```text
src/
  ImageEditor.Models.cs
  ImageEditor.State.cs
  ImageEditor.Ui.cs
  ImageEditor.LocalEditing.cs
  ImageEditor.AiConfig.cs
  ImageEditor.AiTasks.cs
  ImageEditor.Ocr.cs
  ImageEditor.Precision.cs
```

不要求机械地一定是 8 个文件。

要求：

- 至少拆成 **5 个职责文件**
- 不允许只是把整个文件等分切成 Part1 / Part2 / Part3
- 文件名必须能表达职责
- `PetController` 仍使用 partial
- 跨文件 private 字段 / private 方法可以继续保持现有访问方式
- 不为了“更漂亮”批量改方法签名

---

## 6. P2A-02：模型 / DTO 独立

优先将目前文件顶部的纯数据类型移到独立文件，例如：

- `ImageTextRegion`
- `PrecisionLayer`
- `PsdExportLayer`
- `ImageTextCache`
- `ImageAiConfig`
- `ImageProviderProfile`
- `ImageHistoryEntry`

要求：

- public/internal 可见性除非编译要求，不主动改变
- 属性名不改
- JSON 序列化字段不改
- 默认构造行为不改
- 不做 DTO “美化重构”

---

## 7. P2A-03：状态与 UI 边界

将 ImageEditor 专属字段与 WPF UI 构建逻辑形成明确边界。

UI 文件应主要包含：

- `BuildImageEditor`
- ImageEditor UI element 创建
- UI 事件绑定
- tool tab / panel / settings panel 视图构建
- 纯 UI layout helper

要求：

- 不改变布局
- 不改变尺寸
- 不改变颜色
- 不改变文案
- 不改变事件绑定结果

状态字段可单独放到 `ImageEditor.State.cs`；不要把非 ImageEditor 的 PetController 状态搬入。

---

## 8. P2A-04：AI 配置 / Secret 边界

将以下职责与 UI 大段代码隔离：

- `InitializeImageEditorPaths`
- Image AI config load/save
- Provider / Profile migration
- Active profile selection
- Image/Text/Profile key 文件路径
- DPAPI load/save

本阶段仍允许它们作为 `PetController` partial 私有方法存在。

**不要在本轮改造成新 Secret Store / Repository。**

原因：这些逻辑带有历史迁移和兼容语义，先移动边界，再在后续阶段决定是否抽 Service。

要求：

- 原文件名不变
- DPAPI 行为不变
- migration 顺序不变
- 现有用户配置兼容性不变

---

## 9. P2A-05：本地编辑边界

将纯本地图片编辑相关方法形成独立职责文件，包括实际存在的：

- bitmap load / encode / decode
- crop
- resize
- compression
- alpha / canvas facts
- local save / download
- image history / undo / redo / restore

具体归组以现有代码为准。

要求：

- 算法不重写
- 输出格式不改变
- PNG 透明行为不改变
- JPEG 行为不改变
- 压缩结果策略不改变

---

## 10. P2A-06：OCR 与文字编辑边界

将：

- OCR process / cache
- OCR precache
- text regions
- text selection
- image text layer
- 图片文字替换相关逻辑

从 UI / AI task 主文件中隔离。

要求：

- 不改变 OCR helper 协议
- 不改变 cache 路径与语义
- 不改变文本选择行为
- 不修改现有文字替换业务流程

---

## 11. P2A-07：AI Task 与 Precision 边界

将 AI 调用任务与精确编辑/图层职责形成清晰文件边界。

可分别形成：

- ImageEditor.AiTasks.cs
- ImageEditor.Precision.cs

至少覆盖实际存在的：

- RunImageHelper
- TrackImageTask
- FinishImageTask
- RetryLastImageTask
- HandleGeneratedImageResponse
- precision request / response
- precision selection
- precision layers / composition

要求：

- 不修改 wind_bridge 请求字段
- 不修改 model routing
- 不修改 retry 语义
- 不修改临时文件生命周期
- 不改变 layer ordering / coordinates / scaling

---

## 12. P2A-08：构建系统同步

Phase 0 构建仍手工维护 C# source list。

拆分后必须同步：

`scripts/build.ps1`

要求：

- 所有新增 `ImageEditor*.cs` 纳入编译
- 不遗漏文件
- 不重新引入固定 SDK 路径
- 不破坏 build-meta
- 保持 `windows-build` 全绿

---

## 13. P2A-09：结构回归测试

新增轻量结构测试，例如：

`test-image-editor-boundary.ps1`

至少检查：

1. ImageEditor 已拆为多个职责文件
2. 关键 DTO 只定义一次
3. `PetController` 的 ImageEditor 代码仍为 partial
4. 构建 source list 包含所有 ImageEditor 文件
5. 关键兼容字符串 / 文件名仍存在且未重复产生冲突
6. 禁止出现 `ImageEditor.Part1.cs` 这类无职责命名
7. 原巨型文件体积显著下降

测试应进入：

`scripts/test.ps1`

CI 中原有测试必须继续：

- Compliance
- Office Comfort
- Engineering Guardrails
- ImageEditor Boundary

注意：

结构测试不能代替 build；C# 编译成功仍是核心门禁。

---

## 14. 规模验收

执行报告必须给出：

- 拆分前 `ImageEditor.cs` 行数 / 字节数
- 拆分后每个 `ImageEditor*.cs` 的行数 / 字节数
- 最大文件尺寸
- 文件职责表

目标不是追求极小文件，而是消除单点巨型文件。

验收原则：

- 不再存在一个同时混合 UI + Secret + OCR + AI + Precision + Local Editing 的单文件
- 至少 5 个职责边界清晰的文件
- 不允许用无意义 PartN 拆分骗过验收

---

## 15. Diff 约束

本阶段属于“结构移动”。

执行侧必须重点检查：

```text
业务逻辑新增量
用户可见文本变化
默认值变化
URL / model 变化
文件路径变化
DPAPI 变化
JSON schema 变化
```

上述项目原则上都应为 **0**。

如果为了编译必须做小调整，报告中逐项解释。

建议尽量保留原代码内容，避免在移动同时重新格式化。

---

## 16. 验收标准

Phase 2A 只有全部满足才允许提交验收。

### Structure

- ImageEditor 至少拆成 5 个有明确职责的文件
- DTO / Model 独立
- UI / Config / Local Editing / OCR / AI-Precision 有可识别边界
- 不存在无意义 PartN 拆分
- 原巨型 ImageEditor.cs 明显缩小或转为较薄入口

### Compatibility

- 配置文件名不变
- DPAPI 不变
- JSON schema 不变
- 用户数据路径不变
- Provider / Model 默认值不变
- wind_bridge 协议不变
- UI 文案 / 布局不变
- 业务行为不主动改变

### Tests

- Compliance PASS
- Office Comfort PASS
- Engineering Guardrails PASS
- ImageEditor Boundary PASS
- OVERALL PASS

### CI

- `windows-build` conclusion=success
- Build success
- Test success
- Artifact upload success

### Git

不得提交：
- Secret
- 用户数据
- `.agents`
- node_modules
- logs
- diagnostics 实际包
- 临时产物

---

## 17. 明确不在本阶段做的“下一步”

即使拆分完成，也不要继续做：

- ImageAiConfigService
- ImageSecretStore
- ImageTaskService
- ImageOcrService
- ImageEditingService
- MVVM
- interface / DI 全面改造

这些是否值得做，要等 Phase 2A 的拆分结果和依赖图出来后，由规划角色重新决定。

---

## 18. 执行报告

完成后新增：

`docs/PHASE2A_EXECUTION_REPORT.md`

必须包含：

1. 修改文件列表
2. 每个文件职责
3. 拆分前/后文件规模
4. `git diff --stat`
5. 是否存在非纯移动的逻辑修改
6. 配置 / DPAPI / schema / URL / model / UI 兼容性核对
7. 测试结果
8. 真实 CI run ID / commit / conclusion
9. artifact 结果
10. 未完成项
11. 风险项
12. 下一步可抽 Service 的候选边界（只分析，不实现）
13. 是否满足全部 Phase 2A 验收标准
14. 需要规划角色判断的事项

不得自行进入 Phase 2B。

---

## 19. 执行指令

执行工程师必须：

1. 同步最新 main
2. 阅读 `AGENTS.md`
3. 阅读 `tasks/CURRENT_TASK.md`
4. 完整阅读本文档
5. 先盘点 ImageEditor 现有职责和方法分组
6. 以“移动优先、逻辑不变”完成拆分
7. 更新构建 source list
8. 新增结构回归测试
9. 运行全部测试
10. push 并等待真实 GitHub Actions
11. 生成并提交 `docs/PHASE2A_EXECUTION_REPORT.md`
12. 等待规划角色验收

禁止自行扩展到 Phase 2B。
