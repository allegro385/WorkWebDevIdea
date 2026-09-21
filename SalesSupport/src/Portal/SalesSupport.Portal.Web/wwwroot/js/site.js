// Portal固有の最小限のスクリプトです。確認ダイアログは操作補助であり、サーバー側の検証・認可の代わりにしません。
(function () {
    'use strict';

    // 状態を限定公開または非公開へ変更する場合にだけ確認する文言です。
    var STATUS_CONFIRM = '状態を変更すると、利用者向けの表示と利用可否が変わります。よろしいですか？';

    // フォームまたは押したボタンに指定された確認文言を返します。確認が不要な場合はnullです。
    function confirmMessage(form, submitter) {
        if (submitter && submitter.dataset && submitter.dataset.ptConfirm) return submitter.dataset.ptConfirm;
        if (form.dataset.ptConfirm) return form.dataset.ptConfirm;
        if (!form.dataset.ptConfirmStatus) return null;

        var status = form.querySelector('#basic-status');
        var current = form.querySelector('#basic-status-current');
        if (!status || !current || status.value === current.value) return null;
        return status.value === 'PRIVATE' || status.value === 'HIDDEN' ? STATUS_CONFIRM : null;
    }

    // 二重送信の抑止より先に判定するため、documentのキャプチャー段階で確認します。
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!(form instanceof HTMLFormElement)) return;

        var message = confirmMessage(form, event.submitter);
        if (message && !window.confirm(message)) {
            event.preventDefault();
            event.stopPropagation();
        }
    }, true);
})();
