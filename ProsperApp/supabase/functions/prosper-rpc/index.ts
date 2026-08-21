import postgres from "https://deno.land/x/postgresjs@v3.4.5/mod.js";
import {
  normalizeScalarSqlValue,
  safeDatabaseErrorCode,
  verifySignedEnvelope,
} from "./security.ts";

type PgType =
  | "bigint"
  | "boolean"
  | "date"
  | "integer"
  | "json"
  | "jsonb"
  | "numeric"
  | "text"
  | "text[]"
  | "timestamp with time zone";

type RpcParam = {
  name: string;
  type: PgType;
  defaultValue?: unknown;
};

type RpcDefinition = {
  result: "rows" | "scalar";
  params: RpcParam[];
};

type RpcName = {
  schemaName: string;
  functionName: string;
};

type RequestBody = {
  operation?: "rpc" | "bind_access";
  actor?: {
    email?: string;
    google_subject?: string;
    email_verified?: boolean;
  };
  function_name?: string;
  payload?: Record<string, unknown> | unknown;
};

const rpcDefinitions = new Map<string, RpcDefinition>([
  ["store.get_departments", { result: "rows", params: [] }],
  [
    "store.delete_non_master_records",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_confirmation", type: "text" },
      ],
    },
  ],
  ["store.get_business_home_bootstrap_v2", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  [
    "store.get_management_master_snapshot",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_known_revision", type: "text", defaultValue: null },
      ],
    },
  ],
  [
    "store.save_management_master_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_area", type: "text" },
        { name: "p_action", type: "text" },
        { name: "p_expected_area_revision", type: "text" },
        { name: "p_payload", type: "jsonb" },
        { name: "p_allow_admin_actions", type: "boolean", defaultValue: false },
      ],
    },
  ],
  [
    "store.get_current_business_home_snapshot",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_known_revision", type: "bigint", defaultValue: null },
      ],
    },
  ],
  [
    "store.get_current_order_entry_candidates",
    { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] },
  ],
  [
    "store.get_business_day_daily_report",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_sales_history_page",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_from_business_date", type: "date", defaultValue: null },
        { name: "p_to_business_date", type: "date", defaultValue: null },
        { name: "p_before_business_date", type: "date", defaultValue: null },
        { name: "p_before_business_day_id", type: "bigint", defaultValue: null },
        { name: "p_limit", type: "integer", defaultValue: 31 },
        { name: "p_current_business_date", type: "date" },
      ],
    },
  ],
  [
    "store.get_sales_history_correction_editor",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_current_business_date", type: "date" },
      ],
    },
  ],
  [
    "store.save_sales_history_correction_v1",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_expected_snapshot_version", type: "integer" },
        { name: "p_current_business_date", type: "date" },
        { name: "p_actor_email", type: "text" },
        { name: "p_reason", type: "text" },
        { name: "p_correction_payload", type: "jsonb" },
      ],
    },
  ],
  [
    "store.get_current_receipt_work_queue",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_resume_cursor", type: "text", defaultValue: null },
      ],
    },
  ],
  [
    "store.is_pending_receipt_drive_file_allowed",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_drive_file_id", type: "text" },
      ],
    },
  ],
  [
    "store.advance_receipt_work_queue_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_action", type: "text" },
        { name: "p_work_item_token", type: "text" },
        { name: "p_document_id", type: "text" },
        { name: "p_payment_date", type: "date", defaultValue: null },
        { name: "p_amount", type: "numeric", defaultValue: null },
        { name: "p_account_subject", type: "text", defaultValue: null },
        { name: "p_description", type: "text", defaultValue: null },
        { name: "p_group_code", type: "text", defaultValue: null },
        { name: "p_advance_cast_id", type: "bigint", defaultValue: null },
      ],
    },
  ],
  [
    "store.save_current_business_day_drink_delivery_amount_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint", defaultValue: null },
        { name: "p_expected_business_day_revision", type: "bigint", defaultValue: null },
        { name: "p_business_date", type: "date" },
        { name: "p_drink_delivery_amount", type: "numeric" },
      ],
    },
  ],
  [
    "store.get_current_closing_dashboard",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_known_cast_master_revision", type: "text", defaultValue: null },
      ],
    },
  ],
  [
    "store.close_business_day_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint", defaultValue: null },
        { name: "p_expected_business_day_revision", type: "bigint", defaultValue: null },
        { name: "p_memo", type: "text", defaultValue: null },
        { name: "p_ignore_closing_requirements", type: "boolean", defaultValue: false },
      ],
    },
  ],
  [
    "store.get_current_drink_delivery_editor",
    { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] },
  ],
  [
    "store.get_current_cast_sales_adjustment_overview",
    { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] },
  ],
  [
    "store.get_current_drink_back_editor",
    { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] },
  ],
  [
    "store.save_drink_back_adjustments_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_required_adjustments", type: "jsonb", defaultValue: [] },
        { name: "p_optional_adjustments", type: "jsonb", defaultValue: [] },
        { name: "p_remove_cast_ids", type: "jsonb", defaultValue: [] },
      ],
    },
  ],
  [
    "store.save_current_cast_sales_adjustment_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_expected_slip_version", type: "timestamp with time zone" },
        { name: "p_expected_checkout_id", type: "bigint" },
        { name: "p_expected_checkout_version", type: "timestamp with time zone" },
        { name: "p_adjustments", type: "jsonb" },
        { name: "p_source_amount_type", type: "text" },
        { name: "p_split_mode", type: "text" },
      ],
    },
  ],
  [
    "store.confirm_current_cast_sales_adjustments_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_slips", type: "jsonb" },
      ],
    },
  ],
  [
    "store.sync_business_home_changes_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_client_batch_id", type: "uuid" },
        { name: "p_expected_business_day_id", type: "bigint", defaultValue: null },
        { name: "p_expected_business_day_revision", type: "bigint", defaultValue: null },
        { name: "p_business_date", type: "date", defaultValue: null },
        { name: "p_operations", type: "jsonb" },
        { name: "p_karaoke_lines", type: "jsonb" },
      ],
    },
  ],
  [
    "store.save_current_business_day_attendance_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint", defaultValue: null },
        { name: "p_expected_business_day_revision", type: "bigint", defaultValue: null },
        { name: "p_business_date", type: "date" },
        { name: "p_cast_entries", type: "jsonb" },
        { name: "p_staff_entries", type: "jsonb" },
      ],
    },
  ],
  [
    "store.get_attendance_editor_bootstrap_v2",
    { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] },
  ],
  [
    "store.get_current_attendance_editor_snapshot",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_known_business_day_id", type: "bigint", defaultValue: null },
        { name: "p_known_business_day_revision", type: "bigint", defaultValue: null },
      ],
    },
  ],
  [
    "store.submit_current_order_entry_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_lines", type: "jsonb" },
      ],
    },
  ],
  [
    "store.confirm_checkout_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_payments", type: "jsonb" },
        { name: "p_received_amount", type: "numeric" },
      ],
    },
  ],
  [
    "store.issue_checkout_statement_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_closed_at", type: "timestamp with time zone" },
      ],
    },
  ],
  [
    "store.get_checkout_statement_print_data",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.release_checkout_ready_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_checkout_receipt_print_data",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.cancel_checkout_v2",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_operation_id", type: "text" },
        { name: "p_expected_business_day_id", type: "bigint" },
        { name: "p_expected_business_day_revision", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
]);

