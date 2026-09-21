# MomoPet Phase 2D：PetController Boundary

状态：CURRENT / AUTHORIZED

目标仓库：`guogracey0-png/MomoPet`

前置基线：
- Phase 0：PASS / ARCHIVED
- Phase 1：PASS / ARCHIVED
- Phase 2A：PASS / ARCHIVED
- Phase 2B：PASS / ARCHIVED
- Phase 2C：PASS / ARCHIVED
- Phase 2C 最终 CI：`7bccac5` / run `35583063767` / success
- 正式 CI 门禁：`windows-build`

## 1. 任务定位

这是 Phase 2 架构边界拆分的最后一阶段。

前面已经完成：
- ImageEditor 边界
- Messenger 边界
- Compliance 边界

当前剩余重点是 `src/MomoPet.cs` 中的主 `partial PetController`。

该文件仍混合：
- 核心状态与路径
- 构造 / Start / Exit 生命周期
- 主桌宠窗口 BuildPet
- 资源与皮肤基础加载
- 任务/提醒 UI、调度、持久化
- 中转袋的主窗口拖入/剪贴板入口
- 市场盯盘状态与调度
- 桌宠动画/状态机/渲染
- edge hide / reaction / toast 等核心交互

本阶段继续采用：

> **Move-first / Refactor-later**

目标不是重写 PetController，而是：

> 把 `MomoPet.cs` 中剩余职责拆成清晰 partial 文件，让 `MomoPet.cs` 最终主要保留 Program / 基础模型与极薄入口，或根据真实依赖保留少量 core glue。

## 2. 明确不重新拆的模块

本轮不得重新设计或大规模移动以下已经独立的模块：

- `ImageEditor*.cs`
- `Messenger*.cs`
- `Compliance*.cs`
- `PetExperience.cs`
- `StashWorkspace.cs`
- `AiPocket.cs`
- `AiSearch.cs`
- `SkinWardrobe.cs`
- `MomoAccount.cs`
- `CommunityCloud.cs`
- `HealthCompanion.cs`
- `OfficeComfort.cs`

如这些文件与 `MomoPet.cs` 有交叉调用，保持现有 partial 类型共享，不顺手重构。

## 3. 最高优先级禁改项

本阶段禁止：

- 修改桌宠行为概率
- 修改 walk/run/hop/sleep/coffee/happy 等状态语义
- 修改动画速度、时长、帧序
- 修改 edge hide 行为
- 修改拖动/点击/抚摸交互
- 修改桌宠窗口大小、位置、布局、文案
- 修改任务 JSON schema
- 修改任务提醒/重复调度算法
- 修改节假日判断
- 修改提醒时间语义
- 修改 stash 数据 schema
- 修改 stash 文件路径、复制、图片下载与 15 MB 限制
- 修改 market alert 规则、时点、阈值、Wind Key 行为
- 修改现有数据文件名
- 修改 `MOMOPET_DATA_DIR` 语义
- 修改 `MomoStorage` 原子写入策略
- 修改 Program 单实例/激活机制
- 修改顶层异常处理语义
- 修改 startup / shutdown 顺序
- 修改 UI 文案、颜色、布局
- 引入 MVVM / DI / Service 大改
- .NET 迁移
- 大规模格式化或重命名

发现问题只登记，不顺手修。

## 4. 建议拆分边界

建议目标结构：

```text
src/
  PetController.State.cs
  PetController.Lifecycle.cs
  PetController.PetShell.cs
  PetController.Tasks.cs
  PetController.StashIngress.cs
  PetController.Market.cs
  PetController.Animation.cs
```

允许根据真实依赖微调命名与合并，但必须满足：

- 至少 **6 个明确职责文件**
- 禁止 Part1 / Part2 / Part3
- `PetController` 继续 partial
- 不为了拆分而改变 public/private 签名
- 不引入新的架构层

`MomoPet.cs` 可以继续保留：
- `MomoPaths`
- `MomoStorage`
- `TaskItem`
- `StashItem`
- `MarketAlertState`
- `MarketIndex`
- `Program`

如果执行侧认为模型单独移动更安全，也必须保持序列化字段和可见性完全不变，并在报告中解释；不是硬性要求。

## 5. P2D-01：State 边界

将仍位于 `MomoPet.cs` 的 PetController 专属字段按主控制器状态集中。

