import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const layoutPath = new URL("../Pages/Shared/_Layout.cshtml", import.meta.url);
const manifestPath = new URL("../wwwroot/site.webmanifest", import.meta.url);
const iconPath = new URL("../wwwroot/icons/store-app-icon.svg", import.meta.url);
const siteCssPath = new URL("../wwwroot/css/site.css", import.meta.url);
const themePath = new URL("../wwwroot/css/dark-theme.css", import.meta.url);
const localSettingsPath = new URL("../Features/Settings/LocalSettings.cs", import.meta.url);
const localSettingsProviderPath = new URL("../Services/LocalSettingsProvider.cs", import.meta.url);
const settingsPagePath = new URL("../Pages/Settings/Index.cshtml", import.meta.url);
const settingsPageModelPath = new URL("../Pages/Settings/Index.cshtml.cs", import.meta.url);

test("the default dark theme is enabled at the document and PWA level", async () => {
  const [layout, manifestSource, icon] = await Promise.all([
    readFile(layoutPath, "utf8"),
    readFile(manifestPath, "utf8"),
    readFile(iconPath, "utf8"),
  ]);
  const manifest = JSON.parse(manifestSource.replace(/^\uFEFF/, ""));

  assert.match(layout, /var localSettings = LocalSettingsProvider\.GetCurrent\(\);/);
  assert.match(layout, /var isDarkTheme = localSettings\.ThemeMode != LocalSettings\.ThemeModeWhite;/);
  assert.match(layout, /var bootstrapTheme = isDarkTheme \? "dark" : "light";/);
  assert.match(layout, /var themeColor = isDarkTheme \? "#101318" : "#f8fafc";/);
  assert.match(layout, /<html lang="ja" data-bs-theme="@bootstrapTheme">/);
  assert.match(layout, /<meta name="theme-color" content="@themeColor" \/>/);
  assert.equal(manifest.background_color, "#101318");
  assert.equal(manifest.theme_color, "#101318");
  assert.match(icon, /fill="#101318"/);
  assert.match(icon, /fill="#9cc7ff"/);
});

test("the dark theme is conditionally loaded after all feature styles", async () => {
  const [layout, siteCss] = await Promise.all([
    readFile(layoutPath, "utf8"),
    readFile(siteCssPath, "utf8"),
  ]);
  const imports = [...siteCss.matchAll(/@import url\("([^"]+)"\);/g)].map(
    ([, path]) => path,
  );
  const siteCssIndex = layout.indexOf('href="~/css/site.css"');
  const darkThemeGuardIndex = layout.indexOf("@if (isDarkTheme)");
  const darkThemeIndex = layout.indexOf('href="~/css/dark-theme.css"');
  const scopedCssIndex = layout.indexOf('href="~/ProsperApp.styles.css"');

  assert.ok(!imports.includes("./dark-theme.css"));
  assert.ok(siteCssIndex >= 0);
  assert.ok(darkThemeGuardIndex > siteCssIndex);
  assert.ok(darkThemeIndex > darkThemeGuardIndex);
  assert.ok(scopedCssIndex > darkThemeIndex);
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

test("administrator settings persist the selected theme mode", async () => {
  const [localSettings, provider, pageModel, page] = await Promise.all([
    readFile(localSettingsPath, "utf8"),
    readFile(localSettingsProviderPath, "utf8"),
    readFile(settingsPageModelPath, "utf8"),
    readFile(settingsPagePath, "utf8"),
  ]);

  assert.match(localSettings, /public const string ThemeModeQuietNavy = "quiet-navy";/);
  assert.match(localSettings, /public const string ThemeModeWhite = "white";/);
  assert.match(localSettings, /public string ThemeMode \{ get; set; \} = ThemeModeQuietNavy;/);
  assert.match(provider, /settings\.ThemeMode is LocalSettings\.ThemeModeQuietNavy or LocalSettings\.ThemeModeWhite/);
  assert.match(provider, /ThemeMode = themeMode/);
  assert.match(pageModel, /ThemeMode = Input\.ThemeMode/);
  assert.match(pageModel, /ThemeMode = settings\.ThemeMode/);
  assert.match(pageModel, /Input\.ThemeMode is not LocalSettings\.ThemeModeQuietNavy and not LocalSettings\.ThemeModeWhite/);
  assert.match(page, /asp-for="Input\.ThemeMode"/);
  assert.match(page, /value="@LocalSettings\.ThemeModeQuietNavy"/);
  assert.match(page, /value="@LocalSettings\.ThemeModeWhite"/);
});
