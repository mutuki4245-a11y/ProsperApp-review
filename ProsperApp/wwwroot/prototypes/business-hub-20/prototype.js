(() => {
  "use strict";

  const variants = [
    { key: "A", name: "高密度台帳" },
    { key: "B", name: "卓ボード" },
    { key: "C", name: "滞在タイムライン" },
    { key: "D", name: "夜レジ風伝票パネル" }
  ];

  const slips = [
    { id: 89, table: "A1", time: "18:15", guest: "田中様", guests: 1, casts: [], amount: 18200, karaoke: 1 },
    { id: 90, table: "A2", time: "18:35", guest: "佐藤様・鈴木様", guests: 2, casts: ["むつき"], amount: 36800, karaoke: 2 },
    { id: 91, table: "A3", time: "18:55", guest: "高橋様ほか", guests: 3, casts: ["むつき"], amount: 52200, karaoke: 3 },
    { id: 92, table: "A4", time: "19:15", guest: "山本様ほか", guests: 4, casts: ["えいこ"], amount: 67400, karaoke: 4 },
    { id: 93, table: "A5", time: "19:35", guest: "吉田様・山田様", guests: 2, casts: ["むつき", "はやと"], amount: 48600, karaoke: 5 },
    { id: 94, table: "A6", time: "19:55", guest: "佐々木様", guests: 1, casts: ["あやか"], amount: 27600, karaoke: 0 },
    { id: 95, table: "B1", time: "20:15", guest: "山口様ほか", guests: 5, casts: [], amount: 73800, karaoke: 1 },
    { id: 96, table: "B2", time: "20:35", guest: "斎藤様ほか", guests: 3, casts: ["えいこ"], amount: 59400, karaoke: 2 },
    { id: 97, table: "B3", time: "20:55", guest: "森様・池田様", guests: 2, casts: ["はやと"], amount: 41600, karaoke: 3 },
    { id: 98, table: "B4", time: "21:15", guest: "橋本様ほか", guests: 4, casts: ["むつき", "あやか"], amount: 81200, karaoke: 4 },
    { id: 99, table: "B5", time: "21:35", guest: "非常に長いお客様名表示確認用", guests: 1, casts: ["あやか"], amount: 32400, karaoke: 5 },
    { id: 100, table: "B6", time: "21:55", guest: "藤田様・後藤様", guests: 2, casts: [], amount: 28800, karaoke: 0 },
    { id: 101, table: "C1", time: "22:15", guest: "岡田様ほか", guests: 3, casts: ["えいこ"], amount: 53800, karaoke: 1 },
    { id: 102, table: "C2", time: "22:35", guest: "近藤様", guests: 1, casts: ["むつき"], amount: 24600, karaoke: 2 },
    { id: 103, table: "C3", time: "22:55", guest: "石井様ほか", guests: 4, casts: ["はやと"], amount: 69400, karaoke: 3 },
    { id: 104, table: "C4", time: "23:15", guest: "斉藤様・福田様", guests: 2, casts: ["あやか"], amount: 37200, karaoke: 4 },
    { id: 105, table: "C5", time: "23:35", guest: "太田様ほか", guests: 5, casts: ["むつき", "えいこ"], amount: 88600, karaoke: 5 },
    { id: 106, table: "C6", time: "23:55", guest: "中島様ほか", guests: 3, casts: [], amount: 100000, karaoke: 0 },
    { id: 107, table: "A1", time: "00:25", guest: "A1二組目", guests: 2, casts: ["えいこ"], amount: 29800, karaoke: 1 },
    { id: 108, table: "B3", time: "00:55", guest: "B3二組目", guests: 1, casts: ["むつき", "あやか"], amount: 21400, karaoke: 2 }
  ];

  const checkedOutSlips = [
    { id: 81, table: "A2", time: "17:20", checkedOutAt: "19:08", guest: "松本様", guests: 2, casts: ["えいこ"], amount: 42600, karaoke: 1 },
    { id: 83, table: "A5", time: "18:05", checkedOutAt: "20:12", guest: "井上様ほか", guests: 3, casts: ["むつき"], amount: 58800, karaoke: 2 },
    { id: 85, table: "B1", time: "19:10", checkedOutAt: "21:04", guest: "清水様", guests: 1, casts: [], amount: 19600, karaoke: 0 },
    { id: 87, table: "C3", time: "20:30", checkedOutAt: "22:46", guest: "木村様・林様", guests: 2, casts: ["あやか", "はやと"], amount: 51400, karaoke: 3 }
  ];

  const tableNames = ["A1", "A2", "A3", "A4", "A5", "A6", "B1", "B2", "B3", "B4", "B5", "B6", "C1", "C2", "C3", "C4", "C5", "C6"];
  const app = document.querySelector("#prototypeApp");
  const switcherLabel = document.querySelector("[data-variant-label]");
  let currentVariant = readVariant();
  let searchText = "";
  let selectedTable = "A1";
  let nightFilter = "all";

  function readVariant() {
    const value = new URLSearchParams(window.location.search).get("variant")?.toUpperCase();
    return variants.some((variant) => variant.key === value) ? value : "A";
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;")
      .replaceAll("'", "&#039;");
  }

  function yen(amount) {
    return `${new Intl.NumberFormat("ja-JP").format(amount)} 円`;
  }

  function timeToMinutes(time) {
    const [hour, minute] = time.split(":").map(Number);
    return (hour < 12 ? hour + 24 : hour) * 60 + minute;
  }

  function durationMinutes(slip) {
    return (25 * 60 + 10) - timeToMinutes(slip.time);
  }

  function durationText(slip) {
    const minutes = durationMinutes(slip);
    return `${Math.floor(minutes / 60)}時間${String(minutes % 60).padStart(2, "0")}分`;
  }

  function groupsFor(table, source = slips) {
    return source
      .filter((slip) => slip.table === table)
      .sort((left, right) => timeToMinutes(left.time) - timeToMinutes(right.time));
  }

  function groupIndex(slip) {
    return groupsFor(slip.table).findIndex((row) => row.id === slip.id) + 1;
  }

  function matchesSearch(slip) {
    if (!searchText) return true;
    const haystack = [slip.table, slip.time, slip.guest, ...slip.casts].join(" ").toLocaleLowerCase("ja");
    return haystack.includes(searchText.toLocaleLowerCase("ja"));
  }

  function filteredSlips() {
    return slips.filter(matchesSearch);
  }

  function tableCategory(table) {
    return table.match(/^[^\d]+/)?.[0] ?? "その他";
  }

  function compareSlipsByTable(left, right) {
    return left.table.localeCompare(right.table, "ja", { numeric: true })
      || timeToMinutes(left.time) - timeToMinutes(right.time)
      || left.id - right.id;
  }

  function nightState(slip) {
    if ([105, 108].includes(slip.id)) {
      return { key: "checkout", label: "会計準備中" };
    }
    if ([89, 94, 99, 104].includes(slip.id)) {
      return { key: "extension", label: "延長確認" };
    }
    return { key: "active", label: "接客中" };
  }

  function nightRemainingMinutes(slip) {
    const state = nightState(slip);
    if (state.key === "checkout") return 0;
    if (state.key === "extension") return -([89, 94, 99, 104].indexOf(slip.id) + 1) * 4;
    return 8 + ((slip.id * 7) % 43);
  }

  function nightTimer(slip) {
    const remaining = nightRemainingMinutes(slip);
    if (remaining < 0) return `<strong>超過 ${Math.abs(remaining)}分</strong><span>延長確認が必要です</span>`;
    if (remaining === 0) return "<strong>会計待ち</strong><span>伝票内容を固定中</span>";
    return `<strong>残り ${remaining}分</strong><span>${durationText(slip)}滞在</span>`;
  }

  function nightFilteredSlips() {
    return slips
      .filter((slip) => {
        if (nightFilter === "extension") return nightState(slip).key === "extension";
        if (nightFilter === "checkout") return nightState(slip).key === "checkout";
        return true;
      })
      .sort(compareSlipsByTable);
  }

  function shell(content, description) {
    const total = slips.reduce((sum, slip) => sum + slip.amount, 0);
    return `
      <div class="prototype-note">比較専用・保存されません</div>
      <section class="page-head">
        <div class="page-head__title">
          <h1>営業中ハブ</h1>
          <p>2026-07-27 ／ 20件同時openの表示検証</p>
        </div>
        <button class="primary-button" type="button">＋ 伝票を追加</button>
      </section>
      <section class="summary-bar" aria-label="営業中サマリー">
        <div class="summary-stat"><span>営業日</span><strong>7月27日</strong></div>
        <div class="summary-stat"><span>未会計</span><strong>20 件</strong></div>
        <div class="summary-stat is-alert"><span>同卓複数組</span><strong>2 卓</strong></div>
        <div class="summary-stat"><span>総客数</span><strong>${slips.reduce((sum, slip) => sum + slip.guests, 0)} 名</strong></div>
        <div class="summary-stat"><span>見込み売上</span><strong>${yen(total)}</strong></div>
        <label class="summary-search">
          <input type="search" value="${escapeHtml(searchText)}" placeholder="卓・客名・キャストを検索" data-prototype-search>
        </label>
      </section>
      <section class="attention-strip">
        <span class="attention-strip__label">要確認</span>
        <button class="attention-chip" type="button" data-focus-table="A1">A1：2組</button>
        <button class="attention-chip" type="button" data-focus-table="B3">B3：2組</button>
        <button class="attention-chip" type="button" data-focus-table="A1">A1：6時間55分</button>
        <button class="attention-chip" type="button" data-focus-table="A2">A2：6時間35分</button>
      </section>
      <p class="variant-description">${description}</p>
      ${content}
    `;
  }

  function tableJump(source) {
    return `
      <nav class="table-jump" aria-label="卓へ移動">
        <span class="table-jump__label">卓へ移動</span>
        ${tableNames.map((table) => {
          const count = groupsFor(table, source).length;
          const fullCount = groupsFor(table).length;
          return `<button class="table-chip" type="button" data-focus-table="${table}" data-groups="${fullCount}" ${count === 0 ? "disabled" : ""}>${table}${fullCount > 1 ? `・${fullCount}組` : ""}</button>`;
        }).join("")}
      </nav>
    `;
  }

  function renderVariantA() {
    const source = filteredSlips();
    if (source.length === 0) {
      app.innerHTML = shell('<div class="empty-search">条件に一致する伝票がありません。</div>', "20件を一列で高速走査する案。情報量を落とさず、卓・時刻・客名・指名・金額・操作を揃えます。");
      bindCommonEvents();
      return;
    }

    const zones = ["A", "B", "C"];
    const rows = zones.map((zone) => {
      const zoneSlips = source
        .filter((slip) => slip.table.startsWith(zone))
        .sort((left, right) => left.table.localeCompare(right.table) || timeToMinutes(left.time) - timeToMinutes(right.time));
      if (zoneSlips.length === 0) return "";
      return `
        <div class="ledger__zone"><strong>${zone}エリア</strong><span>${zoneSlips.length}組</span></div>
        ${zoneSlips.map((slip) => {
          const multiple = groupsFor(slip.table).length > 1;
          return `
            <article class="ledger__row ${multiple ? "is-duplicate" : ""}" id="table-${slip.table}-${slip.id}">
              <strong class="ledger__table">${slip.table}</strong>
              <span class="ledger__time">${slip.time}</span>
              <span class="ledger__guest" title="${escapeHtml(slip.guest)}">
                ${escapeHtml(slip.guest)}
                ${multiple ? `<span class="group-tag">${groupIndex(slip)}組目</span>` : ""}
              </span>
              <span class="ledger__count">${slip.guests}名</span>
              <span class="ledger__cast">${slip.casts.length ? escapeHtml(slip.casts.join("・")) : "指名なし"}</span>
              <strong class="ledger__amount">${yen(slip.amount)}</strong>
              <span class="ledger__karaoke">カラオケ ${slip.karaoke}</span>
              <span class="ledger__actions">
                <button class="ledger__detail" type="button">詳細</button>
                <button class="ledger__checkout" type="button">会計伝票</button>
              </span>
            </article>
          `;
        }).join("")}
      `;
    }).join("");

    const content = `
      ${tableJump(source)}
      <section class="ledger">
        <div class="ledger__header" aria-hidden="true">
          <span>卓</span><span>入店</span><span>お客様</span><span>客数</span><span>指名</span><span>会計</span><span>カラオケ</span><span>操作</span>
        </div>
        ${rows}
      </section>
    `;
    app.innerHTML = shell(content, "A：20件を一列で高速走査する案。情報量を落とさず、卓・時刻・客名・指名・金額・操作を揃えます。");
    bindCommonEvents();
  }

  function renderVariantB() {
    const source = filteredSlips();
    const cards = tableNames.map((table) => {
      const groups = groupsFor(table, source);
      if (groups.length === 0) return "";
      const fullCount = groupsFor(table).length;
      return `
        <button class="table-card ${fullCount > 1 ? "has-multiple" : ""} ${selectedTable === table ? "is-selected" : ""}" type="button" data-select-table="${table}">
          <span class="table-card__head"><strong>${table}</strong><span>${fullCount}組</span></span>
          <span class="table-card__groups">
            ${groups.map((slip) => `
              <span class="table-card__group">
                <span class="table-card__time">${slip.time}</span>
                <span class="table-card__guest">${escapeHtml(slip.guest)}</span>
                <span class="table-card__meta">${slip.guests}名 ／ ${slip.casts.length ? escapeHtml(slip.casts.join("・")) : "指名なし"}</span>
              </span>
            `).join("")}
          </span>
        </button>
      `;
    }).join("");

    const selectedGroups = groupsFor(selectedTable);
    const drawer = selectedGroups.length ? `
      <aside class="board-drawer" data-board-drawer>
        <div class="board-drawer__head">
          <h2>${selectedTable} ／ ${selectedGroups.length}組</h2>
          <button class="board-drawer__close" type="button" data-close-drawer aria-label="閉じる">×</button>
        </div>
        ${selectedGroups.map((slip) => `
          <section class="drawer-group">
            <div class="drawer-group__top">
              <strong>${escapeHtml(slip.guest)}</strong>
              <span>${slip.time}入店・${durationText(slip)}</span>
            </div>
            <div class="drawer-group__meta">${slip.guests}名 ／ ${slip.casts.length ? escapeHtml(slip.casts.join("・")) : "指名なし"} ／ ${yen(slip.amount)} ／ カラオケ${slip.karaoke}</div>
            <div class="drawer-group__actions">
              <button class="secondary-button" type="button">詳細</button>
              <button class="primary-button" type="button">会計伝票</button>
            </div>
          </section>
        `).join("")}
      </aside>
    ` : "";

    const content = source.length === 0
      ? '<div class="empty-search">条件に一致する伝票がありません。</div>'
      : `
        <div class="board-toolbar">
          <div class="board-legend"><span>1組</span><span>同卓複数組</span></div>
          <span class="variant-description">卓カードを押すとグループ詳細が開きます</span>
        </div>
        <section class="table-board">${cards}</section>
        ${drawer}
      `;
    app.innerHTML = shell(content, "B：18卓を一画面で見渡す案。同卓複数組はカード内で積み、詳細操作は選択した卓だけに絞ります。");
    bindCommonEvents();
    app.querySelectorAll("[data-select-table]").forEach((button) => {
      button.addEventListener("click", () => {
        selectedTable = button.dataset.selectTable;
        render();
      });
    });
    app.querySelector("[data-close-drawer]")?.addEventListener("click", () => {
      selectedTable = "";
      app.querySelector("[data-board-drawer]")?.remove();
      app.querySelectorAll(".table-card").forEach((card) => card.classList.remove("is-selected"));
    });
  }

  function renderVariantC() {
    const source = filteredSlips();
    const zones = ["A", "B", "C"];
    const rangeStart = 18 * 60;
    const rangeEnd = 25 * 60 + 10;
    const range = rangeEnd - rangeStart;
    const scale = ["18:00", "19:00", "20:00", "21:00", "22:00", "23:00", "00:00", "01:00"];

    const rows = zones.map((zone) => {
      const tables = tableNames.filter((table) => table.startsWith(zone));
      return `
        <div class="timeline-zone">${zone}エリア</div>
        ${tables.map((table) => {
          const groups = groupsFor(table, source);
          if (groups.length === 0) return "";
          const fullCount = groupsFor(table).length;
          const longest = groups.reduce((max, slip) => Math.max(max, durationMinutes(slip)), 0);
          return `
            <div class="timeline-row ${fullCount > 1 ? "has-multiple" : ""}" data-timeline-table="${table}">
              <strong class="timeline-table">${table}${fullCount > 1 ? `<span class="group-tag">${fullCount}組</span>` : ""}</strong>
              <div class="timeline-track">
                ${groups.map((slip) => {
                  const start = Math.max(0, Math.min(100, ((timeToMinutes(slip.time) - rangeStart) / range) * 100));
                  return `<button class="timeline-band ${durationMinutes(slip) >= 240 ? "is-long" : ""}" type="button" style="--start:${start.toFixed(2)}%" data-timeline-slip="${slip.id}" title="${escapeHtml(slip.guest)} ${durationText(slip)}">${slip.time} ${escapeHtml(slip.guest)}・${slip.guests}名</button>`;
                }).join("")}
              </div>
              <div class="timeline-side"><strong>${Math.floor(longest / 60)}時間${String(longest % 60).padStart(2, "0")}分</strong><button type="button" data-focus-table="${table}">詳細</button></div>
            </div>
          `;
        }).join("")}
      `;
    }).join("");

    const content = source.length === 0
      ? '<div class="empty-search">条件に一致する伝票がありません。</div>'
      : `
        <div class="timeline-callout"><strong>橙色</strong><span>滞在4時間以上。帯の開始位置が入店時刻、長さが現在までの滞在を示します。</span></div>
        <section class="timeline-shell">
          <div class="timeline-head">
            <strong>卓</strong>
            <div class="timeline-scale">${scale.map((label) => `<span>${label}</span>`).join("")}</div>
            <strong>最長滞在</strong>
          </div>
          ${rows}
        </section>
      `;
    app.innerHTML = shell(content, "C：20件を滞在時間で捉える案。長時間滞在と同卓複数組が、卓ごとの時間軸ですぐ判別できます。");
    bindCommonEvents();
  }

  function renderNightSlipCard(slip) {
    const state = nightState(slip);
    const multiple = groupsFor(slip.table).length > 1;
    const castLabel = slip.casts.length ? escapeHtml(slip.casts.join("・")) : "フリー";
    return `
      <article class="night-slip-card is-${state.key}">
        <header class="night-slip-card__head">
          <div class="night-slip-card__identity">
            <strong class="night-slip-card__table">${slip.table}</strong>
            <span class="night-slip-card__number">伝票 #${slip.id}</span>
            ${multiple ? `<span class="night-slip-card__group">${groupIndex(slip)}組目</span>` : ""}
          </div>
          <span class="night-state-badge">${state.label}</span>
        </header>
        <div class="night-slip-card__timer">${nightTimer(slip)}</div>
        <div class="night-slip-card__body">
          <div class="night-slip-card__guest">
            <span>${slip.time} 入店・${slip.guests}名</span>
            <strong title="${escapeHtml(slip.guest)}">${escapeHtml(slip.guest)}</strong>
          </div>
          <dl class="night-slip-card__facts">
            <div><dt>指名</dt><dd>${castLabel}</dd></div>
            <div><dt>カラオケ</dt><dd>${slip.karaoke} 回</dd></div>
          </dl>
          <strong class="night-slip-card__amount">${yen(slip.amount)}</strong>
        </div>
        <footer class="night-slip-card__actions">
          <button type="button"><span aria-hidden="true">＋</span>注文</button>
          <button type="button"><span aria-hidden="true">♥</span>指名</button>
          <button type="button"><span aria-hidden="true">↻</span>延長</button>
          <button class="is-primary" type="button"><span aria-hidden="true">¥</span>会計</button>
        </footer>
      </article>
    `;
  }

  function renderCheckedOutCard(slip) {
    const castLabel = slip.casts.length ? escapeHtml(slip.casts.join("・")) : "指名なし";
    return `
      <article class="night-checked-card">
        <strong class="night-checked-card__table">${slip.table}</strong>
        <span class="night-checked-card__number">伝票 #${slip.id}</span>
        <span class="night-checked-card__guest">${escapeHtml(slip.guest)}・${slip.guests}名</span>
        <span class="night-checked-card__meta">${slip.time}入店 ／ ${slip.checkedOutAt}会計 ／ ${castLabel}</span>
        <strong class="night-checked-card__amount">${yen(slip.amount)}</strong>
        <button type="button">内容を見る</button>
      </article>
    `;
  }

  function renderVariantD() {
    const source = nightFilteredSlips();
    const categories = [...new Set(source.map((slip) => tableCategory(slip.table)))];
    const openTotal = slips.reduce((sum, slip) => sum + slip.amount, 0);
    const paidTotal = checkedOutSlips.reduce((sum, slip) => sum + slip.amount, 0);
    const guestTotal = slips.reduce((sum, slip) => sum + slip.guests, 0);
    const extensionCount = slips.filter((slip) => nightState(slip).key === "extension").length;
    const checkoutCount = slips.filter((slip) => nightState(slip).key === "checkout").length;

    const categoryPanels = categories.map((category) => {
      const categorySlips = source.filter((slip) => tableCategory(slip.table) === category);
      return `
        <section class="night-category" aria-labelledby="night-category-${category}">
          <div class="night-category__divider">
            <span></span>
            <strong id="night-category-${category}">${category}カテゴリ</strong>
            <small>${categorySlips.length}伝票</small>
            <span></span>
          </div>
          <div class="night-slip-grid">
            ${categorySlips.map(renderNightSlipCard).join("")}
          </div>
        </section>
      `;
    }).join("");

    app.innerHTML = `
      <div class="prototype-note">比較専用・保存されません</div>
      <section class="night-panel">
        <header class="night-panel__top">
          <div class="night-panel__title">
            <span>BUSINESS FLOOR</span>
            <h1>営業中</h1>
            <p>伝票単位で、接客・延長・会計をまとめて操作</p>
          </div>
          <div class="night-panel__summary" aria-label="営業状況">
            <div class="is-sales"><span>現在の売上</span><strong>${yen(openTotal + paidTotal)}</strong></div>
            <div><span>未会計伝票</span><strong>${slips.length}<small>件</small></strong></div>
            <div><span>ご来店</span><strong>${guestTotal}<small>名</small></strong></div>
            <div><span>出勤キャスト</span><strong>7<small>名</small></strong></div>
            <div><span>待機</span><strong>3<small>名</small></strong></div>
          </div>
          <button class="night-panel__add" type="button"><span>＋</span>新しい伝票</button>
        </header>

        <div class="night-panel__toolbar">
          <nav class="night-filter" aria-label="伝票の絞り込み">
            <button class="${nightFilter === "all" ? "is-active" : ""}" type="button" data-night-filter="all">
              すべて <span>${slips.length}</span>
            </button>
            <button class="${nightFilter === "extension" ? "is-active" : ""}" type="button" data-night-filter="extension">
              延長確認 <span>${extensionCount}</span>
            </button>
            <button class="${nightFilter === "checkout" ? "is-active" : ""}" type="button" data-night-filter="checkout">
              会計準備 <span>${checkoutCount}</span>
            </button>
          </nav>
          <div class="night-panel__legend">
            <span class="is-active">接客中</span>
            <span class="is-extension">延長確認</span>
            <span class="is-checkout">会計準備中</span>
          </div>
        </div>

        <div class="night-panel__content">
          ${source.length
            ? categoryPanels
            : '<div class="night-panel__empty">該当する伝票はありません。</div>'}
        </div>

        <details class="night-checked">
          <summary>
            <span class="night-checked__chevron" aria-hidden="true">⌄</span>
            <strong>会計済み</strong>
            <span>${checkedOutSlips.length}件</span>
            <small>${yen(paidTotal)}</small>
          </summary>
          <div class="night-checked__body">
            ${checkedOutSlips
              .slice()
              .sort(compareSlipsByTable)
              .map(renderCheckedOutCard)
              .join("")}
          </div>
        </details>
      </section>
    `;

    app.querySelectorAll("[data-night-filter]").forEach((button) => {
      button.addEventListener("click", () => {
        nightFilter = button.dataset.nightFilter;
        renderVariantD();
      });
    });
  }

  function bindCommonEvents() {
    const input = app.querySelector("[data-prototype-search]");
    input?.addEventListener("input", (event) => {
      searchText = event.target.value;
      render();
      requestAnimationFrame(() => {
        const next = app.querySelector("[data-prototype-search]");
        next?.focus();
        next?.setSelectionRange(searchText.length, searchText.length);
      });
    });

    app.querySelectorAll("[data-focus-table]").forEach((button) => {
      button.addEventListener("click", () => {
        const table = button.dataset.focusTable;
        if (currentVariant === "B") {
          selectedTable = table;
          render();
          return;
        }
        const target = currentVariant === "A"
          ? app.querySelector(`[id^="table-${table}-"]`)
          : app.querySelector(`[data-timeline-table="${table}"]`);
        target?.scrollIntoView({ behavior: "smooth", block: "center" });
      });
    });
  }

  function render() {
    const variant = variants.find((item) => item.key === currentVariant);
    switcherLabel.textContent = `${variant.key} — ${variant.name}`;
    document.title = `PROTOTYPE ${variant.key} — ${variant.name}`;
    if (currentVariant === "B" && !selectedTable) selectedTable = "A1";
    if (currentVariant === "A") renderVariantA();
    if (currentVariant === "B") renderVariantB();
    if (currentVariant === "C") renderVariantC();
    if (currentVariant === "D") renderVariantD();
  }

  function setVariant(key) {
    currentVariant = key;
    const url = new URL(window.location.href);
    url.searchParams.set("variant", key);
    window.history.replaceState({}, "", url);
    window.scrollTo({ top: 0, behavior: "instant" });
    render();
  }

  function cycle(direction) {
    const index = variants.findIndex((variant) => variant.key === currentVariant);
    const next = (index + direction + variants.length) % variants.length;
    setVariant(variants[next].key);
  }

  document.querySelector("[data-variant-previous]").addEventListener("click", () => cycle(-1));
  document.querySelector("[data-variant-next]").addEventListener("click", () => cycle(1));
  document.addEventListener("keydown", (event) => {
    const target = event.target;
    if (target instanceof HTMLElement && (target.matches("input, textarea") || target.isContentEditable)) return;
    if (event.key === "ArrowLeft") cycle(-1);
    if (event.key === "ArrowRight") cycle(1);
  });

  render();
})();
