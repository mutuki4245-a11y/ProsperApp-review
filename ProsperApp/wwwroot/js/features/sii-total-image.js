(() => {
    const toAmount = (value) => Math.round(Number(value) || 0);

    const createPng = (width, height, draw) => new Promise((resolve, reject) => {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d');
        if (!context) {
            reject(new Error('合計額の印刷画像を作成できません。'));
            return;
        }

        context.fillStyle = '#ffffff';
        context.fillRect(0, 0, width, height);
        context.fillStyle = '#000000';
        draw(context);
        canvas.toBlob((blob) => {
            if (!blob) {
                reject(new Error('合計額の印刷画像を作成できません。'));
                return;
            }
            resolve(blob);
        }, 'image/png');
    });

    const createTotal = ({ label, amount, lineWidth }) => {
        const columns = Number.isFinite(Number(lineWidth)) ? Math.max(24, Number(lineWidth)) : 48;
        const width = Math.round(columns * 12);
        const height = 58;
        const value = `${toAmount(amount).toLocaleString('ja-JP')}円`;

        return createPng(width, height, (context) => {
            context.textBaseline = 'alphabetic';
            context.font = '600 24px sans-serif';
            context.fillText(String(label || '合計'), 0, 42);
            context.font = '700 36px sans-serif';
            context.textAlign = 'right';
            context.fillText(value, width, 44);
        });
    };

    window.ProsperSiiPrintImage = { createTotal };
})();
