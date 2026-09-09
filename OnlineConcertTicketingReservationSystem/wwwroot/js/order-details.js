(() => {
    const element = document.getElementById('order-modal');
    if (!element) return;
    const body = document.getElementById('order-modal-body');
    let pending;
    document.querySelectorAll('.order-view').forEach(link => link.addEventListener('click', async event => {
        event.preventDefault();
        pending?.abort();
        const request = new AbortController();
        pending = request;
        document.getElementById('order-modal-title').textContent = `Order #${link.dataset.orderId}`;
        body.textContent = 'Loading order details…';
        bootstrap.Modal.getOrCreateInstance(element).show();
        try {
            const url = new URL(link.href);
            url.searchParams.set('popup', 'true');
            const response = await fetch(url, { signal: request.signal });
            if (!response.ok || response.redirected) throw new Error('Unable to load order details. Refresh the page and try again.');
            const html = await response.text();
            if (!request.signal.aborted) body.innerHTML = html;
        } catch (error) {
            if (error.name !== 'AbortError') body.textContent = error.message;
        }
    }));
    element.addEventListener('hidden.bs.modal', () => pending?.abort());
})();
