import { createHash, randomBytes } from "node:crypto";
import { TABLE_PREFIX } from "../db/client.js";
import { putRow, getRow, getRange, updateRow, deleteRow, pk, attr, INF_MIN, INF_MAX } from "../db/helpers.js";
import { generateId } from "../utils/id.js";

export const MOMO_TABLES = {
  sessions: `${TABLE_PREFIX}momo_sessions`,
  profiles: `${TABLE_PREFIX}momo_profiles`,
  inbox: `${TABLE_PREFIX}momo_messages_inbox`,
  outbox: `${TABLE_PREFIX}momo_messages_outbox`,
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

export type MomoMessage = {
  id: string;
  senderId: string;
  receiverId: string;
  senderNickname: string;
  receiverNickname: string;
  senderSkinId: string;
  content: string;
  createdAt: string;
  status: "sent" | "delivered" | "read";
  deliveredAt: string;
  readAt: string;
};

function messageAttrs(message: MomoMessage, primaryMember: "senderId" | "receiverId") {
  const values = [
    attr("senderId", message.senderId), attr("receiverId", message.receiverId),
    attr("senderNickname", message.senderNickname), attr("receiverNickname", message.receiverNickname),
    attr("senderSkinId", message.senderSkinId), attr("content", message.content),
    attr("status", message.status), attr("deliveredAt", message.deliveredAt), attr("readAt", message.readAt),
  ];
  return values.filter(value => !Object.prototype.hasOwnProperty.call(value, primaryMember));
}

function fromRow(row: Record<string, any>): MomoMessage {
  return {
    id: String(row.id || ""), senderId: String(row.senderId || ""), receiverId: String(row.receiverId || ""),
    senderNickname: String(row.senderNickname || ""), receiverNickname: String(row.receiverNickname || ""),
    senderSkinId: safeSkin(row.senderSkinId), content: String(row.content || ""), createdAt: String(row.createdAt || ""),
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
};
