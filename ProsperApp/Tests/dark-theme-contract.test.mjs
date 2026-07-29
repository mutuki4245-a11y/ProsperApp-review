import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const layoutPath = new URL("../Pages/Shared/_Layout.cshtml", import.meta.url);
const manifestPath = new URL("../wwwroot/site.webmanifest", import.meta.url);
const iconPath = new URL("../wwwroot/icons/store-app-icon.svg", import.meta.url);
const siteCssPath = new URL("../wwwroot/css/site.css", import.meta.url);
const themePath = new URL("../wwwroot/css/dark-theme.css", import.meta.url);

test("the dark theme is enabled at the document and PWA level", async () => {
  const [layout, manifestSource, icon] = await Promise.all([
    readFile(layoutPath, "utf8"),
    readFile(manifestPath, "utf8"),
    readFile(iconPath, "utf8"),
  ]);
  const manifest = JSON.parse(manifestSource.replace(/^\uFEFF/, ""));

  assert.match(layout, /<html lang="ja" data-bs-theme="dark">/);
  assert.match(layout, /<meta name="theme-color" content="#0d1210" \/>/);
  assert.equal(manifest.background_color, "#0d1210");
  assert.equal(manifest.theme_color, "#0d1210");
  assert.match(icon, /fill="#0d1210"/);
  assert.match(icon, /fill="#35c4ae"/);
});

test("the dark theme is loaded after all feature styles", async () => {
  const siteCss = await readFile(siteCssPath, "utf8");
  const imports = [...siteCss.matchAll(/@import url\("([^"]+)"\);/g)].map(
    ([, path]) => path,
  );

  assert.equal(imports.at(-1), "./dark-theme.css");
});

test("the theme covers shared controls and every operational surface", async () => {
  const theme = await readFile(themePath, "utf8");
  const requiredSelectors = [
    ".app-main-nav",
    ".modal-content",
    ".form-control",
    ".business-floor",
    ".business-slip-card.slip-list__row",
    ".order-entry__panel",
    ".closing-panel",
    ".opening-list__card",
    ".settings-card",
    ".account-sheet__item",
  ];

  for (const selector of requiredSelectors) {
    assert.ok(theme.includes(selector), `missing dark theme selector: ${selector}`);
  }

  assert.match(theme, /@media print\s*\{/);
  assert.doesNotMatch(theme, /(?:linear|radial)-gradient\(/);
  assert.equal(
    [...theme].filter((character) => character === "{").length,
    [...theme].filter((character) => character === "}").length,
  );
});
