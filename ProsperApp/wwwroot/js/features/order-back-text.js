(() => {
    const toAmount = (value) => Number(value) || 0;

    const summary = (cast, item, quantity) => {
        const count = Math.max(0, Math.trunc(Number(quantity) || 0));
        const regularBack = toAmount(item?.castBackRegularUnitAmount) * count;
        const nominationBack = toAmount(item?.castBackNominationUnitAmount) * count;
        return `${cast.display} / ドリンク ${window.MoneyText.yen(regularBack)} / 担当 ${window.MoneyText.yen(nominationBack)}`;
    };

    window.OrderBackText = { summary };
})();
