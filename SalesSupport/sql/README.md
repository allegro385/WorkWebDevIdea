# SalesSupport database scripts

SQL Server用の初期構築スクリプトです。対象データベースを作成した後、次の順序で実行します。

1. `001_CreateTables.sql` — スキーマ、全テーブル、制約、索引、監査トリガー、問い合わせ番号採番プロシージャ
2. `002_SeedMasterData.sql` — 初期ロール、固定コード、システム設定、アップロード制限、共通エラーコード
3. `900_SeedDummyData.sql` — 開発・画面確認用のダミー業務データ

見積試算サンプルの専用SQLは`sample/`に分離しています。`001`・`002`へ混ぜず、[専用の適用・清掃手順](sample/README.md)を確認してください。

## 実行上の注意

- すべてSQL Server向けです。接続先データベースを明示してから実行してください。
- `001_CreateTables.sql`は空のデータベース専用です。対象テーブルが一つでも存在する場合は停止します。
- `002_SeedMasterData.sql`は同じキーの行を更新し、不足行を追加するため再実行できます。運用で追加した行は削除しません。
- `900_SeedDummyData.sql`は本番投入禁止です。スクリプト先頭の`@ExpectedDevelopmentDatabase`を実際の開発DB名へ書き換え、接続中のDB名との一致を確認してから実行してください。DBに`EnvironmentName`拡張プロパティがある場合は`DEVELOPMENT`以外を拒否します。DB名確認は運用上の確認であり、本番の自動判別ではありません。
- ダミー投入は既存データを削除しません。ダミーID・名称の衝突時は停止します。再投入には新しい開発専用DBを用意してください。
- ダミースクリプトはIdentityユーザーを直接作成しません。あらかじめアプリケーションのUserManager経由で、有効な`ADMIN`ユーザーとロール`A`ユーザーを一名以上登録してください。
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

## 既存DBのロール移行

`001_CreateTables.sql`は既存DBへ再実行しません。保守時間にアプリへの新規要求を止め、接続先とバックアップを確認した上で次の順に進めます。移行前に使い捨ての開発専用DBでSQLとコマンドを検証してください。

1. 旧スキーマの既存DBへ`003_MigrateRoleToolSchema.sql`を一度だけ適用する。Rolesに一時的な`USER`を残し、既存ツールすべてにAを割り当てる。ToolCategories.SortOrderを削除する。
2. 新版Portalを配置し、同じDB設定で`dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web -- migrate-user-roles`を実行する。全既存USERをUserManager経由でAへ変更し、SecurityStampを更新する。失敗時は原因を修正して再実行する。
3. `SELECT COUNT(*) FROM portal.AspNetUsers WHERE RoleCode = 'USER'`が0であることを確認し、`004_CompleteRoleMigration.sql`を適用する。旧ロールを削除する。
4. アプリへの要求を再開し、Aユーザーの一覧・直接URL、ADMINの一覧、B～Dの未割当てツール拒否を確認する。

新規DBでは`001`、`002`を適用し、`003`と`004`は実行しません。ロール名称とツール割当てはDB運用で管理します。`ADMIN`をToolRolesへ登録せず、一般ロールだけを関連付けます。

## 制約・監査の検証

テーブル・列・制約は[DB設計](../../設計書/04_DB設計.md)を正とします。空の開発専用DBへ`001`・`002`を適用後、次のSQLを実行します。検証中のデータ変更はロールバックします。

- `tests/VerifySchema.sql`：公開状態のNULL拒否、業務日付・バージョン・拡張子の形式、本文長、ツール状態色、監査列とトリガー、カテゴリ名の重複許可、初期マスタ、ロール・ツールのスキーマ。
- `tests/VerifyUploadAndVersion.sql`：アップロード情報の必須列と外部キー、TSVポリシー、バージョンの範囲・先頭ゼロ拒否。

本文の長さは`DATALENGTH <= 20000`で末尾空白も含めて判定します。監査トリガーは`USER_NAME()`を記録し、登録時の更新回数を0、作成・更新日時を同一にします。更新時は作成監査列を保持し、対象トリガー自身だけの再入を防ぎます。

既存DBの制約変更には別途ALTER移行が必要です。アップロード情報の実値と不適合バージョンを確認し、新規構築SQLで既存DBを更新しないでください。`002`の再実行ではコード・設定・ポリシーの初期値が更新されるため、運用で変更した値を事前に確認します。