至少覆盖实际存在的：
- app / root / data paths
- JSON serializer
- tasks / stashItems
- pet / panel / stashPanel / marketPanel
- pet sprite / badges / speech controls
- task editor controls
- market controls / state
- timers
- animation state
- edge hide state
- exiting / rendering state

要求：
- 不把其他 partial 文件已经拥有的字段再搬回来
- 字段初始值不变
- readonly / const 不变
- 初始化顺序不变

## 6. P2D-02：Lifecycle 边界

集中：
- `PetController(Application)`
- `Start`
- `Exit`
- 主模块初始化/关闭编排

要求：
- Start 调用顺序逐字保持
- timer interval 不变
- shutdown 顺序不变
- 不新增异步初始化
- 不新增 lazy initialization
- 不改变 Program.Main

## 7. P2D-03：PetShell 边界

集中主桌宠窗口和核心 UI 壳：

- `LoadAssets`
- `LoadSkin`
- `SelectedSkinFrame`
- `AddSkinMenu`
- `ApplySelectedSkin`
- `BuildPet`
- ContextMenu style
- 与主桌宠 shell 直接相关的 helper

要求：
- Window size / Topmost / AllowDrop / tooltip 不变
- 菜单项与顺序不变
- 事件绑定不变
- 不重做 SkinWardrobe
- 不重做 PetExperience

## 8. P2D-04：Tasks / Reminder 边界

把 `MomoPet.cs` 中任务与提醒相关职责集中：

- task panel / editor
- task categories / filter
- repeat / weekday / monthly schedule
- load/save tasks
- next due calculation
- reminder checks
- holiday skip
- reminder toast

要求：
- `tasks.json` 不变
- `TaskItem` schema 不变
- repeat 字符串不变
- weekday 编码不变
- monthly 行为不变
- SkipWeekends / SkipHolidays 不变
- reminder timing 不变
- `ShowReminder` 文案/UI 不变

## 9. P2D-05：StashIngress 边界

注意：`StashWorkspace.cs` 已经存在，本阶段**不重拆 StashWorkspace**。

这里只移动目前仍留在 `MomoPet.cs` 的桌宠入口/数据接收逻辑，例如：

- `CanAcceptStash`
- `AcceptStashDrop`
- clipboard/html/image source detection
- `QueueImageSource`
- `CopyFileIntoStash`
- stash preview / helper（仅实际仍在 MomoPet.cs 的部分）
- 主桌宠 pocket/drop 入口相关 glue

要求：
- `stash.json` schema 不变
- `StashFiles` 路径不变
- 文件复制命名不变
- image URL / data URI 行为不变
- 15 MB 限制不变
- WebClient 行为不变
- 去重 / SaveStash / RefreshStash 时机不变

如果某段实际更适合继续留在 `MomoPet.cs`，可以保留，但必须解释职责边界。

## 10. P2D-06：Market 边界

集中 `MomoPet.cs` 中现有盯盘/行情相关内容：

- market state
- market panel
- Wind Key
- alert schedule
- market fetch
- market state load/save
- alert rendering / status

要求：
- 数据源不变
- Wind Key 存储方式不变
- 请求协议不变
- schedule / threshold / alert timing 不变
- `market-alerts.json` 不变
- 不重写行情功能

## 11. P2D-07：Animation / Behavior 边界

集中主桌宠状态机和渲染：

- `AnimateScale`
- `React`
- `StartState`
- `ChooseBehavior`
- `EnsureRendering` / `StopRendering`
- `OnRendering`
- `AnimatePet`
- hop / frame / motion helper
- edge hide / edge peek
- frame setting / motion frame
- Ease/Smooth 等 animation helper

要求：
- 所有数字常量与 timing 不变
- behavior random thresholds 不变
- frame sequence 不变
- motion math 不变
- state 字符串不变
- edge hide positioning 不变
- 与 `PetExperience.cs` 的调用边界不变

## 12. 构建同步

更新 `scripts/build.ps1`：

- 纳入所有新增 `PetController*.cs`
- 保持现有所有模块 source list
- 不遗漏任何新文件
- 不修改 SDK / Node / artifact 机制

## 13. PetController Boundary 结构测试

新增：

`test-petcontroller-boundary.ps1`

至少检查：

