(() => {
    const config = window.prosperBusinessHome ?? {};
    const editorUrl = config.businessSlipEditorUrl || '';
    const modalElement = document.querySelector('[data-business-slip-editor-modal]');
    const content = modalElement?.querySelector('[data-business-slip-editor-content]');
    const title = modalElement?.querySelector('[data-business-slip-editor-title]');
    const table = modalElement?.querySelector('[data-business-slip-editor-table]');
    const labels = {
        customers: '客を編集',
        nominations: '指名を編集',
        adjustments: '自由明細を編集'
    };
    const state = {
        section: null,
        slipId: null,
        requestId: 0,
        isSubmitting: false
    };

    if (!editorUrl || !modalElement || !content || !window.bootstrap?.Modal) {
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(modalElement);
    const currentSlip = (slipId) => (window.prosperBusinessHomeSlips || []).find((slip) => String(slip.id) === String(slipId));

    const setLoading = () => {
        content.innerHTML = '<div class="slip-create__panel slip-detail-panel-loading">編集内容を読み込み中です。</div>';
    };

    const setError = (message) => {
        content.replaceChildren();
        const alert = document.createElement('div');
        alert.className = 'alert alert-warning mb-0';
        alert.setAttribute('role', 'alert');
        alert.textContent = message;
        content.appendChild(alert);
    };

    const showNetworkError = () => {
        const alert = document.createElement('div');
        alert.className = 'alert alert-warning';
        alert.setAttribute('role', 'alert');
        alert.dataset.businessSlipEditorNetworkError = '';
        alert.textContent = '保存に失敗しました。通信状態を確認してから再実行してください。';
        content.querySelector('[data-business-slip-editor-network-error]')?.remove();
        content.prepend(alert);
    };

    const buildUrl = (section, slipId) => {
        const url = new URL(editorUrl, window.location.href);
        url.searchParams.set('section', section);
        url.searchParams.set('slipId', String(slipId));
        return url.toString();
    };

    const prepareContent = () => {
        window.PartialForms?.prepareDynamicContent?.(content);
        content.querySelectorAll('.nomination-row__kind').forEach((kindSelect) => syncCompanionPrice(kindSelect));
    };

    const setModalHeading = (section, slip) => {
        if (title) {
            title.textContent = labels[section] || '編集';
        }
        if (table) {
            table.textContent = slip?.tableDisplay || '伝票';
        }
    };

    const renderUnavailable = (message) => {
        state.isSubmitting = false;
        setError(message);
    };

    const openEditor = async (section, slipId) => {
        if (!Object.hasOwn(labels, section) || !slipId) {
            return;
        }

        const requestId = ++state.requestId;
        state.section = section;
        state.slipId = String(slipId);
        state.isSubmitting = false;
        const slip = currentSlip(slipId);
        setModalHeading(section, slip);
        setLoading();
        modal.show();

        try {
            const response = await fetch(buildUrl(section, slipId), {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) {
                throw new Error('Business slip editor load failed.');
            }

            const html = await response.text();
            if (requestId !== state.requestId || !modalElement.classList.contains('show')) {
                return;
            }

            content.innerHTML = html;
            prepareContent();
        } catch {
            if (requestId === state.requestId && modalElement.classList.contains('show')) {
                renderUnavailable('編集内容を取得できませんでした。営業中一覧を再表示してから開き直してください。');
            }
        }
    };

    const syncCompanionPrice = (kindSelect, force = false) => {
        const option = kindSelect?.selectedOptions?.[0];
        if (!option || option.dataset.companion !== 'true') {
            return;
        }

        const priceSelect = kindSelect.closest('form')?.querySelector('.nomination-row__price');
        if (!priceSelect || !Array.from(priceSelect.options).some((item) => item.value === '3000')) {
            return;
        }

        if (force || priceSelect.value === '' || priceSelect.value === '1000') {
            priceSelect.value = '3000';
        }
    };

    const adjustmentHasInput = () => {
        const form = content.querySelector('[data-business-adjustment-form]');
        if (!form) {
            return false;
        }

        const name = form.querySelector('[data-business-adjustment-name]')?.value?.trim() || '';
        const amount = Number(form.querySelector('[data-business-adjustment-amount]')?.value || 0);
        return name.length > 0 || amount !== 0;
    };

    const submitEditor = async (form) => {
        if (state.isSubmitting) {
            return;
        }

        state.isSubmitting = true;
        const submitters = form.querySelectorAll('button[type="submit"], input[type="submit"]');
        submitters.forEach((submitter) => { submitter.disabled = true; });
        window.AppLoading?.show(form);
        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) {
                throw new Error('Business slip editor save failed.');
            }

            const html = await response.text();
            content.innerHTML = html;
            prepareContent();
            state.isSubmitting = false;

            void window.prosperBusinessHomeReload?.();
        } catch {
            state.isSubmitting = false;
            submitters.forEach((submitter) => { submitter.disabled = false; });
            showNetworkError();
        } finally {
            window.AppLoading?.hide(form);
        }
    };

    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-business-slip-editor]');
        if (!button) {
            return;
        }

        event.preventDefault();
        void openEditor(button.dataset.businessSlipEditor, button.dataset.businessSlipId);
    });

    document.addEventListener('change', (event) => {
        const kindSelect = event.target.closest('.nomination-row__kind');
        if (kindSelect && content.contains(kindSelect)) {
            syncCompanionPrice(kindSelect, true);
        }
    });

    document.addEventListener('submit', (event) => {
        const form = event.target.closest('[data-business-slip-editor-form]');
        if (!form || !content.contains(form) || event.defaultPrevented) {
            return;
        }

        event.preventDefault();
        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }

        void submitEditor(form);
    });

    modalElement.addEventListener('hide.bs.modal', (event) => {
        if (state.isSubmitting || state.section !== 'adjustments' || !adjustmentHasInput()) {
            return;
        }

        if (!window.confirm('自由入力明細を保存せず閉じますか？')) {
            event.preventDefault();
        }
    });

    modalElement.addEventListener('hidden.bs.modal', () => {
        state.requestId += 1;
        state.section = null;
        state.slipId = null;
        state.isSubmitting = false;
        content.innerHTML = '<div class="slip-create__panel slip-detail-panel-loading">編集内容を読み込み中です。</div>';
    });

    document.addEventListener('prosper:business-slips-updated', (event) => {
        if (!state.slipId || state.isSubmitting || !modalElement.classList.contains('show')) {
            return;
        }

        const refreshedSlip = event.detail?.slips?.find((slip) => String(slip.id) === state.slipId);
        if (!refreshedSlip || refreshedSlip.status !== 'open') {
            renderUnavailable('この伝票は会計準備中または会計済みのため編集できません。営業中一覧を再表示してください。');
        }
    });
})();
