export async function verifySignedEnvelope(
  rawBody: string,
  timestamp: string,
  signature: string,
  secrets: string[],
  nowMilliseconds = Date.now(),
): Promise<boolean> {
  const timestampSeconds = Number(timestamp);
  if (!Number.isInteger(timestampSeconds) ||
      Math.abs(nowMilliseconds / 1000 - timestampSeconds) > 300 ||
      !/^[0-9a-f]{64}$/i.test(signature)) {
    return false;
  }

  const data = new TextEncoder().encode(`${timestamp}\n${rawBody}`);
  const signatureBytes = hexToBytes(signature);
  for (const secret of secrets) {
    const key = await crypto.subtle.importKey(
      "raw",
      new TextEncoder().encode(secret),
      { name: "HMAC", hash: "SHA-256" },
      false,
      ["verify"],
    );
    if (await crypto.subtle.verify("HMAC", key, signatureBytes, data)) {
      return true;
    }
  }
  return false;
}

export function safeDatabaseErrorCode(error: unknown): string {
  const message = error instanceof Error ? error.message : "";
  return /^[a-z][a-z0-9_]{2,80}$/.test(message) ? message : "database_error";
}

export function normalizeScalarSqlValue(value: unknown, type: string): unknown {
  if (value === undefined || (value === "" && type !== "text")) {
    return null;
  }
  return value;
}

function hexToBytes(value: string): Uint8Array {
  const bytes = new Uint8Array(value.length / 2);
  for (let index = 0; index < value.length; index += 2) {
    bytes[index / 2] = Number.parseInt(value.slice(index, index + 2), 16);
  }
  return bytes;
}
