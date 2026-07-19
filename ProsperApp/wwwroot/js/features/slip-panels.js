(() => {
    const container = document.querySelector('[data-slip-panels-url]');
    const panelsUrl = container?.dataset.slipPanelsUrl ?? '';
    const panelIds = [
        'slipCustomersSection',
        'slipNominationsSection',
        'slipOrdersSection'
    ];

    if (!container || !panelsUrl) {
        return;
    }

    const showError = () => {
        panelIds.forEach((id) => {
            const panel = document.getElementById(id);
            if (panel) {
                panel.innerHTML = '<div class="alert alert-warning">パネルを取得できませんでした。画面を再読み込みしてください。</div>';
            }
        });
    };

    const initializePanels = () => {
        window.PartialForms?.prepareDynamicContent?.(container);
        window.SlipAdjustments?.init();
        window.SlipNominations?.syncDefaults?.();
        window.SlipOrders?.resetOrderCorrection?.();
        window.SlipOrders?.renderOrderQueue?.();
    };

    const loadPanels = async () => {
        try {
            const response = await fetch(panelsUrl, {
                headers: {
                    'X-Requested-With': 'XMLHttpRequest'
                }
            });
            if (!response.ok) {
                throw new Error('Slip panels load failed.');
            }

            const html = await response.text();
            const preview = document.createElement('div');
            preview.innerHTML = html;
            panelIds.forEach((id) => {
                const target = document.getElementById(id);
                const source = preview.querySelector(`#${id}`);
                if (target && source) {
                    target.innerHTML = source.innerHTML;
                }
            });
            initializePanels();
        } catch {
            showError();
        }
    };

    void loadPanels();
})();
