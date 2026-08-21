import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";

const root = process.cwd();
const program = fs.readFileSync(path.join(root, "Program.cs"), "utf8");
const options = fs.readFileSync(path.join(root, "Options", "ReviewAuthOptions.cs"), "utf8");

assert.match(program, /Configure<ReviewAuthOptions>/);
assert.match(program, /MapGet\("\/review-login"/);
assert.match(program, /BindUserAccessAsync\(/);
assert.match(program, /ProsperAccessClaims\.GoogleSubject/);
assert.match(program, /ProsperAccessClaims\.Department/);
assert.match(program, /ProsperAccessClaims\.DepartmentRole/);
assert.match(options, /CryptographicOperations\.FixedTimeEquals/);
assert.doesNotMatch(options, /Enabled\s*=\s*true/);

console.log("Review auth test-database contract checks passed.");
