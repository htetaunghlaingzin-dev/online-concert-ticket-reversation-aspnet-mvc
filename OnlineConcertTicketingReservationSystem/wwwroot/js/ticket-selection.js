(() => {
    const form = document.getElementById('ticket-selection');
    if (!form) return;
    const quantity = document.getElementById('quantity');
    function update() {
        const type = form.querySelector('input[name="TicketTypeId"]:checked');
        if (type) {
            quantity.max = Math.min(100, Number(type.dataset.stock));
            document.getElementById('quantity-hint').textContent = `${type.dataset.stock} available. Maximum ${quantity.max} per order.`;
        }
        let total = type ? Number(type.dataset.price) * Math.max(0, Number(quantity.value)) : 0;
        form.querySelectorAll('.merch-quantity').forEach(input => total += Number(input.dataset.price) * Math.max(0, Number(input.value)));
        document.getElementById('ticket-total').textContent = `MMK ${total.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    }
    form.addEventListener('input', update);
    update();
})();
