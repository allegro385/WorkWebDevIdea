// ログ管理の抽出内容は、ツール利用ログを選択した場合だけ表示します。
// 表示の切替は操作補助であり、サーバー側では種別に応じた抽出内容だけを適用します。
(function () {
    'use strict';

    function apply() {
        var kind = document.getElementById('log-kind');
        var field = document.getElementById('log-extraction-field');
        var note = document.getElementById('log-export-note');
        if (!kind || !field) return;
        var isToolUsage = kind.value === 'ToolUsage';
        field.hidden = !isToolUsage;
        if (note) {
            note.textContent = isToolUsage
                ? 'ツール利用ログの明細、またはツールごとの重複を除いた利用者数を出力します。'
                : '選択したログの全列・全件の明細を出力します。';
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        var kind = document.getElementById('log-kind');
        if (kind) kind.addEventListener('change', apply);
        apply();
    });
})();