1. 新增 >=6 个职责文件
2. 不存在 PartN
3. 所有 host 保持 `public partial class PetController`
4. build source list 覆盖全部新文件
5. `MomoPet.cs` 显著缩小
6. `Program.Main` 仍在 `MomoPet.cs`
7. `MomoPaths` / `MomoStorage` 仍存在且语义未改
8. 关键生命周期方法位于 Lifecycle 文件
9. 关键 task 方法位于 Tasks 文件
10. 关键 animation 方法位于 Animation 文件
11. 关键 stash ingress 方法位于 StashIngress 文件
12. 关键 market 方法位于 Market 文件
13. 不得出现重复 PetController 成员定义

接入 `scripts/test.ps1`。

CI 必须继续运行：
- Compliance regression
- Office comfort
- Engineering guardrail
- ImageEditor boundary
- Messenger boundary
- Compliance boundary
- PetController boundary

## 14. 成员守恒要求

这是 Phase 2D 的核心验收。

执行侧必须建立自动化守恒校验，证明：

- 拆分前 `MomoPet.cs` 中 PetController 的字段/方法没有丢失
- 除文件壳和 build/test 接入外，业务成员正文原则上 move-only
- 每个成员定义恰好一次
- `Program`、基础 static helper、models 不被误计为 PetController 成员

报告必须给出：
- 拆分前 PetController 字段数 / 方法数
- 拆分后各文件成员数
- 总数核对
- 自动化守恒方法

## 15. Diff / 兼容性验收

报告必须逐项确认原则上为 **0**：

- Program.Main 逻辑变化
- Start/Exit 顺序变化
- timer interval 变化
- task schema / repeat logic 变化
- reminder 逻辑变化
- market schedule / threshold 变化
- stash schema / path / limit 变化
- behavior probability 变化
- animation timing/math 变化
- UI 文案/布局变化
- asset 路径变化
- data file 路径变化

若为了编译必须存在非纯移动调整，逐条列出。

## 16. 已知问题只登记

本阶段可以分析但禁止实施：
- PetController 是否未来需要 Service/Coordinator
- TaskService / MarketService / StashService
- AnimationEngine
- AppLifecycle Coordinator
- MVVM
- DI
- 模型文件进一步拆分
- WebClient / HttpWebRequest 技术债
- UI 重设计
- 性能调优

这些属于 Phase 2 完成后的重新评估项。

## 17. 验收标准

### Structure
- `MomoPet.cs` 的 PetController 主体显著缩小
- >=6 个明确职责 partial 文件
- State / Lifecycle / PetShell / Tasks / StashIngress / Market / Animation 边界可识别
- 无 PartN

### Compatibility
- Program 单实例行为不变
- Startup / Shutdown 不变
- Task / Reminder 不变
- Stash ingress 不变
- Market 不变
- Pet animation / behavior 不变
- UI 不变
- 文件/schema 不变

### Tests
- 所有既有回归 PASS
- PetController Boundary PASS
- OVERALL PASS

### CI
- `windows-build` success
- Build success
- Test success
- Artifact upload success

### Git
不得提交 Secret、用户数据、日志、diagnostics 包、临时产物。

## 18. 执行报告

完成后新增：

`docs/PHASE2D_EXECUTION_REPORT.md`

必须包含：

1. 修改文件列表
2. 每个文件职责
3. 拆分前后规模
4. PetController 字段/方法成员守恒
5. `git diff --stat`
6. 所有非纯移动修改
7. Program/Lifecycle/Task/Stash/Market/Animation/UI 兼容性核对
8. 全量测试
9. 真实 CI run ID / commit / conclusion
10. artifact
11. 风险与已登记问题
12. Phase 2 后续架构候选（只分析，不实现）
13. 是否满足全部验收标准
14. 需要规划角色判定的事项

不得自行进入 Phase 3。

## 19. 执行指令

1. 同步最新 main
2. 阅读 `AGENTS.md`
3. 阅读 `tasks/CURRENT_TASK.md`
4. 完整阅读本文档
5. 先盘点 `src/MomoPet.cs` 中 PetController 字段和方法
6. 建立成员守恒基线
7. 按 Move-first 拆分
8. 更新 build source list
9. 新增 PetController Boundary 结构测试
10. 运行全部测试
11. push 并等待真实 GitHub Actions
12. 生成并提交 `docs/PHASE2D_EXECUTION_REPORT.md`
13. 等待规划角色验收

禁止自行扩展到 Phase 3。
