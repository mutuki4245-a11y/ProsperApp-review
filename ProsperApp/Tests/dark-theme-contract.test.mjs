import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const layoutPath = new URL("../Pages/Shared/_Layout.cshtml", import.meta.url);
const manifestPath = new URL("../wwwroot/site.webmanifest", import.meta.url);
const iconPath = new URL("../wwwroot/icons/store-app-icon.svg", import.meta.url);
const themePath = new URL("../wwwroot/css/dark-theme.css", import.meta.url);
const localSettingsPath = new URL("../Features/Settings/LocalSettings.cs", import.meta.url);
const localSettingsProviderPath = new URL("../Services/LocalSettingsProvider.cs", import.meta.url);
const settingsPagePath = new URL("../Pages/Settings/Index.cshtml", import.meta.url);
const settingsPageModelPath = new URL("../Pages/Settings/Index.cshtml.cs", import.meta.url);
const storeSettingsPagePath = new URL("../Pages/Management/Index.cshtml", import.meta.url);
const storeSettingsPageModelPath = new URL("../Pages/Management/Index.cshtml.cs", import.meta.url);
const ordersPagePath = new URL("../Pages/Orders/Index.cshtml", import.meta.url);

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
  const layout = await readFile(layoutPath, "utf8");
  const orderedStyles = [
    "base.css",
    "features/opening.css",
    "features/slips.css",
    "features/orders.css",
    "features/closing.css",
    "features/sales-history.css",
    "features/settings.css",
    "layout.css",
  ];
  const styleIndexes = orderedStyles.map((path) => layout.indexOf(`href="~/css/${path}"`));
  const darkThemeGuardIndex = layout.indexOf("@if (isDarkTheme)");
  const darkThemeIndex = layout.indexOf('href="~/css/dark-theme.css"');
  const scopedCssIndex = layout.indexOf('href="~/ProsperApp.styles.css"');

  assert.ok(styleIndexes.every((index) => index >= 0));
  assert.deepEqual(styleIndexes, [...styleIndexes].sort((left, right) => left - right));
  assert.ok(darkThemeGuardIndex > styleIndexes.at(-1));
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

  assert.match(
    theme,
    /\.business-slip-card__meta\s*,\s*\.business-slip-card__meta-separator\s*\{[^}]*color:\s*var\(--app-text-muted\)/s,
  );
  assert.match(
    theme,
    /\.business-slip-card__person--nomination\s*\{[^}]*background:\s*#3a1f2f;[^}]*border-color:\s*#8b4367;[^}]*color:\s*#f9c7dc/s,
  );
  assert.match(
    theme,
    /\.business-slip-card\.slip-list__row\s*\{[^}]*--business-slip-accent:\s*#72aaa3;[^}]*--business-slip-header-background:\s*#244441;[^}]*--business-slip-header-hover:\s*#2b504d;[^}]*--business-slip-money-color:\s*var\(--app-money\);[^}]*border-top-color:\s*var\(--business-slip-accent\)/s,
  );
  assert.match(
    theme,
    /\.business-slip-card\.slip-list__row--checkout-ready\s*\{[^}]*--business-slip-header-background:\s*#253f5b/s,
  );
  assert.match(
    theme,
    /\.business-slip-card\.slip-list__row--checked-out\s*\{[^}]*--business-slip-header-background:\s*#343b42/s,
  );
  assert.match(
    theme,
    /\.business-slip-card__header\s*\{[^}]*background:\s*var\(--business-slip-header-background\);[^}]*border-color:\s*var\(--business-slip-header-border\)/s,
  );
  assert.match(theme, /@media print\s*\{/);
  assert.doesNotMatch(theme, /(?:linear|radial)-gradient\(/);
  assert.equal(
    [...theme].filter((character) => character === "{").length,
    [...theme].filter((character) => character === "}").length,
  );
});

test("store settings persist the selected screen and theme modes", async () => {
  const [localSettings, provider, adminPageModel, adminPage, storePageModel, storePage, layout, ordersPage] = await Promise.all([
    readFile(localSettingsPath, "utf8"),
    readFile(localSettingsProviderPath, "utf8"),
    readFile(settingsPageModelPath, "utf8"),
    readFile(settingsPagePath, "utf8"),
    readFile(storeSettingsPageModelPath, "utf8"),
    readFile(storeSettingsPagePath, "utf8"),
    readFile(layoutPath, "utf8"),
    readFile(ordersPagePath, "utf8"),
  ]);

  assert.match(localSettings, /public const string ThemeModeQuietNavy = "quiet-navy";/);
  assert.match(localSettings, /public const string ThemeModeWhite = "white";/);
  assert.match(localSettings, /public string ThemeMode \{ get; set; \} = ThemeModeQuietNavy;/);
  assert.match(provider, /settings\.ThemeMode is LocalSettings\.ThemeModeQuietNavy or LocalSettings\.ThemeModeWhite/);
  assert.match(provider, /ThemeMode = themeMode/);
  assert.match(storePageModel, /public IActionResult OnPostSaveDisplaySettings\(\)/);
  assert.match(storePageModel, /ScreenMode is not "sales-management" and not "order-entry"/);
  assert.match(storePageModel, /ThemeMode is not LocalSettings\.ThemeModeQuietNavy and not LocalSettings\.ThemeModeWhite/);
  assert.match(storePageModel, /StoreName = currentSettings\.StoreName/);
  assert.match(storePageModel, /StoreDepartmentId = currentSettings\.StoreDepartmentId/);
  assert.match(storePageModel, /ScreenMode = ScreenMode/);
  assert.match(storePageModel, /ThemeMode = ThemeMode/);
  assert.match(storePage, /ViewData\["Title"\] = "店舗設定"/);
  assert.match(storePage, /asp-for="ScreenMode"/);
  assert.match(storePage, /value="sales-management"/);
  assert.match(storePage, /value="order-entry"/);
  assert.match(storePage, /asp-for="ThemeMode"/);
  assert.match(storePage, /value="@LocalSettings\.ThemeModeQuietNavy"/);
  assert.match(storePage, /value="@LocalSettings\.ThemeModeWhite"/);
  assert.match(layout, /asp-page="\/Management\/Index">店舗設定<\/a>/);
  assert.match(ordersPage, /asp-page="\/Management\/Index">店舗設定<\/a>/);
  assert.match(adminPageModel, /ScreenMode = currentSettings\.ScreenMode/);
  assert.match(adminPageModel, /ThemeMode = currentSettings\.ThemeMode/);
  assert.doesNotMatch(adminPageModel, /Input\.ScreenMode/);
  assert.doesNotMatch(adminPageModel, /Input\.ThemeMode/);
  assert.doesNotMatch(adminPage, /asp-for="Input\.ScreenMode"/);
  assert.doesNotMatch(adminPage, /asp-for="Input\.ThemeMode"/);
  assert.match(adminPage, /<h2>利用店舗<\/h2>/);
});
