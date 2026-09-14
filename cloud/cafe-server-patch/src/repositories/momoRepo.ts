import { createHash, randomBytes } from "node:crypto";
import { TABLE_PREFIX } from "../db/client.js";
import { putRow, getRow, getRange, updateRow, deleteRow, pk, attr, INF_MIN, INF_MAX } from "../db/helpers.js";
import { generateId } from "../utils/id.js";

export const MOMO_TABLES = {
  sessions: `${TABLE_PREFIX}momo_sessions`,
  profiles: `${TABLE_PREFIX}momo_profiles`,
  inbox: `${TABLE_PREFIX}momo_messages_inbox`,
  outbox: `${TABLE_PREFIX}momo_messages_outbox`,
  groups: `${TABLE_PREFIX}momo_groups`,
  groupMembers: `${TABLE_PREFIX}momo_group_members`,
  groupMessages: `${TABLE_PREFIX}momo_group_messages`,
} as const;

const allowedSkins = new Set([
  "default", "detective", "green-scarf-calico", "tuxedo-bell",
  "white-bowtie", "orange-scarf", "red-collar-calico", "gentleman-monocle",
]);

function tokenHash(token: string) {
  return createHash("sha256").update(token).digest("hex");
}

export function safeSkin(value: unknown): string {
  const skin = String(value || "default");
  return allowedSkins.has(skin) ? skin : "default";
}

// 信件随附的文件：文件本身存在 OSS / 本地静态目录，消息里只保存元数据。
export type MomoAttachment = { name: string; url: string; type: string; size: number };

export type MomoMessage = {
  id: string;
  senderId: string;
  receiverId: string;
  senderNickname: string;
  receiverNickname: string;
  senderSkinId: string;
  content: string;
  attachments: MomoAttachment[];
  createdAt: string;
  status: "sent" | "delivered" | "read";
  deliveredAt: string;
  readAt: string;
};

export type MomoGroup = { id: string; name: string; ownerId: string; memberIds: string[]; createdAt: string };
export type MomoGroupMessage = { id: string; groupId: string; senderId: string; senderNickname: string; senderSkinId: string; content: string; attachments: MomoAttachment[]; createdAt: string };

function parseStrings(value: unknown): string[] {
  if (Array.isArray(value)) return value.map(String);
  if (typeof value === "string" && value.trim()) { try { const parsed=JSON.parse(value);if(Array.isArray(parsed))return parsed.map(String); } catch {} }
  return [];
}

function messageAttrs(message: MomoMessage, primaryMember: "senderId" | "receiverId") {
  const values = [
    attr("senderId", message.senderId), attr("receiverId", message.receiverId),
    attr("senderNickname", message.senderNickname), attr("receiverNickname", message.receiverNickname),
    attr("senderSkinId", message.senderSkinId), attr("content", message.content),
    attr("attachments", message.attachments || []),
    attr("status", message.status), attr("deliveredAt", message.deliveredAt), attr("readAt", message.readAt),
  ];
  return values.filter(value => !Object.prototype.hasOwnProperty.call(value, primaryMember));
}

// attachments 以 JSON 字符串落库；历史数据没有该属性时回退成空数组。
function parseAttachments(value: unknown): MomoAttachment[] {
  if (Array.isArray(value)) return value as MomoAttachment[];
  if (typeof value === "string" && value.trim()) {
    try { const parsed = JSON.parse(value);if (Array.isArray(parsed)) return parsed as MomoAttachment[]; } catch {}
  }
  return [];
}

function fromRow(row: Record<string, any>): MomoMessage {
  return {
    id: String(row.id || ""), senderId: String(row.senderId || ""), receiverId: String(row.receiverId || ""),
    senderNickname: String(row.senderNickname || ""), receiverNickname: String(row.receiverNickname || ""),
    senderSkinId: safeSkin(row.senderSkinId), content: String(row.content || ""), attachments: parseAttachments(row.attachments),
    createdAt: String(row.createdAt || ""),
    status: (row.status || "sent") as MomoMessage["status"], deliveredAt: String(row.deliveredAt || ""), readAt: String(row.readAt || ""),
  };
}

