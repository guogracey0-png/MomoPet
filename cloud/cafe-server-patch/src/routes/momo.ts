import { Router, type Request, type Response, type NextFunction } from "express";
import { memberRepo } from "../repositories/memberRepo.js";
import { momoRepo, safeSkin } from "../repositories/momoRepo.js";
import { asyncHandler } from "../utils/asyncHandler.js";

export const momoRouter = Router();

function bearer(req: Request) {
  const value = String(req.headers.authorization || "");
  return value.startsWith("Bearer ") ? value.slice(7).trim() : value.trim();
}

async function requireMember(req: Request, res: Response, next: NextFunction) {
  const id = await momoRepo.resolveSession(bearer(req));
  if (!id) { res.status(401).json({ error: "登录已过期，请重新登录" });return; }
  (res.locals as any).memberId = id;next();
}

function publicMember(member: any, skinId = "default") {
  return { id: member.id, username: member.username, nickname: member.nickname, avatar: member.avatar, bio: member.bio, role: member.role, joinedAt: member.joinedAt, signature: member.signature, skinId };
}

async function finishLogin(member: any, skinId: unknown, res: Response, status = 200) {
  const skin = safeSkin(skinId);await momoRepo.setSkin(member.id, skin);const token = await momoRepo.createSession(member.id);res.status(status).json({ member: publicMember(member, skin), token });
}

momoRouter.post("/auth/register", asyncHandler(async (req, res) => {
  const { username, password, nickname, skinId } = req.body || {};
  if (!/^[a-zA-Z0-9_]{3,20}$/.test(String(username || ""))) { res.status(400).json({ error: "账号需为 3-20 位字母、数字或下划线" });return; }
  if (String(password || "").length < 6) { res.status(400).json({ error: "密码至少 6 位" });return; }
  if (!String(nickname || "").trim()) { res.status(400).json({ error: "昵称必填" });return; }
  if (await memberRepo.getByUsername(String(username))) { res.status(409).json({ error: "账号已存在" });return; }
  const member = await memberRepo.createAccount({ username: String(username), password: String(password), nickname: String(nickname).trim().slice(0,30), avatar: "🐾", bio: "MomoPet 用户", signature: "让小猫替我送信" });
  await finishLogin(member, skinId, res, 201);
}));

momoRouter.post("/auth/login", asyncHandler(async (req, res) => {
  const member = await memberRepo.verifyCredentials(String(req.body?.username || ""), String(req.body?.password || ""));
  if (!member) { res.status(401).json({ error: "账号或密码错误" });return; }
  await finishLogin(member, req.body?.skinId, res);
}));

momoRouter.post("/auth/logout", requireMember, asyncHandler(async (req, res) => { await momoRepo.deleteSession(bearer(req));res.json({ ok: true }); }));

momoRouter.get("/auth/me", requireMember, asyncHandler(async (_req, res) => {
  const id = String(res.locals.memberId);const member = await memberRepo.getById(id);if (!member) { res.status(404).json({ error: "账号不存在" });return; }res.json({ member: publicMember(member, await momoRepo.getSkin(id)) });
}));

momoRouter.get("/members", requireMember, asyncHandler(async (_req, res) => {
  const members = await memberRepo.list();const skins = await momoRepo.getSkins(members.map(x => x.id));res.json(members.map(member => publicMember(member, skins[member.id])));
}));

momoRouter.patch("/profile", requireMember, asyncHandler(async (req, res) => {
  const skinId = safeSkin(req.body?.skinId);await momoRepo.setSkin(String(res.locals.memberId), skinId);res.json({ ok: true, skinId });
}));

momoRouter.post("/messages", requireMember, asyncHandler(async (req, res) => {
  const senderId = String(res.locals.memberId), receiverId = String(req.body?.receiverId || ""), content = String(req.body?.content || "").trim();
  if (!receiverId || receiverId === senderId) { res.status(400).json({ error: "请选择其他联系人" });return; }
  if (!content || content.length > 1000) { res.status(400).json({ error: "消息需为 1-1000 个字" });return; }
  const [sender, receiver] = await Promise.all([memberRepo.getById(senderId), memberRepo.getById(receiverId)]);if (!sender || !receiver) { res.status(404).json({ error: "联系人不存在" });return; }
  const message = await momoRepo.createMessage({ senderId, receiverId, senderNickname: sender.nickname, receiverNickname: receiver.nickname, senderSkinId: await momoRepo.getSkin(senderId), content });res.status(201).json(message);
}));

momoRouter.get("/messages/inbox", requireMember, asyncHandler(async (req, res) => {
  let items = await momoRepo.inbox(String(res.locals.memberId));if (String(req.query.unread || "") === "1") items = items.filter(x => x.status !== "read");res.json(items.slice(-50));
}));

momoRouter.get("/messages/conversation/:peerId", requireMember, asyncHandler(async (req, res) => {
  res.json(await momoRepo.conversation(String(res.locals.memberId), req.params.peerId));
}));

momoRouter.post("/messages/:id/delivered", requireMember, asyncHandler(async (req, res) => {
  const message = await momoRepo.mark(String(res.locals.memberId), req.params.id, "delivered");if (!message) { res.status(404).json({ error: "来信不存在" });return; }res.json(message);
}));

momoRouter.post("/messages/:id/read", requireMember, asyncHandler(async (req, res) => {
  const message = await momoRepo.mark(String(res.locals.memberId), req.params.id, "read");if (!message) { res.status(404).json({ error: "来信不存在" });return; }res.json(message);
}));
