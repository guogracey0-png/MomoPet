# MomoPet 毛毛桌宠

MomoPet 是一个原生 Windows 桌面提醒与 AI 办公桌宠。小猫负责轻量陪伴和快捷入口，工作簿、中转袋、盯盘、AI 对话与图片、合规审核等功能集中在独立工作界面中。

项目使用 C# / WPF 构建，不依赖 Electron、浏览器内核或数据库。任务与本地设置默认保存在 `%LOCALAPPDATA%\MomoPet`，不会随源码提交。

## 主要功能

- 桌宠：拖动、贴边、自动行走、提醒反馈、悬停快捷气泡和多套可切换皮肤
- 工作簿：记录事项、设置提醒、完成和整理任务
- 中转袋：暂存文本、图片和文件，支持预览、筛选、搜索及再次拖出
- AI 工作台：对话、联网搜索、图片生成与编辑；可保存多个服务来源并按任务切换
- AI 社区：发布实践分享、浏览动态、加入主题小组、查看周报和收藏内容
- 账号与来信：复用社区账号；好友的小猫会按对方皮肤叼信入场，点击收信后返回已读回执
- AI 应用：独立的应用广场与本地应用管理，可关联并从桌宠直接运行应用
- 资源中心：统一整理、检索和收藏 Skill 与图片、文档、模板等素材
- 合规审核：本地规则初筛、模型语境复核、人工确认、原文定位与报告导出
- 办公辅助：OCR、快速入口和本地桥接能力

## 运行

发布版用户可双击 `启动博道咪.vbs`（也兼容 `启动毛毛.vbs`）。启动器会在同一目录中选择最新的 `MomoPet.exe` 或 `MomoPet.next-*.exe`。

首次运行时，单文件程序会把必要的 OCR 助手和桥接脚本释放到 `%LOCALAPPDATA%\MomoPet\app`。

## 从源码构建

要求：

- Windows 10/11 64 位
- .NET Framework 4.x
- Windows 10/11 SDK（用于本地 OCR）

在 Windows PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

为避免覆盖正在运行的程序，构建脚本会生成带时间戳的 `MomoPet.next-*.exe`。生成文件不会提交到 Git；需要分发时请把它作为 GitHub Release 附件发布。

## 目录

```text
src/                         WPF 主程序源码
assets/normalized/           当前正式使用的桌宠动作与皮肤资源
wind_bridge/                 AI 与数据服务桥接脚本
cloud/cafe-server-patch/     博知汇阿里云来信服务增量补丁
tools/                       皮肤资源检查和维护工具
compliance-rules.txt         本地合规规则
build.ps1                    单文件构建入口
```

## 隐私与密钥

API Key 和服务来源应由每位用户在应用设置中自行填写。请勿把真实密钥、内部地址、客户资料或本机数据提交到 Issue、Pull Request 或源码。

## 参与贡献

欢迎通过 Issue 反馈问题，通过 Pull Request 一起改进。具体要求见 [CONTRIBUTING.md](CONTRIBUTING.md)。

## 许可证

代码以 [MIT License](LICENSE) 开源。第三方模型、接口、金融数据和用户自行导入的素材仍受各自条款约束。