const databaseUrl = Deno.env.get("PROSPER_RPC_DB_URL");
const sql = databaseUrl
  ? postgres(databaseUrl, { max: 3, prepare: false })
  : null;

Deno.serve(async (req) => {
  const requestId = crypto.randomUUID();
  const startedAt = performance.now();
  let functionName = "";
  let operationId: string | null = null;
  let status = 500;

  if (req.method !== "POST") {
    return errorResponse("method_not_allowed", requestId, 405);
  }

  if (!sql) {
    return errorResponse("service_not_configured", requestId, 503);
  }

  const rawBody = await req.text();
  let body: RequestBody;
  try {
    body = JSON.parse(rawBody);
  } catch {
    return errorResponse("invalid_json", requestId, 400);
  }

  const signed = await validateSignedEnvelope(req, rawBody);
  const legacyAllowed = Deno.env.get("PROSPER_RPC_ALLOW_LEGACY_KEY") !== "false";
  if (!signed && !(legacyAllowed && await validateLegacyClientKey(req))) {
    return errorResponse("invalid_signature", requestId, 401);
  }

  try {
    if (body.operation === "bind_access") {
      functionName = "store.bind_app_user_access";
      if (!signed || !body.actor) {
        return errorResponse("invalid_signature", requestId, 401);
      }
      const rows = await sql.unsafe(
        "select * from store.bind_app_user_access($1::text, $2::text, $3::boolean)",
        [body.actor.email ?? "", body.actor.google_subject ?? "", body.actor.email_verified === true],
      );
      status = 200;
      return jsonResponse({ data: rows, request_id: requestId }, status);
    }

    functionName = body.function_name ?? "";
    const definition = rpcDefinitions.get(functionName);
    const rpcName = parseRpcName(functionName);
    if (!definition || !rpcName) {
      return errorResponse("invalid_function_name", requestId, 400);
    }

    const source = isRecord(body.payload) ? body.payload : {};
    operationId = typeof source.p_operation_id === "string" ? source.p_operation_id : null;
    let allowedDepartmentIds: Set<string> | null = null;
    if (signed && functionName === "store.get_departments") {
      if (!body.actor) {
        return errorResponse("access_denied", requestId, 403);
      }
      const accessRows = await sql.unsafe(
        "select * from store.get_app_user_access($1::text, $2::text)",
        [body.actor.email ?? "", body.actor.google_subject ?? ""],
      );
      allowedDepartmentIds = new Set(accessRows.map((row) => String(row.department_id)));
      if (allowedDepartmentIds.size === 0) {
        return errorResponse("access_denied", requestId, 403);
      }
    } else if (signed) {
      const departmentId = toPositiveBigInt(source.p_department_id);
      if (!body.actor || departmentId === null) {
        return errorResponse("access_denied", requestId, 403);
      }
      const requiredRole = requiredRoleFor(functionName, source);
      const accessRows = await sql.unsafe(
        "select store.authorize_app_user_access($1::text, $2::text, $3::bigint, $4::text) as allowed",
        [body.actor.email ?? "", body.actor.google_subject ?? "", departmentId, requiredRole],
      );
      if (accessRows[0]?.allowed !== true) {
        return errorResponse("access_denied", requestId, 403);
      }
    }

    let result = await runRpc(rpcName, definition, body.payload);
    if (allowedDepartmentIds && Array.isArray(result)) {
      result = result.filter((row) =>
        isRecord(row) && allowedDepartmentIds!.has(String(row.department_id))
      );
    }
    status = 200;
    return definition.result === "scalar"
      ? jsonResponse({ result, request_id: requestId }, status)
      : jsonResponse({ data: result, request_id: requestId }, status);
  } catch (error) {
    const errorCode = safeDatabaseErrorCode(error);
    status = errorStatus(errorCode);
    console.error(JSON.stringify({
      request_id: requestId,
      function_name: functionName,
      operation_id: operationId,
      status,
      duration_ms: Math.round(performance.now() - startedAt),
      error_code: errorCode,
    }));
    return errorResponse(errorCode, requestId, status);
  } finally {
    console.info(JSON.stringify({
      request_id: requestId,
      function_name: functionName,
      operation_id: operationId,
      status,
      duration_ms: Math.round(performance.now() - startedAt),
    }));
  }
});

