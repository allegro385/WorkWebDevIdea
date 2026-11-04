# SalesSupport 修正依頼（Common／Portal／ツールテンプレート／サンプルツール）

- 作成日：2026-11-04
- 経緯：ツールテンプレートから生成した検証用ツール（以下「検証ツール」）を、Portal と合わせて開発サーバー（IIS・HTTP・Anonymous 設定）に配置しました。その際に見つかった問題と改善の要望をまとめます。
- 優先度：**高**＝利用者に見える不具合・統一性、**中**＝配置・運用作業でつまずいた点や必要な機能、**低**＝文書化・改善
- 表記：ToolId・ツール名・URL は `<ToolId>`・`<ツール名>`・`<サーバー>` と表記します。

## 対象と依頼一覧

| No. | 優先度 | 対象 | 内容 |
| --- | --- | --- | --- |
| 1 | 高 | Common・Portal | ヘッダー・パンくず・ページタイトルの見た目を Common に集約する |
| 2 | 高 | Common・テンプレート | ツール画面のパンくずとタイトルを「ツール一覧 / ツール名」にする |
| 3 | 高 | テンプレート・サンプル | ブラウザーの戻る・進むでフォームの再送信を要求しない（PRG） |
| 4 | 高 | Common | 開発環境のヘッダー（赤帯）の色を rgb(220, 53, 69) にする |
| 5 | 中 | Portal | ユーザー登録コマンドを本番でも使える形にし、TSV から一括登録するツールにする |
| 6 | 中 | テンプレート・サンプル・Portal | web.config に環境変数を既定で記載し、再発行で消えないようにする |
| 7 | 中 | Common・config | 共通設定ファイル・保存領域・DB 接続まわりの初期設定をつまずかない形にする |
| 8 | 中 | sql | サンプル以外のツールを登録する SQL のひな形を用意する |
| 9 | 中 | Common | 拒否理由（FailureReason）が実際の原因と合っているか確認する |
| 10 | 中 | Portal・Common | ツール管理のバージョン：最新の判定を更新日付にし、入力形式の制限を撤廃する |
| 11 | 中 | Portal・Common・sql | ツールのカテゴリ（ToolCategories）を廃止する |
| 12 | 低 | テンプレート README | 題材の削除手順、CSP、静的ファイルの注意を追記する |
| 13 | 低 | Common（検討） | 単独モードでの HTTP 検証の扱いを決める |

---

## 1. ヘッダー・パンくず・ページタイトルの見た目を Common に集約（高）

### 現象
Portal とツールでヘッダーの見た目（文字の大きさ、メニューの位置）が違います。

| 箇所 | Portal | ツール |
| --- | --- | --- |
| ヘッダーの「ツール一覧」ボタン（`button.btn.ss-nav-button`） | 21px（Portal の `site.css:41` の `.ss-nav-button { font-size: 1.3125rem }`） | 16px（Bootstrap の既定値） |

### 原因
共通ヘッダーの部品（`.ss-` で始まるクラス）は Common の `_SalesSupportLayout`／`SalesSupportHeader` が出力しています。しかし、その見た目の一部が **Portal の `wwwroot/css/site.css`** に書かれています。ツールは Portal の site.css を読み込まないため、Bootstrap の既定値で表示されます。

### 依頼内容
- Portal の `site.css` から、次の共通部品のルールを Common の `wwwroot/css/sales-support.css` に移し、Portal 側からは削除してください。
  - ヘッダー：`.ss-header`、`.ss-header-inner`、`.ss-brand`、`.ss-nav`、`.ss-nav-group`、`.ss-nav-button`、`.ss-dropdown`、`.ss-account` など
  - パンくず：`.ss-breadcrumb`
  - ページタイトル：`.ss-page-title`、`.ss-page-title-band`、`.ss-current-tool`
- Portal 固有の部分（お知らせ、ツール一覧の表など）は Portal の site.css に残してください。
- ツールのテンプレートの site.css には共通部品のルールを書かない方針を、README に明記してください。

