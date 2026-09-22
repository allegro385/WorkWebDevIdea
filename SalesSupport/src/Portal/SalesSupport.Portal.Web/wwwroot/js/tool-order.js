// ツール一覧の表示順を上下ボタンで変更し、保存する番号を表示中の順番にそろえます。
(function () {
    'use strict';

    var rows = document.getElementById('tool-order-rows');
    if (!rows) return;

    function updateOrder() {
        Array.from(rows.querySelectorAll('tr')).forEach(function (row, index, allRows) {
            var value = row.querySelector('.pt-order-value');
            if (!value) return;
            value.value = index + 1;
            row.querySelector('.pt-order-label').textContent = index + 1;
            var up = row.querySelector('[data-direction="up"]');
            var down = row.querySelector('[data-direction="down"]');
            if (up) up.disabled = index === 0;
            if (down) down.disabled = index === allRows.length - 1;
        });
    }

    rows.addEventListener('click', function (event) {
        var button = event.target.closest('[data-direction]');
        if (!button) return;
        var row = button.closest('tr');
        if (button.dataset.direction === 'up' && row.previousElementSibling) {
            rows.insertBefore(row, row.previousElementSibling);
        } else if (button.dataset.direction === 'down' && row.nextElementSibling) {
            rows.insertBefore(row.nextElementSibling, row);
        }
        updateOrder();
        button.focus();
    });

    updateOrder();
})();
