// 問い合わせ検索の送信期間だけを消去します。検索条件全体は「条件クリア」で初期化します。
(function () {
    'use strict';

    var clear = document.getElementById('clear-inquiry-period');
    if (!clear) return;

    clear.addEventListener('click', function () {
        document.getElementById('search-from').value = '';
        document.getElementById('search-to').value = '';
    });

    document.querySelectorAll('.pt-inquiry-status select').forEach(function (select) {
        select.addEventListener('change', function () {
            var cell = select.closest('.pt-inquiry-status');
            var color = select.selectedOptions[0].dataset.color;
            if (color) cell.style.setProperty('--pt-status-color', color);
            else cell.style.removeProperty('--pt-status-color');
        });
    });
})();
