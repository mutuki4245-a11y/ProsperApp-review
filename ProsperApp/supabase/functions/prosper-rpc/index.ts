import postgres from "https://deno.land/x/postgresjs@v3.4.5/mod.js";

const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, apikey, content-type, x-prosper-rpc-api-key",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

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

const databaseUrl = Deno.env.get("SUPABASE_DB_URL");
const sql = databaseUrl
  ? postgres(databaseUrl, { max: 3, prepare: false })
  : null;

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }

  if (req.method !== "POST") {
    return jsonResponse({ error: "method_not_allowed" }, 405);
  }

  const authError = validateClientKey(req);
  if (authError) {
    return jsonResponse({ error: authError }, 401);
  }

  if (!sql) {
    return jsonResponse({ error: "missing_supabase_db_url" }, 500);
  }

  let body: RequestBody;
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ error: "invalid_json" }, 400);
  }

  const requestedFunctionName = body.function_name ?? "";
  const definition = rpcDefinitions.get(requestedFunctionName);
  const rpcName = parseRpcName(requestedFunctionName);
  if (!definition || !rpcName) {
    return jsonResponse({ error: "invalid_function_name" }, 400);
  }

  try {
    const result = await runRpc(rpcName, definition, body.payload);
    return definition.result === "scalar"
      ? jsonResponse({ result }, 200)
      : jsonResponse({ data: result }, 200);
  } catch (error) {
    console.error(error);
    return jsonResponse({
      error: "database_error",
      message: error instanceof Error ? error.message : String(error),
    }, 500);
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

function validateClientKey(req: Request): string | null {
  const provided = readBearer(req.headers.get("authorization"))
    ?? req.headers.get("x-prosper-rpc-api-key")
    ?? req.headers.get("apikey");
  if (!provided) {
    return "missing_client_key";
  }

  const allowedKeys = getAllowedClientKeys();
  if (allowedKeys.length === 0) {
    return "missing_allowed_client_keys";
  }

  return allowedKeys.includes(provided) ? null : "invalid_client_key";
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

  if (value === undefined || value === "") {
    return null;
  }

  return value;
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
      ...corsHeaders,
      "content-type": "application/json; charset=utf-8",
    },
  });
}
