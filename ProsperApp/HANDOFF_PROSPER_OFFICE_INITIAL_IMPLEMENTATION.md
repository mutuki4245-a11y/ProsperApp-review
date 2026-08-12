# ProsperOffice Initial Implementation Handoff

## Objective

Create the first implementation of a head-office application for the existing Prosper store app.

The current app is the store-side operational app. It handles daily store work: opening/current business day, attendance, slips, orders, checkout, receipts, closing, and store master settings.

The new head-office app should focus on what happens after store closing:

- sales management across stores and periods
- head-office review and approval of closed business days
- payroll calculation and payroll review
- payroll period closing

Use the product name **ProsperOffice** for the head-office app. In Japanese UI text, use **本部アプリ**.

## Repository Context

Work in this repository:

```text
C:\Users\mutuk\Desktop\ProsperApp\ProsperApp
```

Before editing, read:

- `AGENTS.md`
- `docs/requirements-definition.md`
- `docs/system-specification.md`
- `HANDOFF.md`

Important existing constraints:

- The app is ASP.NET Core Razor Pages targeting `.NET net10.0`.
- The existing store app uses Supabase only through the `prosper-rpc` Edge Function and allowlisted PostgreSQL RPCs.
- Do not call Supabase table REST directly.
- Do not add local server startup unless the user explicitly asks.
- Prefer `dotnet build ProsperApp.csproj --no-restore` for verification.
- Do not commit `bin/`, `obj/`, publish output, local secrets, or `.codex/deploy/`.

## Implementation Strategy

For this initial implementation, build ProsperOffice as a **head-office area inside the existing Razor Pages app**, not as a separate repository yet.

Reason:

- It lets the team validate screen flow and terminology quickly.
- It reuses existing authentication, layout, feature flag patterns, CSS, and deployment setup.
- It avoids designing production payroll tables/RPCs before the review workflow is clear.

Keep the code shaped so it can later be split into a separate `ProsperOffice` app.

Use this module structure:

```text
Features/HeadOffice/
Pages/HeadOffice/
wwwroot/css/features/head-office.css
wwwroot/js/features/head-office.js
```

Use `HeadOffice` in C# namespaces and route folders. Avoid using `Management` for head-office concepts because `/Management` already means store settings.

## Domain Terms

Use these terms consistently:

| Term | Japanese UI | Meaning |
| --- | --- | --- |
| Store Closing | 店舗締め | The store has closed a business day. This is already handled by the store app. |
| Head Office Review | 本部レビュー | Head office checks a closed business day for sales, payments, receipts, attendance, and adjustments. |
| Head Office Approval | 本部承認 | Head office approves the reviewed business day as valid for monthly sales and payroll. |
| Payroll Period | 給与期間 | The period used to calculate payroll, usually monthly. |
| Payroll Run | 給与計算 | A calculation result for one payroll period. |
| Payroll Line | 給与明細行 | One person in one payroll run, with calculated components. |
| Payroll Adjustment | 給与調整 | Manual addition or deduction applied by head office. |
| Payroll Closing | 給与締め | The payroll run is locked for payment/export. |

Do not call all of these simply "締め". Store closing, head-office approval, and payroll closing must remain separate states.

## MVP Scope

Implement a usable shell with realistic data models and demo data. Do not implement real payroll persistence or new Supabase RPCs in this first pass unless the user explicitly asks.

Create these pages:

```text
/HeadOffice
/HeadOffice/Sales
/HeadOffice/Sales/Day
/HeadOffice/Payroll
/HeadOffice/Payroll/Run
/HeadOffice/Rules
```

Suggested page responsibilities:

| Route | Purpose |
| --- | --- |
| `/HeadOffice` | Overview dashboard: sales this month, pending review days, payroll status, alerts. |
| `/HeadOffice/Sales` | Store/month sales list with review status and totals. |
| `/HeadOffice/Sales/Day` | One closed business day review screen. Use query params for `departmentId` and `businessDate` later. |
| `/HeadOffice/Payroll` | Payroll period list and current payroll summary. |
| `/HeadOffice/Payroll/Run` | Payroll run detail with person-level lines and adjustments. |
| `/HeadOffice/Rules` | Read-only placeholder for payroll/sales rules in the initial implementation. |

The first pass should render useful screens, not empty placeholders. Use in-memory demo data behind an interface so real repositories can replace it.

## Suggested Interfaces

Create a small application service interface:

