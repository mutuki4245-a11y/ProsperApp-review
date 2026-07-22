(() => {
    let tail = Promise.resolve();

    const enqueue = (job) => {
        const run = tail.then(job, job);
        tail = run.catch(() => {});
        return run;
    };

    window.ProsperSiiPrintQueue = { enqueue };
})();