export const momoRepo = {
  async createSession(memberId: string) {
    const token = randomBytes(32).toString("base64url");
    const now = new Date();
    await putRow(MOMO_TABLES.sessions, [pk("tokenHash", tokenHash(token))], [
      attr("memberId", memberId), attr("createdAt", now.toISOString()),
      attr("expiresAt", new Date(now.getTime() + 30 * 86400000).toISOString()),
    ]);
    return token;
  },

  async resolveSession(token: string) {
    if (!token) return null;
    const row = await getRow(MOMO_TABLES.sessions, [pk("tokenHash", tokenHash(token))]);
    if (!row || !row.memberId || Date.parse(String(row.expiresAt || "")) < Date.now()) return null;
    return String(row.memberId);
  },

  async deleteSession(token: string) {
    if (token) await deleteRow(MOMO_TABLES.sessions, [pk("tokenHash", tokenHash(token))]);
  },

  async setSkin(memberId: string, skinId: string) {
    await putRow(MOMO_TABLES.profiles, [pk("memberId", memberId)], [attr("skinId", safeSkin(skinId)), attr("updatedAt", new Date().toISOString())]);
  },

  async getSkin(memberId: string) {
    const row = await getRow(MOMO_TABLES.profiles, [pk("memberId", memberId)]);
    return safeSkin(row?.skinId);
  },

  async getSkins(memberIds: string[]) {
    const output: Record<string, string> = {};
    await Promise.all(memberIds.map(async id => { output[id] = await this.getSkin(id); }));
    return output;
  },

  async createMessage(input: Omit<MomoMessage, "id" | "createdAt" | "status" | "deliveredAt" | "readAt">) {
    const message: MomoMessage = { ...input, id: generateId("msg"), createdAt: new Date().toISOString(), status: "sent", deliveredAt: "", readAt: "" };
    await Promise.all([
      putRow(MOMO_TABLES.inbox, [pk("receiverId", message.receiverId), pk("createdAt", message.createdAt), pk("id", message.id)], messageAttrs(message, "receiverId")),
      putRow(MOMO_TABLES.outbox, [pk("senderId", message.senderId), pk("createdAt", message.createdAt), pk("id", message.id)], messageAttrs(message, "senderId")),
    ]);
    return message;
  },

  async inbox(receiverId: string) {
    const rows = await getRange(MOMO_TABLES.inbox, [pk("receiverId", receiverId), pk("createdAt", INF_MIN), pk("id", INF_MIN)], [pk("receiverId", receiverId), pk("createdAt", INF_MAX), pk("id", INF_MAX)]);
    return rows.map(fromRow).sort((a,b) => a.createdAt.localeCompare(b.createdAt));
  },

  async outbox(senderId: string) {
    const rows = await getRange(MOMO_TABLES.outbox, [pk("senderId", senderId), pk("createdAt", INF_MIN), pk("id", INF_MIN)], [pk("senderId", senderId), pk("createdAt", INF_MAX), pk("id", INF_MAX)]);
    return rows.map(fromRow).sort((a,b) => a.createdAt.localeCompare(b.createdAt));
  },

  async conversation(memberId: string, peerId: string) {
    const [incoming, outgoing] = await Promise.all([this.inbox(memberId), this.outbox(memberId)]);
    return incoming.filter(x => x.senderId === peerId).concat(outgoing.filter(x => x.receiverId === peerId)).sort((a,b) => a.createdAt.localeCompare(b.createdAt)).slice(-200);
  },

  async mark(receiverId: string, id: string, next: "delivered" | "read") {
    const rows = await this.inbox(receiverId);
    const message = rows.find(x => x.id === id);
    if (!message) return null;
    if (message.status === "read" || (message.status === "delivered" && next === "delivered")) return message;
    const now = new Date().toISOString();message.status = next;if (next === "delivered") message.deliveredAt = now;else { message.deliveredAt = message.deliveredAt || now;message.readAt = now; }
    const changes = [attr("status", message.status), attr("deliveredAt", message.deliveredAt), attr("readAt", message.readAt)];
    await Promise.all([
      updateRow(MOMO_TABLES.inbox, [pk("receiverId", message.receiverId), pk("createdAt", message.createdAt), pk("id", message.id)], changes),
      updateRow(MOMO_TABLES.outbox, [pk("senderId", message.senderId), pk("createdAt", message.createdAt), pk("id", message.id)], changes),
    ]);
    return message;
  },

  async createGroup(ownerId: string, name: string, memberIds: string[]) {
    const group: MomoGroup={id:generateId("grp"),name,ownerId,memberIds:Array.from(new Set([ownerId,...memberIds])),createdAt:new Date().toISOString()};
    await putRow(MOMO_TABLES.groups,[pk("id",group.id)],[attr("name",group.name),attr("ownerId",group.ownerId),attr("memberIds",group.memberIds),attr("createdAt",group.createdAt)]);
    await Promise.all(group.memberIds.map(memberId=>putRow(MOMO_TABLES.groupMembers,[pk("memberId",memberId),pk("groupId",group.id)],[attr("joinedAt",group.createdAt),attr("role",memberId===ownerId?"owner":"member")])));
    return group;
  },

  async getGroup(id: string) {
    const row=await getRow(MOMO_TABLES.groups,[pk("id",id)]);if(!row)return null;
    return {id:String(row.id||id),name:String(row.name||""),ownerId:String(row.ownerId||""),memberIds:parseStrings(row.memberIds),createdAt:String(row.createdAt||"")} as MomoGroup;
  },

  async groupsFor(memberId: string) {
    const rows=await getRange(MOMO_TABLES.groupMembers,[pk("memberId",memberId),pk("groupId",INF_MIN)],[pk("memberId",memberId),pk("groupId",INF_MAX)]);
    const groups=await Promise.all(rows.map(row=>this.getGroup(String(row.groupId||""))));return groups.filter(Boolean) as MomoGroup[];
  },

  async createGroupMessage(group: MomoGroup,input: Omit<MomoGroupMessage,"id"|"createdAt"|"groupId">) {
    const message:MomoGroupMessage={...input,id:generateId("gmsg"),groupId:group.id,createdAt:new Date().toISOString()};
    await putRow(MOMO_TABLES.groupMessages,[pk("groupId",message.groupId),pk("createdAt",message.createdAt),pk("id",message.id)],[attr("senderId",message.senderId),attr("senderNickname",message.senderNickname),attr("senderSkinId",message.senderSkinId),attr("content",message.content),attr("attachments",message.attachments||[])]);return message;
  },

  async groupMessages(groupId:string) {
    const rows=await getRange(MOMO_TABLES.groupMessages,[pk("groupId",groupId),pk("createdAt",INF_MIN),pk("id",INF_MIN)],[pk("groupId",groupId),pk("createdAt",INF_MAX),pk("id",INF_MAX)]);
    return rows.map(row=>({id:String(row.id||""),groupId:String(row.groupId||groupId),senderId:String(row.senderId||""),senderNickname:String(row.senderNickname||""),senderSkinId:safeSkin(row.senderSkinId),content:String(row.content||""),attachments:parseAttachments(row.attachments),createdAt:String(row.createdAt||"")} as MomoGroupMessage)).slice(-300);
  },
};
