# 新規Webツールの生成スクリプト

ToolIdからテンプレートツールを複製し、新しいWebツールのソース一式を作ります。方針は[サンプル・テンプレートツール設計方針 第3節](../../設計書/12_サンプル・テンプレートツール設計方針.md)を正とします。

| ファイル | 役割 |
| --- | --- |
| `new-tool.bat` | 入口。ToolIdを引数または対話入力で受け取り、PowerShellスクリプトを呼び出します |
| `New-SalesSupportTool.ps1` | 複製、置換、生成前後の検査、`restore`・`build`・`test`を行います |

Windows PowerShell 5.1で動作します。実行ポリシーは`-ExecutionPolicy Bypass`をバッチ側で指定します。

## 使い方

リポジトリを取得した端末で、`SalesSupport\scripts`から実行します。

```text
new-tool.bat T001
new-tool.bat T001 -ToolName "見積計算ツール" -HttpsPort 7101 -HttpPort 5101
```

引数なしで実行するとToolIdを対話入力できます。PowerShellから直接実行する場合は次のとおりです。

```text
powershell -NoProfile -ExecutionPolicy Bypass -File .\New-SalesSupportTool.ps1 -ToolId T001 -SkipBuild
```

| パラメーター | 既定 | 内容 |
| --- | --- | --- |
| `-ToolId` | （必須） | Portalのツールマスタへ登録するツールID |
| `-ToolName` | ToolIdと同じ | ローカル起動時のアプリ名。障害ログのアプリ名に使用します |
| `-HttpsPort` / `-HttpPort` | テンプレートと同じ`7075`／`5275` | ローカル起動のポート。複数ツールを同時に起動する場合は重複しない値を指定します |
| `-SalesSupportRoot` | スクリプトの親フォルダー | `SalesSupport`フォルダーの場所 |
| `-SkipBuild` | 指定なし | `restore`・`build`・`test`を実行しません |

## 命名規則

ToolIdはハイフンと下線で区切り、各語の先頭だけを大文字にして連結した名称`<名称>`を導出します。

| ToolId | 導出した名称 |
| --- | --- |
| `T001` | `T001` |
| `TOOL_001` | `Tool001` |
| `DMY-WEB` | `DmyWeb` |

| 生成物 | 名称 |
| --- | --- |
| ソリューション | `SalesSupport/<名称>.slnx` |
| プロジェクト | `SalesSupport/src/<名称>/SalesSupport.<名称>.Web` |
| ルート名前空間 | `SalesSupport.<名称>.Web` |
| テストプロジェクト | `SalesSupport/tests/SalesSupport.<名称>.Tests` |
| Portalからの起動URL例 | `/tools/<ToolId>/app` |

ToolIdはソースへ書かず、`Properties/launchSettings.json`のローカル起動用環境変数と生成したREADMEにだけ記載します。

## 置換対象

テンプレート内の次の文字列だけを置き換えます。任意の文字列の全面置換は行いません。

| 置換対象 | 置換後 |
| --- | --- |
| `SalesSupport.Template.Web` | `SalesSupport.<名称>.Web` |
| `SalesSupport.Template.Tests` | `SalesSupport.<名称>.Tests` |
| `src/Template/` | `src/<名称>/` |
| `template.slnx` | `<名称>.slnx` |
| `__TOOL_ID__` | ToolId |
| `__TOOL_NAME__` | `-ToolName`の値 |
| `7075` / `5275` | `-HttpsPort` / `-HttpPort`の値（指定時のみ） |

テンプレートの`README.md`は複製せず、生成したツール用のREADMEを新しく作成します。`bin`・`obj`・`.vs`は複製しません。

## 検査

生成前に次を確認し、1件でも該当すると何も作らずに中断します。

- ToolIdが英数字・下線・ハイフンの1～20文字であること
- 予約値`SYSTEM`でないこと。導出した名称が`Common`・`Portal`・`Template`等の既存名やWindowsの予約デバイス名でないこと
- 導出した名称の先頭が英字で、C#の識別子として使えること
- 生成先のフォルダー・ソリューションが存在しないこと（大文字小文字を区別せずに照合します）
- テンプレート一式が存在すること

生成後は次を確認します。いずれかが失敗した場合は生成物を完成品として扱わず、終了コード1で終わります。

- プレースホルダーと旧テンプレート名が残っていないこと（ファイル名を含む）
- ソリューションのプロジェクト参照と各プロジェクトの`ProjectReference`が実在すること
- `dotnet restore`・`dotnet build`・`dotnet test`が成功すること（`-SkipBuild`指定時と.NET SDKがない場合は未実行として表示します）

## スクリプトが行わないこと

DB登録、SQL適用、アップロード条件の登録、IISのアプリケーション・アプリケーションプール作成、環境変数の設定、証明書、本番配置は行いません。生成後の作業は[導入・運用](../../設計書/05_導入・運用.md#deployment)と、生成したツールのREADMEに従ってください。