### 確認方法
- Portal とツールのヘッダーを F12 →「計算済み」で比べ、`font-size`・余白・色が一致すること
- Portal のタイトル帯（水色のグラデーション）がツールにも同じく表示されること

---

## 2. ツール画面のパンくずとタイトル（高）

### 現状
```
トップ / ツール一覧 / 用途        ← 末尾がツール内の画面名（ステップ名）
<ツール名>                        ← 小さい文字（.ss-current-tool）
用途                              ← 大きい見出し（ページタイトル）
```

### 希望
```
トップ / ツール一覧 / <ツール名>
<ツール名>                        ← Portal と同じタイトル帯で表示
```
- パンくずの末尾は**ツール名**（`portal.Tools.ToolName`、または `SalesSupport:Application:Name`）にしてください。
- 大きい見出しは**ツール名**にし、Portal のページタイトルと同じ見た目（タイトル帯）にしてください。
- ツール内の画面名（ステップ名や「計算結果」など）は、各ツールが本文の見出しで表示する方針にしてください。

### 依頼内容
- Common：`IToolPageShell.Build(...)` で、画面名を省略できるようにしてください。省略したときは「パンくずの末尾＝ツール名」「見出し＝ツール名」とし、今の「ツール名を小さく、画面名を大見出しにする」表示はやめてください。
- テンプレート／サンプル：Controller の呼び出しを新しい書き方にそろえてください。
- 検証ツール側は、Common の修正後にこちらで呼び出しを合わせます。

---

## 3. ブラウザーの戻る・進むでフォームの再送信を要求しない（高）

### 現象
画面遷移を POST の応答としてそのまま表示しているため、ブラウザーの戻る・進むで「フォームを再送信しますか？」（ERR_CACHE_MISS）が表示されます。

### 原因
- POST の応答画面は、表示し直すのにフォームの再送信が必要です。
- Antiforgery トークンを出力した画面には、ASP.NET Core が `Cache-Control: no-cache, no-store` を自動で付けます。このためキャッシュの設定を変えても回避できず、変える必要もありません（ログアウト後に画面を残さないためにも no-store のままが適切です）。

### 依頼内容
- テンプレートとサンプルの画面遷移を **PRG（POST → リダイレクト → GET）** にしてください。
  - 入力エラーのとき：今までどおり POST の応答でエラーを表示する
  - 成功したとき：GET の画面へリダイレクトする（入力の引き継ぎはクエリ文字列、または TempData）
- 計算・保存など**利用ログ（WEB_EXECUTE）を記録する処理の結果画面は、POST の応答のまま**にしてください。GET にすると、更新や戻る・進むで二重に記録されるためです。この方針を README に明記してください。
- 参考：検証ツールの `ToolController` に次の形で実装し、開発サーバーで動作を確認済みです。必要であればソースを提供します。
  - `[HttpGet("wizard")]`：ステップの画面を表示する
  - `[HttpPost("wizard")]`：入力を検証してリダイレクトする
  - 入力は `RouteValueDictionary` でクエリ文字列に付けて引き継ぐ

---

## 4. 開発環境のヘッダー（赤帯）の色（高）

### 依頼内容
- 開発環境で表示するヘッダーの帯（`.ss-header-development`）の色を **`rgb(220, 53, 69)`**（`#dc3545`）に変更してください。
- No.1 に合わせて、色の指定も Common の `sales-support.css` の1か所にまとめ、Portal とツールで同じ色になるようにしてください。
- 帯の文字（サイト名・メニュー）が、この背景色の上で十分なコントラストで読めることを確認してください。

---

## 5. ユーザー登録コマンドを本番でも使える形にし、TSV から一括登録するツールにする（中）

