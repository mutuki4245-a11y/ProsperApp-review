(() => {
    const config = window.ProsperManagementEditorConfig ?? {};
    const managementMaster = window.ProsperManagementMaster;
    const root = document.querySelector('[data-management-editor]');
    const status = document.querySelector('[data-management-status]');
    const refreshButton = document.querySelector('[data-management-refresh]');
    const payloadKeys = Object.freeze({
        tables: 'tables',
        casts: 'casts',
        staffs: 'staffs',
        items: 'itemCatalog',
        nominations: 'nominationBacks',
        pricing: 'pricingPlan'
    });

    if (!root || !status || !refreshButton || !managementMaster || !payloadKeys[config.area]) {
        if (status) {
            status.className = 'alert alert-danger py-2 mb-0';
            status.textContent = '管理画面を初期化できませんでした。';
        }
        return;
    }

    const escapeHtml = window.ProsperHtml.escape;

    const asArray = (value) => Array.isArray(value) ? value : [];
    const asNumber = (value, fallback = 0) => {
        const parsed = Number(value);
        return Number.isFinite(parsed) ? parsed : fallback;
    };
    const asInteger = (value, fallback = 0) => Math.trunc(asNumber(value, fallback));
    const checked = (value) => value === true ? ' checked' : '';
    const disabled = (value) => value ? ' disabled' : '';
    const idText = (value) => /^\d+$/.test(String(value ?? '')) ? String(value) : '';
    const payloadId = (value) => {
        const normalized = idText(value);
        const numeric = Number(normalized);
        return Number.isSafeInteger(numeric) ? numeric : normalized;
    };
    const yen = new Intl.NumberFormat('ja-JP', {
        style: 'currency',
        currency: 'JPY',
        maximumFractionDigits: 0
    });

    let refreshing = false;
    let mutating = false;
    let coldRefreshStarted = false;

    const setStatus = (kind, message) => {
        status.textContent = message;
        status.className = kind === 'error'
            ? 'alert alert-danger py-2 mb-0'
            : kind === 'success'
                ? 'alert alert-success py-2 mb-0'
                : kind === 'loading'
                    ? 'text-muted small'
                    : 'text-muted small';
    };

    const currentAreaPayload = (state) => state?.payload?.[payloadKeys[config.area]];
    const hasAreaPayload = (state) => Boolean(
        state?.payload &&
        Object.prototype.hasOwnProperty.call(state.payload, payloadKeys[config.area]));

    const updatedMessage = (state) => {
        const updatedAt = state?.updatedAt ? new Date(state.updatedAt) : null;
        return updatedAt && !Number.isNaN(updatedAt.getTime())
            ? `同期済み: ${updatedAt.toLocaleString('ja-JP')}`
            : '同期済み';
    };

    const panelTitle = (title, detail = '') => `
        <div class="slip-create__panel-title">
            <h2>${title}</h2>
            ${detail}
        </div>`;

    const renderTables = (payload) => {
        const rows = asArray(payload).slice().sort((left, right) =>
            asInteger(left.table_category_no) - asInteger(right.table_category_no) ||
            asInteger(left.sort_order) - asInteger(right.sort_order) ||
            String(left.table_code ?? '').localeCompare(String(right.table_code ?? ''), 'ja'));
        const rowHtml = rows.length === 0
            ? '<p class="text-muted mb-0">卓番が未登録です。</p>'
            : `<div class="item-admin__table">${rows.map((table) => {
                const tableId = idText(table.table_id);
                return `
                    <form class="item-admin__row item-admin__row--category" data-management-action="save">
                        <input type="hidden" name="table_id" value="${tableId}">
                        <div class="item-admin__field">
                            <label>卓番コード</label>
                            <input class="form-control" name="table_code" value="${escapeHtml(table.table_code)}" maxlength="40" required>
                        </div>
                        <div class="item-admin__field">
                            <label>表示名</label>
                            <input class="form-control" name="table_name" value="${escapeHtml(table.table_name)}" maxlength="100">
                        </div>
                        <div class="item-admin__field">
                            <label>区分</label>
                            <input class="form-control" name="table_category_no" value="${asInteger(table.table_category_no)}" type="number" min="0" max="9" step="1" required>
                        </div>
                        <div class="item-admin__field">
                            <label>表示順</label>
                            <input class="form-control" name="sort_order" value="${asInteger(table.sort_order)}" type="number" min="0" max="9999" step="1" required>
                        </div>
                        <label class="form-check item-admin__check">
                            <input class="form-check-input" name="is_active" type="checkbox"${checked(table.is_active)}>
                            <span class="form-check-label">有効</span>
                        </label>
                        <button class="btn btn-outline-primary" type="submit">保存</button>
                        <button class="btn btn-outline-danger" type="button" data-delete-action="delete" data-record-id="${tableId}" data-confirm="この卓番を削除しますか？既存伝票は保存済みの表示を使用します。">削除</button>
                    </form>`;
            }).join('')}</div>`;

        return `
            <div class="item-admin">
                <section class="slip-create__panel">
                    ${panelTitle('卓番を追加')}
                    <form class="item-admin__form item-admin__form--category" data-management-action="save">
                        <div>
                            <label class="form-label">卓番コード</label>
                            <input class="form-control" name="table_code" maxlength="40" placeholder="例: A1" required>
                        </div>
                        <div>
                            <label class="form-label">表示名</label>
                            <input class="form-control" name="table_name" maxlength="100" placeholder="例: VIP席">
                        </div>
                        <div>
                            <label class="form-label">区分</label>
                            <input class="form-control" name="table_category_no" type="number" min="0" max="9" step="1" value="0" required>
                        </div>
                        <div>
                            <label class="form-label">表示順</label>
                            <input class="form-control" name="sort_order" type="number" min="0" max="9999" step="1" value="0" required>
                        </div>
                        <label class="form-check item-admin__check">
                            <input class="form-check-input" name="is_active" type="checkbox" checked>
                            <span class="form-check-label">有効</span>
                        </label>
                        <button class="btn btn-primary" type="submit">追加</button>
                    </form>
                    <p class="form-text mb-0">区分は0～9で、伝票作成画面の卓番グループ分けに使います。</p>
                </section>
                <section class="slip-create__panel">
                    ${panelTitle('登録済み卓番', `<span class="item-admin__count">${rows.length} 件</span>`)}
                    ${rowHtml}
                </section>
            </div>`;
    };

    const renderCasts = (payload) => {
        const rows = asArray(payload).slice().sort((left, right) =>
            String(left.display_name ?? '').localeCompare(String(right.display_name ?? ''), 'ja'));
        const tableHtml = rows.length === 0
            ? '<p class="text-muted mb-0">キャストが未登録です。</p>'
            : `<div class="cast-admin-table">
                <div class="cast-admin-table__row cast-admin-table__row--header">
                    <span>ID</span><span>キャスト名</span><span>加入日</span><span>ドリンクメモ</span><span>操作</span>
                </div>
                ${rows.map((cast) => {
                    const castId = idText(cast.cast_id);
                    return `
                        <form class="cast-admin-table__row" data-management-action="update_drink_memo">
                            <input type="hidden" name="cast_id" value="${castId}">
                            <span>${castId}</span>
                            <strong>${escapeHtml(cast.display_name)}</strong>
                            <span>${escapeHtml(cast.joined_on)}</span>
                            <input class="form-control form-control-sm" name="drink_memo" value="${escapeHtml(cast.drink_memo)}" maxlength="30" placeholder="未設定">
                            <div class="cast-admin-table__actions">
                                <button class="btn btn-outline-primary btn-sm" type="submit">保存</button>
                                <button class="btn btn-outline-danger btn-sm" type="button" data-delete-action="delete" data-record-id="${castId}" data-confirm="このキャストを削除しますか？">削除</button>
                            </div>
                        </form>`;
                }).join('')}
            </div>`;

        return `
            <form class="slip-create__panel cast-admin-form" data-management-action="create">
                <div>
                    <label class="form-label">キャスト名</label>
                    <input class="form-control form-control-lg" name="display_name" maxlength="120" placeholder="キャスト名" required>
                </div>
                <div>
                    <label class="form-label">ドリンクメモ</label>
                    <input class="form-control" name="drink_memo" maxlength="30" placeholder="ドリンクを出す際のメモ（任意）">
                </div>
                <button class="btn btn-primary btn-lg" type="submit">登録</button>
            </form>
            <section class="slip-create__panel">
                <div class="opening-attendance__header">
                    <div><h2>登録済みキャスト</h2><p class="text-muted mb-0">加入日は登録日で自動設定されます。</p></div>
                    <span class="opening-attendance__count">${rows.length} 名</span>
                </div>
                ${tableHtml}
            </section>`;
    };

    const renderStaffs = (payload) => {
        const rows = asArray(payload).slice().sort((left, right) =>
            String(left.display_name ?? '').localeCompare(String(right.display_name ?? ''), 'ja'));
        const tableHtml = rows.length === 0
            ? '<p class="text-muted mb-0">スタッフが未登録です。</p>'
            : `<div class="cast-admin-table staff-admin-table">
                <div class="cast-admin-table__row cast-admin-table__row--header staff-admin-table__row">
                    <span>ID</span><span>スタッフ名</span><span>加入日</span><span>雇用区分</span><span>操作</span>
                </div>
                ${rows.map((staff) => {
                    const staffId = idText(staff.staff_id);
                    return `
                        <form class="cast-admin-table__row staff-admin-table__row" data-management-action="update_employment_type">
                            <input type="hidden" name="staff_id" value="${staffId}">
                            <span>${staffId}</span>
                            <strong>${escapeHtml(staff.display_name)}</strong>
                            <span>${escapeHtml(staff.joined_on)}</span>
                            <select class="form-select form-select-sm" name="employment_type" aria-label="${escapeHtml(staff.display_name)} の雇用区分">
                                <option value="employee"${staff.employment_type === 'employee' ? ' selected' : ''}>社員</option>
                                <option value="part_time"${staff.employment_type === 'part_time' ? ' selected' : ''}>バイト</option>
                            </select>
                            <div class="cast-admin-table__actions staff-admin-table__actions">
                                <button class="btn btn-outline-primary btn-sm" type="submit">変更</button>
                                <button class="btn btn-outline-danger btn-sm" type="button" data-delete-action="delete" data-record-id="${staffId}" data-confirm="このスタッフを削除しますか？">削除</button>
                            </div>
                        </form>`;
                }).join('')}
            </div>`;

        return `
            <form class="slip-create__panel cast-admin-form" data-management-action="create">
                <div>
                    <label class="form-label">スタッフ名</label>
                    <input class="form-control form-control-lg" name="display_name" maxlength="120" placeholder="スタッフ名" required>
                </div>
                <div>
                    <label class="form-label">雇用区分</label>
                    <select class="form-select form-select-lg" name="employment_type">
                        <option value="employee">社員</option><option value="part_time">バイト</option>
                    </select>
                </div>
                <button class="btn btn-primary btn-lg" type="submit">登録</button>
            </form>
            <section class="slip-create__panel">
                <div class="opening-attendance__header">
                    <div><h2>登録済みスタッフ</h2><p class="text-muted mb-0">加入日は登録日で自動設定されます。</p></div>
                    <span class="opening-attendance__count">${rows.length} 名</span>
                </div>
                ${tableHtml}
            </section>`;
    };

    const normalizeItemCatalog = (payload) => {
        if (Array.isArray(payload)) {
            return {
                categories: payload.filter((row) => row?.row_type === 'category'),
                items: payload.filter((row) => row?.row_type === 'item')
            };
        }

        return {
            categories: asArray(payload?.categories),
            items: asArray(payload?.items)
        };
    };

    const categoryOptions = (categories, selectedId, activeOnly = false) => categories
        .filter((category) => !activeOnly || category.is_active === true)
        .map((category) => {
            const categoryId = idText(category.item_category_id);
            return `<option value="${categoryId}"${categoryId === idText(selectedId) ? ' selected' : ''}>${escapeHtml(category.category_name)}</option>`;
        })
        .join('');

    const renderCategoryEditor = (categories, items) => {
        const adminDisabled = config.allowAdminActions !== true;
        const nextSortOrder = categories.length === 0
            ? 10
            : Math.max(...categories.map((category) => asInteger(category.sort_order))) + 10;
        const warning = adminDisabled
            ? '<div class="alert alert-warning py-2">カテゴリの追加・編集・削除には管理者モードが必要です。</div>'
            : '';
        const rows = categories.length === 0
            ? '<p class="text-muted mb-0">カテゴリが未登録です。</p>'
            : `<div class="item-admin__table item-admin__table--category">${categories.map((category) => {
                const categoryId = idText(category.item_category_id);
                const itemCount = items.filter((item) => idText(item.item_category_id) === categoryId).length;
                const deleteControl = itemCount === 0
                    ? `<button class="btn btn-outline-danger" type="button" data-delete-action="delete_category" data-record-id="${categoryId}" data-confirm="このカテゴリを削除しますか？"${disabled(adminDisabled)}>削除</button>`
                    : `<button class="btn btn-outline-secondary" type="button" disabled title="配下の商品を先に削除してください">商品 ${itemCount} 件</button>`;
                return `
                    <form class="item-admin__row item-admin__row--category" data-management-action="save_category">
                        <input type="hidden" name="item_category_id" value="${categoryId}">
                        <div class="item-admin__field"><label>コード</label><input class="form-control" name="category_code" value="${escapeHtml(category.category_code)}" maxlength="40" required></div>
                        <div class="item-admin__field"><label>カテゴリ名</label><input class="form-control" name="category_name" value="${escapeHtml(category.category_name)}" maxlength="100" required></div>
                        <div class="item-admin__field"><label>表示順</label><input class="form-control" name="sort_order" value="${asInteger(category.sort_order)}" type="number" min="0" max="9999" step="1" required></div>
                        <label class="form-check item-admin__check"><input class="form-check-input" name="is_active" type="checkbox"${checked(category.is_active)}><span class="form-check-label">有効</span></label>
                        <button class="btn btn-outline-primary" type="submit"${disabled(adminDisabled)}>保存</button>
                        ${deleteControl}
                    </form>`;
            }).join('')}</div>`;

        return `
            <section class="slip-create__panel">
                ${panelTitle('カテゴリ')}
                ${warning}
                <fieldset class="border-0 p-0 m-0"${disabled(adminDisabled)}>
                    <form class="item-admin__form item-admin__form--category" data-management-action="save_category">
                        <div><label class="form-label">カテゴリコード</label><input class="form-control" name="category_code" maxlength="40" placeholder="例: drink" required></div>
                        <div><label class="form-label">カテゴリ名</label><input class="form-control" name="category_name" maxlength="100" placeholder="例: ドリンク" required></div>
                        <div><label class="form-label">表示順</label><input class="form-control" name="sort_order" value="${nextSortOrder}" type="number" min="0" max="9999" step="1" required></div>
                        <label class="form-check item-admin__check"><input class="form-check-input" name="is_active" type="checkbox" checked><span class="form-check-label">有効</span></label>
                        <button class="btn btn-primary" type="submit">カテゴリ追加</button>
                    </form>
                    ${rows}
                </fieldset>
            </section>`;
    };

    const renderItemForm = (item, categories, orderable) => {
        const itemId = idText(item.item_id);
        const isSystem = String(item.item_type ?? 'standard') !== 'standard';
        const orderControls = orderable
            ? `<div class="item-admin__reorder-controls" aria-label="${escapeHtml(item.item_name)} の並び替え">
                    <button class="btn btn-outline-secondary btn-sm" type="button" data-item-move="up">上</button>
                    <button class="btn btn-outline-secondary btn-sm" type="button" data-item-move="down">下</button>
               </div>`
            : '';
        if (isSystem) {
            return `
                <div class="item-admin__row item-admin__row--item"${orderable ? ` data-item-order-row data-item-id="${itemId}"` : ''}>
                    <div class="item-admin__field item-admin__field--name"><label>商品名</label><strong>${escapeHtml(item.item_name)}</strong><span class="badge text-bg-secondary">システム商品</span></div>
                    <div class="item-admin__field"><label>価格</label><span>${escapeHtml(yen.format(asNumber(item.default_price)))}</span></div>
                    <div class="item-admin__field"><label>カテゴリ</label><span>${escapeHtml(item.category_name || '未分類')}</span></div>
                    <div class="item-admin__field"><label>状態</label><span>${item.is_active ? '有効' : '無効'}</span></div>
                    <button class="btn btn-outline-secondary" type="button" disabled>編集不可</button>
                    ${orderControls}
                </div>`;
        }

        return `
            <form class="item-admin__row item-admin__row--item" data-management-action="save_item"${orderable ? ` data-item-order-row data-item-id="${itemId}"` : ''}>
                <input type="hidden" name="item_id" value="${itemId}">
                <input type="hidden" name="cast_back_type" value="${escapeHtml(item.cast_back_type || 'drink')}">
                <div class="item-admin__field item-admin__field--name"><label>商品名</label><input class="form-control" name="item_name" value="${escapeHtml(item.item_name)}" maxlength="120" required></div>
                <div class="item-admin__field"><label>カテゴリ</label><select class="form-select" name="item_category_id" required>${categoryOptions(categories, item.item_category_id)}</select></div>
                <div class="item-admin__field"><label>税込価格</label><input class="form-control" name="default_price" value="${asNumber(item.default_price)}" type="number" min="0" max="9999999" step="1" required></div>
                <label class="item-admin__field item-admin__field--check"><span>有効</span><input class="form-check-input" name="is_active" type="checkbox"${checked(item.is_active)}></label>
                <label class="item-admin__field item-admin__field--check"><span>バック対象</span><input class="form-check-input" name="is_cast_back_target" type="checkbox"${checked(item.is_cast_back_target)}></label>
                <div class="item-admin__field"><label>ドリンクバック</label><input class="form-control" name="cast_back_regular_unit_amount" value="${asNumber(item.cast_back_regular_unit_amount)}" type="number" min="0" max="9999999" step="1" required></div>
                <div class="item-admin__field"><label>担当バック</label><input class="form-control" name="cast_back_nomination_unit_amount" value="${asNumber(item.cast_back_nomination_unit_amount)}" type="number" min="0" max="9999999" step="1" required></div>
                <button class="btn btn-outline-primary" type="submit">保存</button>
                <button class="btn btn-outline-danger" type="button" data-delete-action="delete_item" data-record-id="${itemId}" data-confirm="この商品を削除しますか？">削除</button>
                ${orderControls}
            </form>`;
    };

    const renderItemEditor = (categories, items) => {
        const activeCategories = categories.filter((category) => category.is_active === true);
        const activeItems = items.filter((item) => item.is_active === true);
        const inactiveItems = items.filter((item) => item.is_active !== true);
        const activeCategoryIds = new Set(activeCategories.map((category) => idText(category.item_category_id)));
        const groups = activeCategories.map((category) => ({
            id: idText(category.item_category_id),
            name: category.category_name,
            items: activeItems.filter((item) => idText(item.item_category_id) === idText(category.item_category_id))
        }));
        const uncategorized = activeItems.filter((item) => !activeCategoryIds.has(idText(item.item_category_id)));
        if (uncategorized.length > 0) {
            groups.push({ id: '', name: '未分類', items: uncategorized });
        }

        const newItem = activeCategories.length === 0
            ? '<div class="alert alert-warning mb-3">商品を追加する前に有効なカテゴリを登録してください。</div>'
            : `
                <form class="item-admin__row item-admin__row--add mb-3" data-management-action="save_item">
                    <div class="item-admin__field item-admin__field--name"><label>商品名</label><input class="form-control" name="item_name" maxlength="120" placeholder="商品名" required></div>
                    <div class="item-admin__field"><label>カテゴリ</label><select class="form-select" name="item_category_id" required>${categoryOptions(categories, null, true)}</select></div>
                    <div class="item-admin__field"><label>税込価格</label><input class="form-control" name="default_price" type="number" min="0" max="9999999" step="1" value="0" required></div>
                    <input type="hidden" name="cast_back_type" value="drink">
                    <input type="hidden" name="is_active" value="true">
                    <label class="item-admin__field item-admin__field--check"><span>バック対象</span><input class="form-check-input" name="is_cast_back_target" type="checkbox"></label>
                    <div class="item-admin__field"><label>ドリンクバック</label><input class="form-control" name="cast_back_regular_unit_amount" type="number" min="0" max="9999999" step="1" value="0" required></div>
                    <div class="item-admin__field"><label>担当バック</label><input class="form-control" name="cast_back_nomination_unit_amount" type="number" min="0" max="9999999" step="1" value="0" required></div>
                    <button class="btn btn-primary" type="submit">追加</button>
                </form>`;
        const groupsHtml = activeItems.length === 0
            ? '<p class="text-muted mb-3">商品が未登録です。</p>'
            : `<div class="item-admin__groups">${groups.map((group, index) => `
                <details class="item-admin__group"${index === 0 ? ' open' : ''}>
                    <summary><span>${escapeHtml(group.name)}</span><span>${group.items.length} 件</span></summary>
                    <div class="item-admin__table item-admin__table--item" data-item-group-body>
                        ${group.items.map((item) => renderItemForm(item, categories, true)).join('')}
                    </div>
                </details>`).join('')}</div>`;
        const inactiveHtml = inactiveItems.length === 0
            ? ''
            : `<details class="item-admin__group mt-3">
                <summary><span>無効な商品</span><span>${inactiveItems.length} 件</span></summary>
                <div class="item-admin__table item-admin__table--item">
                    ${inactiveItems.map((item) => renderItemForm(item, categories, false)).join('')}
                </div>
            </details>`;
        const reorderActions = activeItems.length > 1
            ? `<div class="item-admin__toolbar">
                <span class="item-admin__count">${activeItems.length} 件</span>
                <button class="btn btn-outline-primary" type="button" data-item-reorder-start>並べ替え</button>
                <div class="item-admin__reorder-actions" data-item-reorder-actions hidden>
                    <button class="btn btn-primary" type="button" data-item-reorder-save>並び順を保存</button>
                    <button class="btn btn-outline-secondary" type="button" data-item-reorder-cancel>キャンセル</button>
                </div>
               </div>`
            : `<span class="item-admin__count">${activeItems.length} 件</span>`;

        return `
            <section class="slip-create__panel">
                ${panelTitle('登録済み商品', reorderActions)}
                ${newItem}
                ${groupsHtml}
                ${inactiveHtml}
            </section>`;
    };

    const renderItems = (payload) => {
        const catalog = normalizeItemCatalog(payload);
        const categories = catalog.categories.slice().sort((left, right) =>
            asInteger(left.sort_order) - asInteger(right.sort_order) ||
            String(left.category_name ?? '').localeCompare(String(right.category_name ?? ''), 'ja'));
        const items = catalog.items.slice().sort((left, right) =>
            asInteger(left.sort_order) - asInteger(right.sort_order) ||
            String(left.item_name ?? '').localeCompare(String(right.item_name ?? ''), 'ja'));
        return `<div class="item-admin" data-item-admin>${renderCategoryEditor(categories, items)}${renderItemEditor(categories, items)}</div>`;
    };

    const renderNominations = (payload) => {
        const rows = asArray(payload).slice().sort((left, right) =>
            asInteger(left.sort_order) - asInteger(right.sort_order));
        const content = rows.length === 0
            ? '<p class="text-muted mb-0">指名バック設定が未登録です。</p>'
            : `<form data-management-action="save">
                <div class="item-admin__table">
                    ${rows.map((row) => `
                        <div class="item-admin__row item-admin__row--nomination-back" data-nomination-row>
                            <input type="hidden" name="nomination_kind" value="${escapeHtml(row.nomination_kind)}">
                            <input type="hidden" name="nomination_type" value="${escapeHtml(row.nomination_type)}">
                            <input type="hidden" name="display_name" value="${escapeHtml(row.display_name)}">
                            <input type="hidden" name="companion_time" value="${escapeHtml(row.companion_time)}">
                            <input type="hidden" name="sort_order" value="${asInteger(row.sort_order)}">
                            <div class="item-admin__field item-admin__field--name"><label>指名種別</label><strong>${escapeHtml(row.display_name)}</strong></div>
                            <div class="item-admin__field"><label>バック単価</label><input class="form-control" name="back_unit_amount" value="${asNumber(row.back_unit_amount)}" type="number" min="0" max="9999999" step="1" required></div>
                            <label class="item-admin__field item-admin__field--check"><span>有効</span><input class="form-check-input" name="is_active" type="checkbox"${checked(row.is_active)}></label>
                        </div>`).join('')}
                </div>
                <div class="mt-3"><button class="btn btn-primary btn-lg" type="submit">保存</button></div>
              </form>`;
        return `<section class="slip-create__panel">${panelTitle('店舗別バック単価')}${content}</section>`;
    };

    const renderPricing = (payload) => {
        const plan = payload && typeof payload === 'object' && !Array.isArray(payload) ? payload : {};
        return `
            <form class="slip-create__panel" data-management-action="save">
                <div class="item-admin__table">
                    <div class="item-admin__row item-admin__row--nomination-back">
                        <div class="item-admin__field"><label>セット時間</label><div class="input-group"><input class="form-control" name="set_minutes" value="${asInteger(plan.set_minutes, 60)}" type="number" min="5" max="480" step="5" required><span class="input-group-text">分</span></div></div>
                        <div class="item-admin__field"><label>延長時間</label><div class="input-group"><input class="form-control" name="extension_minutes" value="${asInteger(plan.extension_minutes, 30)}" type="number" min="5" max="480" step="5" required><span class="input-group-text">分</span></div></div>
                        <label class="item-admin__field item-admin__field--check"><span>有効</span><input class="form-check-input" name="is_active" type="checkbox"${checked(plan.is_active)}></label>
                    </div>
                    <div class="item-admin__row item-admin__row--nomination-back">
                        <div class="item-admin__field"><label>1人時セット料金</label><div class="input-group"><input class="form-control" name="set_unit_price_single" value="${asNumber(plan.set_unit_price_single)}" type="number" min="0" max="99999999" step="1" required><span class="input-group-text">円</span></div></div>
                        <div class="item-admin__field"><label>複数人時セット料金（1人あたり）</label><div class="input-group"><input class="form-control" name="set_unit_price_per_customer" value="${asNumber(plan.set_unit_price_per_customer)}" type="number" min="0" max="99999999" step="1" required><span class="input-group-text">円</span></div></div>
                    </div>
                    <div class="item-admin__row item-admin__row--nomination-back">
                        <div class="item-admin__field"><label>1人時延長料金</label><div class="input-group"><input class="form-control" name="extension_unit_price_single" value="${asNumber(plan.extension_unit_price_single)}" type="number" min="0" max="99999999" step="1" required><span class="input-group-text">円</span></div></div>
                        <div class="item-admin__field"><label>複数人時延長料金（1人あたり）</label><div class="input-group"><input class="form-control" name="extension_unit_price_per_customer" value="${asNumber(plan.extension_unit_price_per_customer)}" type="number" min="0" max="99999999" step="1" required><span class="input-group-text">円</span></div></div>
                    </div>
                </div>
                <p class="text-muted mt-3 mb-0">入店時の人数でセット料金を1回計算します。各延長は、その時点で在席している人数で計算します。会計伝票を出力した後は、その時点の料金内容を固定します。</p>
                <div class="mt-3"><button class="btn btn-primary btn-lg" type="submit">保存</button></div>
            </form>`;
    };

    const renderers = Object.freeze({
        tables: renderTables,
        casts: renderCasts,
        staffs: renderStaffs,
        items: renderItems,
        nominations: renderNominations,
        pricing: renderPricing
    });

    const updateMoveButtons = () => {
        root.querySelectorAll('[data-item-group-body]').forEach((group) => {
            const rows = Array.from(group.querySelectorAll(':scope > [data-item-order-row]'));
            rows.forEach((row, index) => {
                const up = row.querySelector('[data-item-move="up"]');
                const down = row.querySelector('[data-item-move="down"]');
                if (up) up.disabled = index === 0;
                if (down) down.disabled = index === rows.length - 1;
            });
        });
    };

    const render = (state) => {
        if (!hasAreaPayload(state)) {
            root.innerHTML = '<div class="text-muted py-3">管理マスタを取得しています。</div>';
            root.setAttribute('aria-busy', 'true');
            return;
        }

        root.innerHTML = renderers[config.area](currentAreaPayload(state));
        root.setAttribute('aria-busy', 'false');
        setStatus('ready', updatedMessage(state));
    };

    const field = (container, name) =>
        container.elements?.namedItem(name) ?? container.querySelector(`[name="${name}"]`);
    const value = (form, name) => String(field(form, name)?.value ?? '').trim();
    const numberValue = (form, name) => asNumber(value(form, name));
    const integerValue = (form, name) => asInteger(value(form, name));
    const checkedValue = (form, name) => {
        const control = field(form, name);
        if (!control) return false;
        if (control.type === 'hidden') return String(control.value).toLowerCase() === 'true';
        return control.checked === true;
    };

    const optionalId = (form, name) => {
        const normalized = idText(value(form, name));
        return normalized ? payloadId(normalized) : undefined;
    };

    const buildPayload = (form, action) => {
        if (config.area === 'tables' && action === 'save') {
            return {
                ...(optionalId(form, 'table_id') ? { table_id: optionalId(form, 'table_id') } : {}),
                table_code: value(form, 'table_code'),
                table_name: value(form, 'table_name') || null,
                table_category_no: integerValue(form, 'table_category_no'),
                sort_order: integerValue(form, 'sort_order'),
                is_active: checkedValue(form, 'is_active')
            };
        }

        if (config.area === 'casts') {
            return action === 'create'
                ? { display_name: value(form, 'display_name'), drink_memo: value(form, 'drink_memo') || null }
                : { cast_id: optionalId(form, 'cast_id'), drink_memo: value(form, 'drink_memo') || null };
        }

        if (config.area === 'staffs') {
            return action === 'create'
                ? { display_name: value(form, 'display_name'), employment_type: value(form, 'employment_type') }
                : { staff_id: optionalId(form, 'staff_id'), employment_type: value(form, 'employment_type') };
        }

        if (config.area === 'items' && action === 'save_category') {
            return {
                ...(optionalId(form, 'item_category_id') ? { item_category_id: optionalId(form, 'item_category_id') } : {}),
                category_code: value(form, 'category_code'),
                category_name: value(form, 'category_name'),
                sort_order: integerValue(form, 'sort_order'),
                is_active: checkedValue(form, 'is_active')
            };
        }

        if (config.area === 'items' && action === 'save_item') {
            return {
                ...(optionalId(form, 'item_id') ? { item_id: optionalId(form, 'item_id') } : {}),
                item_category_id: optionalId(form, 'item_category_id'),
                item_name: value(form, 'item_name'),
                default_price: numberValue(form, 'default_price'),
                is_active: checkedValue(form, 'is_active'),
                is_cast_back_target: checkedValue(form, 'is_cast_back_target'),
                cast_back_regular_unit_amount: numberValue(form, 'cast_back_regular_unit_amount'),
                cast_back_nomination_unit_amount: numberValue(form, 'cast_back_nomination_unit_amount'),
                cast_back_type: value(form, 'cast_back_type') || 'drink'
            };
        }

        if (config.area === 'nominations' && action === 'save') {
            return {
                settings: Array.from(form.querySelectorAll('[data-nomination-row]')).map((row) => ({
                    nomination_kind: value(row, 'nomination_kind'),
                    nomination_type: value(row, 'nomination_type'),
                    display_name: value(row, 'display_name'),
                    companion_time: value(row, 'companion_time') || null,
                    back_unit_amount: numberValue(row, 'back_unit_amount'),
                    sort_order: integerValue(row, 'sort_order'),
                    is_active: checkedValue(row, 'is_active')
                }))
            };
        }

        if (config.area === 'pricing' && action === 'save') {
            return {
                set_minutes: integerValue(form, 'set_minutes'),
                extension_minutes: integerValue(form, 'extension_minutes'),
                set_unit_price_single: numberValue(form, 'set_unit_price_single'),
                set_unit_price_per_customer: numberValue(form, 'set_unit_price_per_customer'),
                extension_unit_price_single: numberValue(form, 'extension_unit_price_single'),
                extension_unit_price_per_customer: numberValue(form, 'extension_unit_price_per_customer'),
                is_active: checkedValue(form, 'is_active')
            };
        }

        return {};
    };

    const confirmedMessage = (action) => {
        const messages = {
            save: '保存しました。',
            create: '登録しました。',
            update_drink_memo: 'ドリンクメモを保存しました。',
            update_employment_type: '雇用区分を変更しました。',
            save_category: 'カテゴリを保存しました。',
            save_item: '商品を保存しました。',
            reorder: '商品の並び順を保存しました。',
            delete: '削除しました。',
            delete_category: 'カテゴリを削除しました。',
            delete_item: '商品を削除しました。'
        };
        return messages[action] || '保存しました。';
    };

    const mutate = async (action, payload, source) => {
        if (mutating) return false;
        mutating = true;
        source?.setAttribute('disabled', 'disabled');
        root.setAttribute('aria-busy', 'true');
        setStatus('loading', '保存しています。');
        try {
            const response = await managementMaster.mutate(config.area, action, payload);
            if (response?.status === 'confirmed') {
                setStatus('success', confirmedMessage(action));
                return true;
            }

            const isConflict = response?.status === 'conflict';
            const isValidationError = response?.status === 'validation_error';
            const message = response?.message || (isConflict
                ? '別の端末で更新されています。入力内容を確認して再保存してください。'
                : isValidationError
                    ? '入力内容を確認してください。'
                    : '保存できませんでした。');
            setStatus('error', message);
            return false;
        } catch (error) {
            const payloadStatus = error?.payload?.status;
            const message = error?.payload?.message || error?.message || '保存できませんでした。';
            setStatus('error', payloadStatus === 'conflict'
                ? '別の端末で更新されています。入力内容を確認して再保存してください。'
                : message);
            return false;
        } finally {
            mutating = false;
            source?.removeAttribute('disabled');
            root.setAttribute('aria-busy', 'false');
        }
    };

    const refresh = async () => {
        if (refreshing) return;
        refreshing = true;
        refreshButton.disabled = true;
        setStatus('loading', '管理マスタを再取得しています。');
        try {
            const state = await managementMaster.refresh();
            render(state ?? managementMaster.store.get());
        } catch (error) {
            setStatus('error', error?.message || '管理マスタを取得できませんでした。');
        } finally {
            refreshing = false;
            refreshButton.disabled = false;
        }
    };

    root.addEventListener('submit', (event) => {
        const form = event.target.closest('form[data-management-action]');
        if (!form) return;
        event.preventDefault();
        if (!form.reportValidity()) return;
        const action = form.dataset.managementAction;
        const payload = buildPayload(form, action);
        mutate(action, payload, form.querySelector('[type="submit"]'));
    });

    root.addEventListener('click', (event) => {
        const deleteButton = event.target.closest('[data-delete-action]');
        if (deleteButton) {
            const action = deleteButton.dataset.deleteAction;
            if ((action === 'save_category' || action === 'delete_category') && config.allowAdminActions !== true) return;
            if (!window.confirm(deleteButton.dataset.confirm || '削除しますか？')) return;
            const recordId = payloadId(deleteButton.dataset.recordId);
            const key = config.area === 'tables'
                ? 'table_id'
                : config.area === 'casts'
                    ? 'cast_id'
                    : config.area === 'staffs'
                        ? 'staff_id'
                        : action === 'delete_category'
                            ? 'item_category_id'
                            : 'item_id';
            mutate(action, { [key]: recordId }, deleteButton);
            return;
        }

        const start = event.target.closest('[data-item-reorder-start]');
        if (start) {
            const admin = root.querySelector('[data-item-admin]');
            admin?.classList.add('is-reordering');
            start.hidden = true;
            const actions = root.querySelector('[data-item-reorder-actions]');
            if (actions) actions.hidden = false;
            root.querySelectorAll('.item-admin__group').forEach((group) => { group.open = true; });
            updateMoveButtons();
            return;
        }

        if (event.target.closest('[data-item-reorder-cancel]')) {
            render(managementMaster.store.get());
            return;
        }

        const move = event.target.closest('[data-item-move]');
        if (move) {
            const row = move.closest('[data-item-order-row]');
            if (!row) return;
            if (move.dataset.itemMove === 'up' && row.previousElementSibling) {
                row.parentElement.insertBefore(row, row.previousElementSibling);
            } else if (move.dataset.itemMove === 'down' && row.nextElementSibling) {
                row.parentElement.insertBefore(row.nextElementSibling, row);
            }
            updateMoveButtons();
            return;
        }

        const reorderSave = event.target.closest('[data-item-reorder-save]');
        if (reorderSave) {
            const items = [];
            root.querySelectorAll('[data-item-group-body]').forEach((group) => {
                Array.from(group.querySelectorAll(':scope > [data-item-order-row]')).forEach((row, index) => {
                    items.push({ item_id: payloadId(row.dataset.itemId), sort_order: (index + 1) * 10 });
                });
            });
            mutate('reorder', { items }, reorderSave);
        }
    });

    refreshButton.addEventListener('click', refresh);
    managementMaster.store.subscribe((state) => render(state));

    const initialState = managementMaster.store.get();
    if (hasAreaPayload(initialState)) {
        render(initialState);
    } else {
        render(initialState);
        if (!coldRefreshStarted) {
            coldRefreshStarted = true;
            refresh();
        }
    }
})();