async function runRpc(rpcName: RpcName, definition: RpcDefinition, payload: unknown): Promise<unknown> {
  const source = isRecord(payload) ? payload : {};
  const values = definition.params.map((param) => toSqlValue(
    source[param.name] === undefined ? param.defaultValue : source[param.name],
    param.type
  ));
  const args = definition.params
    .map((param, index) => `$${index + 1}::${param.type}`)
    .join(", ");
  const query = definition.result === "scalar"
    ? `select ${rpcName.schemaName}.${rpcName.functionName}(${args}) as result`
    : `select * from ${rpcName.schemaName}.${rpcName.functionName}(${args})`;

  const rows = await sql!.unsafe(query, values);
  return definition.result === "scalar" ? rows[0]?.result ?? null : rows;
}

async function validateLegacyClientKey(req: Request): Promise<boolean> {
  const provided = readBearer(req.headers.get("authorization"))
    ?? req.headers.get("x-prosper-rpc-api-key")
    ?? req.headers.get("apikey");
  if (!provided) {
    return false;
  }

  const allowedKeys = getAllowedClientKeys();
  if (allowedKeys.length === 0) {
    return false;
  }

  const providedBytes = new TextEncoder().encode(provided);
  for (const allowed of allowedKeys) {
    if (await constantTimeEqual(providedBytes, new TextEncoder().encode(allowed))) {
      return true;
    }
  }
  return false;
}

async function validateSignedEnvelope(req: Request, rawBody: string): Promise<boolean> {
  const timestamp = req.headers.get("x-prosper-timestamp") ?? "";
  const signature = req.headers.get("x-prosper-signature") ?? "";
  return verifySignedEnvelope(rawBody, timestamp, signature, getAllowedClientKeys());
}