### 現状
- 管理者などのユーザーは、Portal の `add-test-user` コマンドで1名ずつ登録します。このコマンドは開発環境専用で、本番では使えません。
- Identity のユーザーは SQL で直接作れない（作ってはいけない）ため、本番で最初の管理者や利用者をまとめて登録する手段がありません。

### 依頼内容
- **開発環境だけという制限をなくし**、本番でも使えるユーザー登録コマンド（または管理用の小さなツール）にしてください。名前は `add-test-user` から用途に合ったものに変更してください（例：`import-users`）。
- **TSV ファイルを読み込んで一括登録**できるようにしてください。

### 要件（案）
列名・細部は実装側で決めてください。

| 項目 | 要件 |
| --- | --- |
| 入力 | UTF-8（BOM の有無は問わない）の TSV、1行目は見出し。列の例：`Email`、`DisplayName`、`RoleCode`、`IsActive` |
| 検証 | 全行を先に検証し、エラーが1件でもあれば登録しない。エラーは行番号と理由を出力する（メール形式、存在しない RoleCode、ファイル内でのメールの重複など） |
| 既存ユーザー | 既定は「スキップして件数を報告」。更新するかどうかはオプションで指定する（例：`--update`） |
| 試行モード | `--dry-run` で、DB を変更せずに結果（追加・更新・スキップ・エラーの件数）だけを表示する |
| トランザクション | 全件を1トランザクションで登録し、途中で失敗したら全件戻す |
| 接続先 | 共通設定ファイルの接続先を使い、実行前に接続先の DB 名と環境区分（EnvironmentCode）を表示する。本番では確認の入力（`y`）を求める |
| 監査 | 登録結果を `log` スキーマ（または操作ログ）に記録する。実行者・ファイル名・件数を残す |
| 個人情報 | 実行結果の表示やログには、パスワード・ワンタイムコードなどの秘密情報を出さない |
| 文書 | README に実行方法、TSV のひな形、ロール一覧、本番での手順（バックアップ、確認方法）を記載する |

---

## 6. web.config の既定の書き方（中）

### 現象
- 発行で生成される web.config は `<aspNetCore … />` の1行だけで、環境変数を毎回手で追加する必要がありました。
- 再発行すると web.config が上書きされ、追加した環境変数が消えます。

### 依頼内容
テンプレート／サンプル／Portal のプロジェクトに `web.config` を含め、発行物に次の形で出力されるようにしてください（`<ToolId>`・`<ツール名>`・`<サイト名>` はテンプレートの生成スクリプトで置き換え）。

```xml
<aspNetCore processPath=".\SalesSupport.<ToolId>.Web.exe" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="SalesSupport__Application__ToolId" value="<ToolId>" />
    <environmentVariable name="SalesSupport__Application__Name" value="<ツール名>" />
    <!-- サーバー上で単独モードの確認だけ行う場合に有効にする。HTTPS 必須・エラー詳細が画面に出るため、確認後は必ず戻す
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Development" />
    <environmentVariable name="SalesSupport__Tool__ExecutionMode" value="Standalone" />
    <environmentVariable name="SalesSupport__Site__Name" value="<サイト名>" />
    -->
  </environmentVariables>
</aspNetCore>
```

- `ASPNETCORE_ENVIRONMENT=Development` と `ExecutionMode=Standalone` を既定で有効にすると、本番に配置しても**単独モード（共通ユーザー・DB なし・エラー詳細の表示）で動いてしまいます**。そのため、この2行はコメントにし、既定は連携モードとしてください。
- `stdoutLogEnabled` の既定は `false` にし、調査時だけ `true` にする手順を README に記載してください。
- 日本語の値を含むため、web.config は UTF-8 で出力されるようにしてください。

---

## 7. 共通設定ファイル・保存領域・DB 接続の初期設定（中）

配置時に次の順でつまずきました。

