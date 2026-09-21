// Portalと各ツールで共有する最小限のスクリプトです。画面固有の処理は各アプリへ置きます。
(function () {
    'use strict';

    // data-ss-once を付けたフォームの二重送信を防ぎます。
    // 送信ボタンはdisabledにせず、押した項目の値が送信対象から外れないようにします。
    function guardDoubleSubmit() {
        document.querySelectorAll('form[data-ss-once]').forEach(function (form) {
            form.addEventListener('submit', function (event) {
                if (form.dataset.ssSubmitting === '1') {
                    event.preventDefault();
                    return;
                }
                form.dataset.ssSubmitting = '1';
                form.querySelectorAll('button[type="submit"]').forEach(function (button) {
                    button.setAttribute('aria-disabled', 'true');
                    button.classList.add('disabled');
                });
            });
        });
    }

    document.addEventListener('DOMContentLoaded', guardDoubleSubmit);
})();
