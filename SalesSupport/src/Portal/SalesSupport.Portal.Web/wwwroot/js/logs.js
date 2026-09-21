// ログ管理の抽出内容は、ツール利用ログを選択した場合だけ表示します。
// 表示の切替は操作補助であり、サーバー側では種別に応じた抽出内容だけを適用します。
(function () {
    'use strict';

    function apply() {
        var kind = document.getElementById('log-kind');
        var field = document.getElementById('log-extraction-field');
        if (!kind || !field) return;
        field.hidden = kind.value !== 'ToolUsage';
    }

    document.addEventListener('DOMContentLoaded', function () {
        var kind = document.getElementById('log-kind');
        if (kind) kind.addEventListener('change', apply);
        apply();
    });
})();
