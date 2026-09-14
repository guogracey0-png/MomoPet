import "../env.js";
import { client, PK } from "../db/client.js";
import { MOMO_TABLES } from "../repositories/momoRepo.js";

const tables = [
  { tableName: MOMO_TABLES.sessions, primaryKey: [{ name: "tokenHash", type: PK.STRING }] },
  { tableName: MOMO_TABLES.profiles, primaryKey: [{ name: "memberId", type: PK.STRING }] },
  { tableName: MOMO_TABLES.inbox, primaryKey: [{ name: "receiverId", type: PK.STRING }, { name: "createdAt", type: PK.STRING }, { name: "id", type: PK.STRING }] },
  { tableName: MOMO_TABLES.outbox, primaryKey: [{ name: "senderId", type: PK.STRING }, { name: "createdAt", type: PK.STRING }, { name: "id", type: PK.STRING }] },
];

function exists(tableName: string): Promise<boolean> { return new Promise(resolve => client.describeTable({ tableName }, (error: Error | null) => resolve(!error))); }
function create(def: typeof tables[number]): Promise<void> { return new Promise((resolve, reject) => client.createTable({ tableMeta: def, reservedThroughput: { capacityUnit: { read: 0, write: 0 } }, tableOptions: { timeToLive: -1, maxVersions: 1 } }, (error: Error | null) => error ? reject(error) : resolve())); }

for (const def of tables) {
  if (await exists(def.tableName)) console.log(`[skip] ${def.tableName}`);
  else { await create(def);console.log(`[created] ${def.tableName}`);await new Promise(resolve => setTimeout(resolve, 1200)); }
}
