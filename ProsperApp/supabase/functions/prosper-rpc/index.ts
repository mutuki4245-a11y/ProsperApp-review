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
  ["store.get_context", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  ["store.get_current_business_day", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  ["store.get_tables", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  ["store.get_casts", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  ["store.get_casts_admin", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  [
    "store.get_business_day_slips",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_order_entry_slips",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  ["store.get_order_items", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  ["store.get_item_admin_catalog", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  ["store.get_nomination_back_master", { result: "rows", params: [{ name: "p_department_id", type: "bigint" }] }],
  [
    "store.save_nomination_back_master",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_settings", type: "jsonb" },
      ],
    },
  ],
  [
    "store.get_slip_detail",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_order_attending_casts",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_pending_receipts",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_status", type: "text" },
      ],
    },
  ],
  [
    "store.get_business_day_drink_delivery_status",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_business_day_closing_attendance",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_business_day_cast_sales_adjustment_status",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_cast_sales_adjustment_slips",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.get_cast_sales_adjustment_detail",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.open_business_day",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_date", type: "date" },
        { name: "p_memo", type: "text" },
      ],
    },
  ],
  [
    "store.open_business_day_with_attendance",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_date", type: "date" },
        { name: "p_attendance_entries", type: "jsonb" },
        { name: "p_memo", type: "text" },
      ],
    },
  ],
  [
    "store.save_business_day_attendance",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_attendance_entries", type: "jsonb" },
      ],
    },
  ],
  [
    "store.get_open_slip_count",
    {
      result: "scalar",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.save_business_day_drink_delivery_amount",
    {
      result: "scalar",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_drink_delivery_amount", type: "numeric" },
      ],
    },
  ],
  [
    "store.save_business_day_closing_attendance",
    {
      result: "scalar",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_attendance_entries", type: "jsonb" },
      ],
    },
  ],
  [
    "store.close_business_day",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_memo", type: "text" },
        { name: "p_pending_receipt_status", type: "text" },
        { name: "p_ignore_closing_requirements", type: "boolean" },
      ],
    },
  ],
  [
    "store.create_cast",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_display_name", type: "text" },
        { name: "p_drink_memo", type: "text" },
      ],
    },
  ],
  [
    "store.update_cast_drink_memo",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_cast_id", type: "bigint" },
        { name: "p_drink_memo", type: "text" },
      ],
    },
  ],
  [
    "store.delete_cast",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_cast_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.upsert_item_category",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_item_category_id", type: "bigint" },
        { name: "p_category_code", type: "text" },
        { name: "p_category_name", type: "text" },
        { name: "p_sort_order", type: "integer" },
        { name: "p_is_active", type: "boolean" },
      ],
    },
  ],
  [
    "store.upsert_item",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_item_id", type: "bigint" },
        { name: "p_item_category_id", type: "bigint" },
        { name: "p_item_name", type: "text" },
        { name: "p_default_price", type: "numeric" },
        { name: "p_is_active", type: "boolean" },
        { name: "p_is_cast_back_target", type: "boolean" },
        { name: "p_cast_back_regular_unit_amount", type: "numeric" },
        { name: "p_cast_back_nomination_unit_amount", type: "numeric" },
        { name: "p_cast_back_type", type: "text" },
      ],
    },
  ],
  [
    "store.delete_item",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_item_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.reorder_items",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_items", type: "jsonb" },
      ],
    },
  ],
  [
    "store.create_slip",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_table_id", type: "bigint" },
        { name: "p_opened_at", type: "timestamp with time zone" },
        { name: "p_customer_labels", type: "text[]" },
        { name: "p_cast_nominations", type: "jsonb" },
        { name: "p_memo", type: "text" },
      ],
    },
  ],
  [
    "store.add_slip_customers",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_customer_labels", type: "text[]" },
        { name: "p_entered_at", type: "timestamp with time zone" },
      ],
    },
  ],
  [
    "store.add_slip_nominations",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_cast_nominations", type: "jsonb" },
      ],
    },
  ],
  [
    "store.save_slip_adjustments",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_adjustment_lines", type: "jsonb" },
      ],
    },
  ],
  [
    "store.add_slip_adjustment",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_line_name", type: "text" },
        { name: "p_amount", type: "numeric" },
      ],
    },
  ],
  [
    "store.save_karaoke_lines",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_business_day_id", type: "bigint" },
        { name: "p_karaoke_lines", type: "jsonb" },
      ],
    },
  ],
  [
    "store.save_order_line_quantities",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_order_lines", type: "jsonb" },
      ],
    },
  ],
  [
    "store.leave_slip_customer",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_customer_id", type: "bigint" },
        { name: "p_left_at", type: "timestamp with time zone" },
      ],
    },
  ],
  [
    "store.update_slip_customer_label",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_customer_id", type: "bigint" },
        { name: "p_customer_label", type: "text" },
      ],
    },
  ],
  [
    "store.void_order_line",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_order_line_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.add_order_lines",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_order_lines", type: "jsonb" },
      ],
    },
  ],
  [
    "store.confirm_checkout",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_payments", type: "jsonb" },
        { name: "p_received_amount", type: "numeric" },
      ],
    },
  ],
  [
    "store.issue_checkout_statement",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
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
    "store.release_checkout_ready",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
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
    "store.cancel_checkout",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
      ],
    },
  ],
  [
    "store.quick_enter_receipt",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_document_id", type: "text" },
        { name: "p_payment_date", type: "date" },
        { name: "p_amount", type: "numeric" },
        { name: "p_account_subject", type: "text" },
        { name: "p_description", type: "text" },
        { name: "p_group_code", type: "text" },
        { name: "p_journal_payload", type: "jsonb" },
        { name: "p_status", type: "text" },
      ],
    },
  ],
  [
    "store.mark_receipt_scan_mistake",
    {
      result: "rows",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_document_id", type: "text" },
        { name: "p_status", type: "text" },
      ],
    },
  ],
  [
    "store.save_cast_sales_adjustment",
    {
      result: "scalar",
      params: [
        { name: "p_department_id", type: "bigint" },
        { name: "p_slip_id", type: "bigint" },
        { name: "p_adjustments", type: "jsonb" },
        { name: "p_source_amount_type", type: "text" },
        { name: "p_split_mode", type: "text" },
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
  const values = definition.params.map((param) => toSqlValue(source[param.name], param.type));
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
