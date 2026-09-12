# 参与 MomoPet 开发

欢迎提交问题、界面建议、皮肤素材和代码改进。

## 开始前

- 系统：Windows 10/11（64 位）
- 运行环境：Windows PowerShell、.NET Framework 4.x
- 请勿把 API Key、Base URL 中的私密参数、用户文档或 `%LOCALAPPDATA%\MomoPet` 下的数据提交到仓库

## 推荐流程

1. Fork 仓库并从 `main` 创建功能分支。
2. 只修改与本次问题相关的文件，保留现有中文界面和既有功能。
3. 运行 `powershell -ExecutionPolicy Bypass -File .\build.ps1`。
4. 启动新生成的 `MomoPet.next-*.exe`，实际验证桌宠动作、窗口交互和受影响功能。
5. 提交 Pull Request，说明改动、验证方法；界面改动请附前后截图。

## 皮肤资源

程序实际使用的资源位于 `assets/normalized/skin-motion/<皮肤名>/`。每套皮肤应保持动作名称和画布规范一致，背景必须为真实 Alpha 透明，主体轮廓不得被裁切，也不得残留其他角色或碎片。

## 提交问题

请写清系统版本、复现步骤、预期表现、实际表现，并在方便时附截图。请先遮盖账号、客户、文件名和密钥等隐私信息。