```csharp
public interface IHeadOfficeApplicationService
{
    Task<HeadOfficeDashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken);
    Task<SalesReviewListViewModel> GetSalesReviewsAsync(CancellationToken cancellationToken);
    Task<SalesDayReviewViewModel> GetSalesDayReviewAsync(int departmentId, DateOnly businessDate, CancellationToken cancellationToken);
    Task<PayrollPeriodListViewModel> GetPayrollPeriodsAsync(CancellationToken cancellationToken);
    Task<PayrollRunViewModel> GetPayrollRunAsync(string periodId, CancellationToken cancellationToken);
    Task<HeadOfficeRulesViewModel> GetRulesAsync(CancellationToken cancellationToken);
}
```

Create one demo adapter:

```text
Features/HeadOffice/DemoHeadOfficeApplicationService.cs
```

Do not introduce a repository seam until there is a real database adapter. One adapter means the seam is hypothetical.

## UI Direction

This is an operational head-office app, not a marketing page.

Design goals:

- dense but readable
- fast scanning
- clear exception states
- restrained color
- no hero section
- no decorative card-heavy landing layout

Use tables, compact summary panels, tabs, badges, filters, and action buttons. Cards are fine for repeated business objects or summary panels, but do not put cards inside cards.

Suggested navigation labels:

```text
本部
売上管理
営業日レビュー
給与管理
給与明細
ルール
```

Add a top-level navigation entry to the existing layout for **本部** only if it does not disrupt store operation. If adding global navigation feels risky, link to `/HeadOffice` from an obvious but low-impact place and keep the rest self-contained.

## Demo Data Requirements

Use realistic demo data that reflects the current store app domain:

- at least 2 stores
- current month and previous month
- multiple closed business days
- review statuses: pending, approved, needs correction
- payment methods: cash, card, other
- attendance count
- gross sales, discounts/adjustments if useful, net sales
- drink delivery amount
- drink back amount
- cast sales adjustment amount
- payroll lines for casts/staff
- payroll components such as attendance pay, nomination back, drink back, sales incentive, adjustment, deduction, total

Make the demo data deterministic in code. Do not read/write local files for demo data.

## Permission And Risk Rules

Do not change existing store workflows in this first pass:

- do not change checkout behavior
- do not change store closing behavior
- do not change payroll-related business calculations in existing store code
- do not change Supabase SQL or Edge Function allowlist
- do not add production database tables
- do not add Azure deployment steps

If the implementation needs a feature flag, add `HeadOffice` to the feature flag system in the same style as existing features. Default behavior should not accidentally expose incomplete payroll screens in production unless the current config pattern already enables all features.

If adding the feature flag creates too much churn, leave the route authenticated and clearly mark the screen as an initial head-office preview in code naming, not in visible marketing copy.

## Expected Files

Likely files to add or edit:

```text
Program.cs
Services/FeatureNames.cs
Pages/Shared/_Layout.cshtml
Pages/HeadOffice/Index.cshtml
Pages/HeadOffice/Index.cshtml.cs
Pages/HeadOffice/Sales/Index.cshtml
Pages/HeadOffice/Sales/Index.cshtml.cs
Pages/HeadOffice/Sales/Day.cshtml
Pages/HeadOffice/Sales/Day.cshtml.cs
Pages/HeadOffice/Payroll/Index.cshtml
Pages/HeadOffice/Payroll/Index.cshtml.cs
Pages/HeadOffice/Payroll/Run.cshtml
Pages/HeadOffice/Payroll/Run.cshtml.cs
Pages/HeadOffice/Rules.cshtml
Pages/HeadOffice/Rules.cshtml.cs
Features/HeadOffice/IHeadOfficeApplicationService.cs
Features/HeadOffice/HeadOfficeModels.cs
Features/HeadOffice/DemoHeadOfficeApplicationService.cs
wwwroot/css/features/head-office.css
```

Keep edits scoped. Do not refactor existing store app pages unless required for navigation.

## Verification

Run at minimum:

```powershell
dotnet build ProsperApp.csproj --no-restore
```

If JavaScript is added:

```powershell
node --check wwwroot/js/features/head-office.js
```

If existing JS files are touched:

```powershell
node --test Tests/*.test.mjs
```

Do not start `dotnet run` unless the user explicitly asks.

## Final Report Requirements

Report:

- implemented routes
- files changed
- verification commands and results
- whether any existing store behavior was changed
- remaining work before production use

If real SQL, RPC, auth, payroll formula, or deployment work becomes necessary, stop and ask for explicit approval before doing it.

