import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const docs = fs.readdirSync(path.join(root, "docs"), { withFileTypes: true })
  .filter((entry) => entry.isFile())
  .map((entry) => entry.name)
  .sort();

assert.deepEqual(docs, ["requirements-definition.md", "system-specification.md"]);

for (const name of [
  ".mcp.json",
  "AGENTS.md",
  "HANDOFF.md",
  "HANDOFF_MASTER_IMPACT_REPORT_20260730.md",
  "HANDOFF_PROSPER_OFFICE_INITIAL_IMPLEMENTATION.md",
  "HANDOFF_PROSPER_TEST_SETUP.md",
  "HANDOFF_RPC_FLOW_RESEARCH_20260806.md",
  "HANDOFF_TECHNICAL_REPORT_20260730.md",
  "HANDOFF_UI_FUNCTION_REPORT_20260730.md"
]) {
  assert.equal(fs.existsSync(path.join(root, name)), false, `${name} must not be public`);
}

assert.equal(
  fs.existsSync(path.join(root, "Sql", "agent_schema_reference.sql")),
  false,
  "agent schema reference must not be public"
);

console.log("Public document contract checks passed.");
