# 博知汇 MomoPet 来信服务补丁

这部分代码给现有博知汇 Express + 阿里云 Tablestore 服务增加安全的 MomoPet 会话和私信能力。

- 复用既有成员账号与密码哈希，不复制用户表。
- 使用独立、可撤销、30 天过期的随机会话令牌。
- 收件箱和发件箱分别建表，支持“已送达 / 已收信”回执。
- 发送者皮肤由服务端资料读取，客户端不能伪装成其他人的皮肤。
- AI 社区的帖子、Skill 分享和 AI 应用分享统一存放在云端；本地仅保留用户下载的副本。
- 阿里云 AccessKey 只存在于服务端环境变量，不进入 MomoPet.exe 或 GitHub。

需要将 `src` 中的文件合并进现有 `cafe-server`，在 `index.ts` 和 `desktop-entry.ts` 挂载：

```ts
import { momoRouter } from "./routes/momo.js";
app.use("/api/momo", momoRouter);
```

部署前执行 `npx tsx src/scripts/initMomoTables.ts` 创建来信、小组与社区所需的九张增量表；该脚本不会删除或修改旧表。
