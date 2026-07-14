(() => {
    const defaultLimit = 80;
    const defaultEmptyMessage = '出勤キャストが登録されていません。';

    const createItem = (label, helpText, onSelect) => {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'cast-select-modal__item';

        const name = document.createElement('strong');
        name.textContent = label;
        button.appendChild(name);

        if (helpText) {
            const help = document.createElement('span');
            help.textContent = helpText;
            button.appendChild(help);
        }

        button.addEventListener('click', onSelect);
        return button;
    };

    const renderEmpty = (list, message) => {
        const empty = document.createElement('p');
        empty.className = 'text-muted mb-0';
        empty.textContent = message || defaultEmptyMessage;
        list.appendChild(empty);
    };

    const getMatches = (casts, limit) => (Array.isArray(casts) ? casts : []).slice(0, limit || defaultLimit);

    const renderRequired = (list, casts, options = {}) => {
        if (!list) {
            return;
        }

        const matches = getMatches(casts, options.limit);
        list.innerHTML = '';
        if (matches.length === 0) {
            renderEmpty(list, options.emptyMessage);
            return;
        }

        matches.forEach((cast) => {
            const label = options.getLabel ? options.getLabel(cast) : cast.display;
            list.appendChild(createItem(label || '', null, () => options.onSelect?.(cast)));
        });
    };

    const renderOptionalBackTarget = (list, casts, options = {}) => {
        if (!list) {
            return;
        }

        const matches = getMatches(casts, options.limit);
        list.innerHTML = '';
        list.appendChild(createItem(
            options.noneLabel || '指定なし',
            options.noneHelpText || 'バックの摘要を付けずに登録',
            () => options.onNone?.()
        ));

        matches.forEach((cast) => {
            const label = options.getLabel ? options.getLabel(cast) : cast.name;
            list.appendChild(createItem(label || '', null, () => options.onSelect?.(cast)));
        });
    };

    window.CastSelectModal = {
        renderRequired,
        renderOptionalBackTarget
    };
})();