| 起きたこと | 原因 | 依頼内容 |
| --- | --- | --- |
| 単独モードのまま HTTP で公開できず、https へ転送された | 単独モードは `TransportMode` を読まず HTTPS に固定している（`CommonServiceExtensions`） | README に「単独モードは HTTPS 専用、クライアントへの公開は連携モード」と明記する（No.11 も参照） |
| 起動時に「Storage:PermanentRootが不正です」 | `storage\files` フォルダーが未作成 | ① エラーメッセージに理由（存在しない、権限がない等）とパスを出す。② `keys`・`storage\temp`・`storage\files` の作成と、アプリケーションプールへの権限付与を行うスクリプトを `scripts\` に用意する |
| DB 接続で「信頼されていない機関によって証明書チェーンが発行されました」 | SQL Server が自己署名の証明書を使っている | `config\salessupport.common.Development.example.json` の接続文字列の例に `TrustServerCertificate=True` を記載する（開発用のみ、と注記） |
| `TransportMode`・`AuthenticationMode` を `SalesSupport` の直下に書いてしまった | 正しくは `SalesSupport:Site` の下 | example.json に両キーをコメント付きで記載する |
| `Portal:BaseUrl` を https のまま HTTP で運用した | スキームを一致させる必要がある | example.json と README に「TransportMode と BaseUrl のスキームを一致させ、末尾は `/`」と記載する |
| 共通設定ファイルを変えても、ツール側に反映されなかった（パンくずが古い URL を指した） | 設定は起動時に読むため、Portal だけリサイクルしてもツールは古い値のまま | README に「共通設定ファイルの変更後は、Portal と全ツールのアプリケーションプールをリサイクルする」と記載する |

---

## 8. ツール登録 SQL のひな形（中）

### 現状
ツールを登録する SQL は `sql\sample\910_CreateSampleEstimate.sql` しかありません。このスクリプトはサンプル用の表の作成や開発 DB のチェックを含むため、新しいツールの登録には一部を抜き出して使う必要がありました。

### 依頼内容
- `sql\` に「ツール1件を登録する」SQL のひな形を追加してください（`portal.Tools`・`portal.ToolRoles`、必要に応じて `FileUploadPolicies`。No.11 のカテゴリ廃止を反映した形にしてください）。
- `WebAppUrl` の命名規則と IIS のエイリアスのそろえ方を README で統一してください。サンプルは `/tools/<ToolId>/app` ですが、ほかの形でも配置できるため、どちらを標準とするかを決めてください。
- Anonymous モードの共通ユーザーがどのロールで判定されるかを明記し、`ToolRoles` に何を登録すればよいか分かるようにしてください。

---

## 9. 拒否理由（FailureReason）の確認（中）

### 現象
`portal.SystemSettings` の `SITE / PUBLICATION_STATUS` が `PUBLIC` なのに、ツールへのアクセスが 404 になりました。このとき、`log.UserActivityLogs` には `ACCESS_DENIED / DENIED / SITE_PRIVATE` と記録されていました。ツールを `portal.Tools` に登録すると解消しました。

### 依頼内容
- `AccessEvaluator` で、ツールが未登録のときの拒否理由が `SITE_PRIVATE` になっていないか確認してください。もしそうなら、`TOOL_NOT_FOUND` など実際の原因が分かる値にしてください。
- 原因は依頼者の推定で、コードでは確認していません。

---

## 10. ツール管理のバージョン（中）

### 依頼内容
**① 最新バージョンの判定を、バージョン番号ではなく更新日付で行う**
- 今はバージョン番号の大小（Common の `CommonValidation.CompareVersions` で数値の組として比較）で最新を判定しています。これを、**更新日付が最も新しいもの**を最新とする判定に変更してください。
- 同じ更新日付が複数ある場合の扱い（登録日時やIDの新しい方を優先するなど）を決めて、仕様に明記してください。
- Portal のツール一覧に表示するバージョン、ツール詳細の履歴の並び順、通知などで「最新」を使っている箇所をすべて同じ判定にそろえてください。

**② バージョンの入力制限（`1.0.0` 形式）を撤廃する**
- 今は `CommonValidation.IsVersion`（数字3組、各0～99、先頭ゼロ禁止）で入力を制限しています。この形式の制限を撤廃し、任意の文字列（例：`2026.11`、`1.0.0-beta`、`第2版`）を入力できるようにしてください。
- 形式の制限はなくしても、最大文字数・必須かどうか・制御文字の禁止などの基本的な検証は残してください。最大文字数は DB の列定義と合わせてください。
- 形式の制限を前提にした DB の CHECK 制約やインデックスがあれば、あわせて見直してください。

**③ 入力がない場合の表示は継続**
- バージョンが未入力の場合に `Ver.1.0.0` と表示する今の動きは、そのまま残してください。

### 確認方法
- バージョン番号が小さくても、更新日付が新しいものが「最新」と表示されること
- `1.0.0` 形式以外のバージョンを登録・表示できること
- バージョンが未入力のツールで `Ver.1.0.0` と表示されること

---

## 11. ツールのカテゴリ（ToolCategories）の廃止（中）

### 依頼内容
ツールのカテゴリの仕組みを廃止し、`portal.ToolCategories` を削除してください。

### 対応が必要と思われる箇所
| 箇所 | 内容 |
| --- | --- |
| DB | `portal.ToolCategories` の表と、`portal.Tools.CategoryId` 列（NOT NULL・外部キー）を削除する。既存の DB 向けに移行用の SQL も用意する |
| `sql\001_CreateTables.sql`・`002_SeedMasterData.sql` | 表・列・初期データの定義から削除する |
| `sql\sample\910_CreateSampleEstimate.sql` | サンプルの分類を作る処理と、分類の重複チェック（`[SAMPLE] 見積試算`）を削除する |
| Portal：ツール一覧 | 「カテゴリ」列と、カテゴリによる並び替え・絞り込みを削除する |
| Portal：ツール管理 | ツールの登録・編集画面のカテゴリ欄と、カテゴリのマスタ管理画面（あれば）を削除する |
| Common・Portal のエンティティ／マッピング | カテゴリのエンティティと `CategoryId` のプロパティ、関連する問い合わせを削除する |
| 設計書・README | カテゴリに関する記述を削除する |

- ツール一覧の並び順は `SortOrder`（同じ値ならツール名）で決まるようにしてください。
- 削除により、既存のツールの公開状態・ロールの割当てなどが変わらないことを確認してください。

### 確認方法
- 新しい DB でも既存の DB（移行 SQL を適用したもの）でも、Portal とツールが起動すること
- ツール一覧・ツール管理の画面にカテゴリの表示や入力欄が残っていないこと
- ツールを新しく登録できること（カテゴリなし）

---

## 12. テンプレート README の追記（低）

- **題材（TemplateExample）の削除手順**：VS の「プロジェクトから除外」ではファイルが残り、題材の残存チェックでビルドが止まります。次の削除と修正が必要なことを手順に書いてください。
  - 削除するもの：`TemplateExample\`、`Views\Tool\TemplateExample*.cshtml`
  - 修正するもの：`_ViewImports.cshtml`、`ToolController.cs`、`ToolServiceExtensions.cs`
- **CSP**：Common の CSP は `script-src 'self'` のため、インラインスクリプトは動きません。既存の HTML を移すときは、スクリプトを外部ファイルに分ける必要があることを記載してください。
- **静的 HTML の配置**：マニュアルなど、Webルートに置く静的ファイルの扱いを記載してください。

---

## 13. 単独モードでの HTTP 検証（低・検討）

単独モードは HTTPS 固定のため、HTTP しか開いていない開発サーバーでは、クライアントから単独モードの動作を確認できません。

- **案A**：今のまま。README に「クライアントからの確認は連携モード」と明記する
- **案B**：単独モードでも `SalesSupport__Site__TransportMode=Http` を読めるようにする（Development 環境に限る）

依頼者としては案A（文書化のみ）で問題ありません。
