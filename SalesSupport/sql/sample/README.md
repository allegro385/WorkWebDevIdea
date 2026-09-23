# 見積試算サンプルの開発専用SQL

`910_CreateSampleEstimate.sql`は、Portalの開発専用DBへ`dbo.SampleEstimateRecords`の1表、`SAMPLE-ESTIMATE`のツール行、`.tsv`・1 MiBのアップロード条件、ADMIN所有の見本案件2件を追加します。本番には適用しません。アプリ起動時にも自動適用しません。

## 適用前

1. 使い捨ての開発専用DBへ`../001_CreateTables.sql`と`../002_SeedMasterData.sql`を適用します。DB名・SQL Server・接続先を作業者が確認します。
2. Portalの`add-test-user`で有効なADMINを1名作成します。IdentityユーザーをSQLで直接作らないでください。
3. DB管理者が対象DBだけに拡張プロパティ`EnvironmentName=DEVELOPMENT`と`SalesSupport.AllowSampleData=1`を明示的に設定します。標識はSQLが作成しません。例：`EXEC sys.sp_addextendedproperty @name=N'EnvironmentName', @value=N'DEVELOPMENT'`と`EXEC sys.sp_addextendedproperty @name=N'SalesSupport.AllowSampleData', @value=N'1'`。既に存在する場合は追加せず、値と接続先を確認してください。
4. `910_CreateSampleEstimate.sql`の先頭の`@ExpectedDevelopmentDatabase`へ実際の開発DB名を書き、SSMSで接続中のDB名を再確認してからファイル全体を実行します。`sqlcmd`を使う場合はUTF-8の`-f 65001`と失敗時停止の`-b`を指定します。

期待DB名、環境区分、許可標識、Portal表、ADMINのいずれかが欠ければ変更前に停止します。同名のサンプル表・分類・ツール行があれば再実行を拒否します。途中で失敗した場合はトランザクションを戻し、原因を確認してください。既存データを上書きして再投入しないでください。

## 確認と清掃

管理者でPortalへログインし、限定公開のツールから`/tools/SAMPLE-ESTIMATE/app`を開きます。計算、TSV、一時ファイル削除、保存、検索・集計、編集、二重送信、同時更新の順に確認します。一般ユーザーへ公開状態を変更するのは、開発環境で入場・認可の確認が必要な場合だけです。テスト後は`PRIVATE`へ戻します。

サンプルの行には開発用の入力だけを使用します。検証終了時はこのサンプル専用の使い捨てDBを環境管理者の通常の破棄手順で廃棄するか、参照元（利用ログ、通知、問い合わせなど）がないことを確認したうえで、管理者が拡張子・ポリシー・ツール行・分類・サンプル表を手動削除してください（表の削除時に専用トリガーも削除されます）。共有開発DBを無条件でDROPしないでください。SQLの適用・清掃日時、接続先、結果は`作業記録/プロジェクト操作履歴.md`へ記録します。