async function constantTimeEqual(left: Uint8Array, right: Uint8Array): Promise<boolean> {
  const key = await crypto.subtle.importKey(
    "raw",
    new Uint8Array(32),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const [leftMac, rightMac] = await Promise.all([
    crypto.subtle.sign("HMAC", key, left),
    crypto.subtle.sign("HMAC", key, right),
  ]);
  return crypto.subtle.verify("HMAC", key, rightMac, left);
}

function getAllowedClientKeys(): string[] {
  const keys = new Set<string>();
  addClientKey(keys, Deno.env.get("ProsperApp_API_KEY"));
  addClientKeys(keys, Deno.env.get("ProsperApp_API_KEYS"));
  addClientKey(keys, Deno.env.get("PROSPERAPP_API_KEY"));
  addClientKeys(keys, Deno.env.get("PROSPERAPP_API_KEYS"));
  addClientKey(keys, Deno.env.get("PROSPER_RPC_API_KEY"));
  addClientKeys(keys, Deno.env.get("PROSPER_RPC_API_KEYS"));
  return [...keys];
}

function addClientKeys(keys: Set<string>, rawKeys: string | undefined) {
  if (!rawKeys) {
    return;
  }

  try {
    const parsed = JSON.parse(rawKeys);
    if (Array.isArray(parsed)) {
      for (const key of parsed) {
        addClientKey(keys, key);
      }
      return;
    }

    if (parsed && typeof parsed === "object") {
      for (const key of Object.values(parsed)) {
        addClientKey(keys, key);
      }
      return;
    }
  } catch {
  }

  for (const key of rawKeys.split(/[,\n;]/)) {
    addClientKey(keys, key);
  }
}

function addClientKey(keys: Set<string>, value: unknown) {
  if (typeof value !== "string") {
    return;
  }

  const trimmed = value.trim();
  if (trimmed.length > 0) {
    keys.add(trimmed);
  }
}

function toSqlValue(value: unknown, type: PgType): unknown {
  if (isJsonType(type)) {
    return toJsonValue(value);
  }

  if (type === "text[]") {
    return Array.isArray(value) ? value : [];
  }

  return normalizeScalarSqlValue(value, type);
}

function isJsonType(type: PgType): boolean {
  return type === "json" || type === "jsonb";
}

function toJsonValue(value: unknown): unknown {
  if (value === undefined || value === null) {
    return null;
  }

  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed.length === 0) {
      return null;
    }

    try {
      return JSON.parse(trimmed);
    } catch {
      return trimmed;
    }
  }

  return value;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object" && !Array.isArray(value);
}

function parseRpcName(value: string): RpcName | null {
  const parts = value.split(".");
  if (parts.length !== 2) {
    return null;
  }

  const [schemaName, functionName] = parts;
  return isSqlIdentifier(schemaName) && isSqlIdentifier(functionName)
    ? { schemaName, functionName }
    : null;
}

function isSqlIdentifier(value: string): boolean {
  return /^[a-z_][a-z0-9_]*$/.test(value);
}

function toPositiveBigInt(value: unknown): bigint | null {
  try {
    const parsed = BigInt(typeof value === "number" ? Math.trunc(value) : String(value));
    return parsed > 0n ? parsed : null;
  } catch {
    return null;
  }
}

function requiredRoleFor(functionName: string, payload: Record<string, unknown>): "operator" | "administrator" {
  if (functionName === "store.delete_non_master_records" ||
      (functionName === "store.save_management_master_v2" && payload.p_allow_admin_actions === true) ||
      (functionName === "store.close_business_day_v2" && payload.p_ignore_closing_requirements === true)) {
    return "administrator";
  }
  return "operator";
}

function errorStatus(errorCode: string): number {
  if (errorCode === "access_denied") return 403;
  if (errorCode.includes("not_found")) return 404;
  if (errorCode.startsWith("invalid_") || errorCode.endsWith("_required")) return 400;
  if (errorCode.includes("conflict") || errorCode.endsWith("_reused") || errorCode.includes("already_")) return 409;
  return 500;
}

function readBearer(value: string | null): string | null {
  if (!value) {
    return null;
  }

  const match = /^Bearer\s+(.+)$/i.exec(value.trim());
  return match?.[1] ?? null;
}

function jsonResponse(body: unknown, status: number): Response {
  return new Response(JSON.stringify(body, (_key, value) => (
    typeof value === "bigint" ? value.toString() : value
  )), {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
    },
  });
}

function errorResponse(errorCode: string, requestId: string, status: number): Response {
  return jsonResponse({ error_code: errorCode, request_id: requestId }, status);
}
