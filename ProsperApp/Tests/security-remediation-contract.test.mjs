import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../', import.meta.url));
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), 'utf8');
const rpcDirectory = path.join(root, 'Sql/store_rpc');
const rpcSqlFiles = fs.readdirSync(rpcDirectory).filter((name) => name.endsWith('.sql'));
const edge = read('supabase/functions/prosper-rpc/index.ts');
const accessSql = read('Sql/store_rpc/00b_app_access.sql');
const grants = read('Sql/store_rpc/99_grants.sql');

test('Edgeは専用DB URLと署名actor envelopeだけを新経路に使う', () => {
  assert.match(edge, /Deno\.env\.get\("PROSPER_RPC_DB_URL"\)/);
  assert.doesNotMatch(edge, /Deno\.env\.get\("SUPABASE_DB_URL"\)/);
  assert.match(edge, /x-prosper-timestamp/);
  assert.match(edge, /x-prosper-signature/);
  assert.match(edge, /authorize_app_user_access/);
  assert.doesNotMatch(edge, /Access-Control-Allow-Origin/);
  assert.doesNotMatch(edge, /message:\s*error instanceof Error/);
});

test('権限表はprivateで、検証済みemailとGoogle subjectを原子的に紐付ける', () => {
  assert.match(accessSql, /create table if not exists store\.app_users/);
  assert.match(accessSql, /create table if not exists store\.app_user_department_access/);
  assert.match(accessSql, /p_email_verified is not true/);
  assert.match(accessSql, /for update;/);
  assert.match(accessSql, /google_subject is distinct from v_subject/);
  assert.match(accessSql, /security definer[\s\S]*?set search_path = pg_catalog, store, public/i);
  assert.match(accessSql, /revoke all on table store\.app_users from public, anon, authenticated, service_role/);
});

test('専用roleは非継承で、全権限剥奪後にallowlist関数だけをgrantする', () => {
  assert.match(grants, /create role prosper_rpc_executor login noinherit nosuperuser nocreatedb nocreaterole noreplication/);
  assert.match(grants, /revoke all on all tables in schema store from prosper_rpc_executor/);
  assert.match(grants, /revoke execute on all functions in schema store from prosper_rpc_executor/);
  const edgeAllowlist = new Set([...edge.matchAll(/"store\.([a-z0-9_]+)"\s*,\s*\{/g)].map((match) => match[1]));
  const grantBlock = /v_allowlist constant text\[\] := array\[([\s\S]*?)\];/.exec(grants)?.[1] ?? '';
  const grantAllowlist = new Set([...grantBlock.matchAll(/'([a-z0-9_]+)'/g)].map((match) => match[1]));
  assert.deepEqual([...grantAllowlist].sort(), [...edgeAllowlist].sort());
});

test('traffic依存cleanupを廃止し、補正ledgerをcron保持対象外にする', () => {
  for (const file of rpcSqlFiles.filter((name) => name !== '32_operation_result_cleanup.sql')) {
    const sql = read(`Sql/store_rpc/${file}`);
    assert.doesNotMatch(
      sql,
      /delete from store\.(?:business_home_sync_results|current_business_day_operation_results|receipt_work_queue_operations|management_master_operation_results)\s+where created_at < now\(\) - interval '30 days'/i,
      `${file} にtraffic依存cleanupを残さないこと。`,
    );
  }
  const cleanup = read('Sql/store_rpc/32_operation_result_cleanup.sql');
  assert.match(cleanup, /operation_type is distinct from 'save_sales_history_correction_v1'/);
  assert.match(cleanup, /'30 3 \* \* \*'/);
});

test('共有operation DDLと業務定数は00_schemaへ集約する', () => {
  const schema = read('Sql/store_rpc/00_schema.sql');
  assert.match(schema, /create table if not exists store\.current_business_day_operation_results/);
  assert.match(schema, /create or replace function store\.business_timezone\(\)/);
  for (const file of rpcSqlFiles.filter((name) => name !== '00_schema.sql')) {
    const sql = read(`Sql/store_rpc/${file}`);
    assert.doesNotMatch(sql, /create table if not exists store\.current_business_day_operation_results/);
    assert.doesNotMatch(sql, /at time zone 'Asia\/Tokyo'|time '12:00'|\* 0\.20|\* 10 \/ 110/);
  }
});

test('共有PIN・許可メール設定・raw error分類を残さない', () => {
  const settingsModel = read('Pages/Settings/Index.cshtml.cs');
  const settingsView = read('Pages/Settings/Index.cshtml');
  const appSettings = read('appsettings.json');
  const repositories = fs.readdirSync(path.join(root, 'Infrastructure/Supabase'))
    .filter((name) => name.endsWith('.cs'))
    .map((name) => read(`Infrastructure/Supabase/${name}`))
    .join('\n');
  assert.doesNotMatch(settingsModel + settingsView, /4245|SettingsSaveToken|Password/);
  assert.doesNotMatch(appSettings, /AllowedEmails|AllowedDomains|DetailedErrors/);
  assert.doesNotMatch(repositories, /(?:rawError|message|error)\.Contains\(/);
});

test('CSPと安全なレスポンスヘッダーを一元適用する', () => {
  const program = read('Program.cs');
  assert.match(program, /script-src 'self' 'nonce-\{nonce\}' https:\/\/www\.sii-ps\.com/);
  assert.match(program, /object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'/);
  assert.match(program, /XContentTypeOptions = "nosniff"/);
  assert.match(program, /XFrameOptions = "SAMEORIGIN"/);
  assert.match(program, /strict-origin-when-cross-origin/);
});

test('Google OAuth scopeは旧Azure設定に影響されずDrive読取専用へ固定する', () => {
  const program = read('Program.cs');
  const driveOptions = read('Options/GoogleDriveOptions.cs');
  const appSettings = read('appsettings.json');
  assert.match(program, /options\.Scope\.Clear\(\)/);
  assert.match(program, /options\.Scope\.Add\("https:\/\/www\.googleapis\.com\/auth\/drive\.readonly"\)/);
  assert.doesNotMatch(program, /googleDriveOptions\.Scopes/);
  assert.doesNotMatch(driveOptions + appSettings, /\bScopes\b|https:\/\/www\.googleapis\.com\/auth\/drive(?!\.readonly)/);
});
