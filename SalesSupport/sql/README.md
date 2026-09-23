# SalesSupport database scripts

SQL Server用の初期構築スクリプトです。対象データベースを作成した後、次の順序で実行します。

1. `001_CreateTables.sql` — スキーマ、全テーブル、制約、索引、監査トリガー、問い合わせ番号採番プロシージャ
2. `002_SeedMasterData.sql` — 固定コード、システム設定、アップロード制限、共通エラーコード
3. `900_SeedDummyData.sql` — 開発・画面確認用のダミー業務データ

見積試算サンプルの専用SQLは`sample/`に分離しています。`001`・`002`へ混ぜず、[専用の適用・清掃手順](sample/README.md)を確認してください。

## 実行上の注意

- すべてSQL Server向けです。接続先データベースを明示してから実行してください。
- `001_CreateTables.sql`は空のデータベース専用です。対象テーブルが一つでも存在する場合は停止します。
- `002_SeedMasterData.sql`は同じキーの行を更新し、不足行を追加するため再実行できます。運用で追加した行は削除しません。
- `900_SeedDummyData.sql`は本番投入禁止です。スクリプト先頭の`@ExpectedDevelopmentDatabase`を実際の開発DB名へ書き換え、接続中のDB名との一致を確認してから実行してください。DBに`EnvironmentName`拡張プロパティがある場合は`DEVELOPMENT`以外を拒否します。DB名確認は運用上の確認であり、本番の自動判別ではありません。
- ダミー投入は既存データを削除しません。ダミーID・名称の衝突時は停止します。再投入には新しい開発専用DBを用意してください。
- ダミースクリプトはIdentityユーザーを直接作成しません。あらかじめアプリケーションのUserManager経由で、有効な`ADMIN`ユーザーと`USER`ユーザーを一名以上登録してください。
- `ToolFiles`のダミー行は作成しません。物理ファイルを伴わない不整合な参照を避けるためです。
- ツール分類とFAQ分類は組織固有のマスタです。本番用の初期値は運用決定後に別途登録し、ダミースクリプトの値を流用しないでください。
- SQLファイルはUTF-8です。`sqlcmd`では`-f 65001`を指定してください。

実行例：

```powershell
sqlcmd -S .\SQLEXPRESS -E -C -f 65001 -d SalesSupport -i .\sql\001_CreateTables.sql -b
sqlcmd -S .\SQLEXPRESS -E -C -f 65001 -d SalesSupport -i .\sql\002_SeedMasterData.sql -b
```

ダミーデータ投入には次のSSMS手順を使用します。`sqlcmd`や拡張プロパティ追加の事前コマンドは不要です。

### 既に管理者を作成した開発用DBへダミーデータを追加する

先にPortalの開発専用`add-test-user`コマンドで有効な一般ユーザーを一名以上作成してください。既存DBにテーブルがある場合、`001_CreateTables.sql`は再実行しません。`002_SeedMasterData.sql`は必要なマスタが未投入の場合に適用します。

1. SSMSで開発専用SQL Serverへ接続し、ツールバーのDB選択で対象の開発DBを選びます。接続先サーバー名とDB名を確認してください。
2. `900_SeedDummyData.sql`をSSMSで開き、先頭の`@ExpectedDevelopmentDatabase = N'__開発DB名を入力__'`を対象DB名へ書き換えます。SQLCMDモードは不要です。
3. ファイル全体を実行します。接続中のDB名と記入した名前が異なる場合、および既存の`EnvironmentName`拡張プロパティが`DEVELOPMENT`以外の場合は、データ変更前に停止します。

同じDBへダミーSQLを再投入せず、必要なら新しい開発専用DBを用意します。スクリプトは`SalesSupport.AllowDummyData`拡張プロパティを要求・追加しません。既存DBにこのプロパティが残っていても投入条件には使用しません。

問い合わせ番号は`portal.AllocateInquiryId`を専用の短いトランザクションとして呼び出します。番号確保後の問い合わせ保存が失敗しても、確保済み番号は再利用しません。

## 初期構築SQLの整合修正

001の本文CHECKは`DATALENGTH <= 20000`で末尾空白も含めて判定し、002のTOOL_STATUS色はDB設計の初期色に合わせた。既存DBへは001を再実行せず、制約の変更には別途ALTER移行を用意する。002を再実行すると該当コードの色が更新される。

- 公開状態NULL拒否、業務日付のYYYY-MM-DD形式、数字3組のバージョン、拡張子の小文字・空値検証を修正。
- 監査はUSER_NAME、登録時0・作成更新日時一致、更新時の作成監査保持、対象トリガー自身だけの再入防止に統一。
- 未合意のカテゴリ名称・コード表示順の一意制約を除去。FAQカテゴリ内表示順等の既定制約は維持。
- 002のコード・設定・ポリシーは確定仕様と一致する。TOOL_STATUS色は設計に合わせて変更し、再投入を検証対象とする。
- 空の専用DBへ001・002を適用後、`tests/VerifySchema.sql`で制約・監査を検証できる。テスト変更はロールバックする。
- これは新規構築SQLの修正であり、既存DBへのALTER移行ではない。001を既存DBへ再実行しない。既存業務DBへの適用は行っていない。
- 追加確定事項を反映：ToolFiles.UploadedByUserId（AspNetUsersへの外部キー）・UploadedAt必須列、SITE＋USER_IMPORT（.tsv、1,000,000バイト）、バージョン各組0～99・先頭ゼロ禁止。TSV100件上限はPortalで検証する。
- 既存DBへの移行ではアップロード情報の実値確認、不適合バージョンの個別修正が必要。新規構築SQLで既存DBを更新しない。
- `log`スキーマ3表へ`OccurredAt`の非クラスター化インデックスを追加。ログ検索は期間で絞るため、件数増加時の全件スキャンを避ける。
