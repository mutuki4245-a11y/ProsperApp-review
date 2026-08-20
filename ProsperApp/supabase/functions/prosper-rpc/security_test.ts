import { assertEquals } from "jsr:@std/assert@1";
import {
  normalizeScalarSqlValue,
  safeDatabaseErrorCode,
  verifySignedEnvelope,
} from "./security.ts";

async function sign(body: string, timestamp: string, secret: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(secret),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const bytes = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(`${timestamp}\n${body}`),
  );
  return [...new Uint8Array(bytes)].map((value) => value.toString(16).padStart(2, "0")).join("");
}

Deno.test("署名本文・timestamp・鍵の完全一致だけを許可する", async () => {
  const body = JSON.stringify({ function_name: "store.get_departments", actor: { email: "a@example.com" } });
  const timestamp = "1800000000";
  const signature = await sign(body, timestamp, "test-secret");
  const now = 1800000000 * 1000;
  assertEquals(await verifySignedEnvelope(body, timestamp, signature, ["test-secret"], now), true);
  assertEquals(await verifySignedEnvelope(`${body} `, timestamp, signature, ["test-secret"], now), false);
  assertEquals(await verifySignedEnvelope(body, "1799999000", signature, ["test-secret"], now), false);
  assertEquals(await verifySignedEnvelope(body, timestamp, signature, ["wrong-secret"], now), false);
});

Deno.test("DB詳細をerror_codeとして漏らさない", () => {
  assertEquals(safeDatabaseErrorCode(new Error("business_day_revision_conflict")), "business_day_revision_conflict");
  assertEquals(safeDatabaseErrorCode(new Error("duplicate key value violates unique constraint ux_secret")), "database_error");
});

Deno.test("textの空文字だけを保持する", () => {
  assertEquals(normalizeScalarSqlValue("", "text"), "");
  assertEquals(normalizeScalarSqlValue("", "numeric"), null);
  assertEquals(normalizeScalarSqlValue("", "date"), null);
});
