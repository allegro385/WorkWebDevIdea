# 営業支援ポータル：リポジトリ指示

## プロジェクトの目的

既存のデスクトップツールと新規 Web ツールを、単一の社内ポータルから利用できるようにする。ポータルは認証、ユーザー・ツール・お知らせ・通知・問い合わせ・ログ・共通 UI を担い、各 Web ツールは同じ IIS サイト配下へ独立したアプリケーションとして配置する。

初期想定は登録ユーザー約 200 名、同時利用約 5 名、ツール約 20 件。複雑な分散構成より、単純で保守しやすい実装を優先する。

## 必要な資料だけを読む

変更対象に応じて次の資料を参照する。すべてを作業前に読む必要はない。

- 要件・対象範囲：`設計書/01_要件定義.md`
- 画面項目、遷移、状態別可否、メール：`設計書/02_画面・機能設計.md`
- 構成、責任分界、認証、共通ライブラリ、開発標準：`設計書/03_システム構成・開発標準.md`
- テーブル、監査列、コード、設定、アップロード制限：`設計書/04_DB設計.md`
- IIS 配置、リリース、運用時の直接更新、定期処理：`設計書/05_導入・運用.md`
- 未確定事項：`設計書/06_検討事項.md`。ここにある事項を実装だけで恒久仕様にしない。
- 認証・Cookie・トークン・試行制限・Data Protection の判断根拠：`設計書/09_セキュリティ参考資料.md`
- 現在の実装・実行方法：対象モジュールの `README.md`、`IMPLEMENTATION.md`、`sql/README.md`
- 画面イメージファイル`\設計書\images`

設計書と実装が食い違う場合は、影響を示してから解消する。依頼範囲を超える仕様変更は推測で進めない。

## 実装の境界

- 技術基盤は .NET 10 LTS、ASP.NET Core 10 MVC、C#、Razor、Bootstrap、EF Core 10、SQL Server、IIS。
- 基本の依存方向は `Controller -> Service -> EF Core DbContext -> SQL Server`。通常処理のための汎用 Repository 層や SQL 実行抽象層は追加しない。
- 検索・結合・集計と更新は EF Core を基本とする。EF Core で表現できない、または具体的な性能問題がある場合に限り、パラメーター化 SQL、ビュー、DB 関数を対象機能内で検討する。
- DbContext とサービスは DI で管理し、ユーザー間や並列処理で同じ DbContext インスタンスを共有しない。
- ツール固有の Entity と DbContext は各ツールに置き、Common に全ツールのテーブル定義を集約しない。
- 共通機能は `SalesSupport.Common.*` を再利用する。独自実装の前に `src/SalesSupport.Common/README.md` と対象パッケージの契約を確認する。
- 初期対象外の機能を便宜的に追加しない。例：ツール単位の利用者権限、キーワード検索、管理画面からのアプリ配備、成果物の恒久保存、バックアップ、CI/CD、ダッシュボードグラフ。

## セキュリティ上の不変条件

- ASP.NET Core Identity と Cookie 認証を使い、独自のパスワードハッシュやトークン暗号を実装しない。
- `ApplicationUser` は `IdentityUser<Guid>` を継承する。メールアドレスをログイン ID と UserName に使い、表示名は `DisplayName`、権限は `RoleCode` の `USER` / `ADMIN` で管理する。
- `RoleCode` または `IsActive` の変更時は SecurityStamp を更新する。
- 保護対象リクエストでは IsActive、RoleCode、SecurityStamp、サイト公開状態、ツール状態を再確認する。Cookie の存在、Referer、過去の画面操作だけで認可しない。
- 設定欠落、未定義コード、状態やポリシーの取得失敗時は許可側へ倒さない。
- パスワード、ハッシュ、認証・再設定トークン、接続文字列、Data Protection キーなどの秘密情報を、画面・メール・ログ・ソースへ出さない。
- GET で状態を変更しない。更新要求には ASP.NET Core 標準の CSRF 対策を使う。
- Identity テーブルは UserManager などの Identity API 経由で更新する。

## ファイル・ログ・外部通信

- ファイル操作には `SalesSupport.Common.FileStorage` を使う。保存先は Web ルートと配置先の外に置き、任意の物理パスやパストラバーサルを許可しない。
- アップロード条件は `FileUploadPolicies` / `FileUploadPolicyExtensions` から取得し、有効な条件を得られなければ拒否する。
- 問い合わせ添付と Web ツール入力は一時保存とし、処理結果や帳票内容を恒久保存しない。
- `ToolUsageLogs`、`UserAccessLogs`、`SystemErrorLogs` を使い分ける。ログ失敗で本処理を失敗させず、ログ失敗を再帰的に記録しない。
- メールや副作用のある HTTP 処理を自動再試行しない。結果を呼び出し元へ返し、業務フロー側で扱う。
- CSV / TSV 出力では改行・タブの整形と数式インジェクション対策を適用する。

## 検証と完了条件

変更範囲に合う最小限の検証から始め、失敗が今回の変更に起因する場合は修正して再実行する。ローカル検証は使い捨てデータで、本番環境へ接続しないため、個別の確認を待たずに実行してよい。

- ポータル：`./scripts/Test-Portal.ps1`
- Common：`./scripts/Build-Common.ps1`
- 起動確認：`./scripts/Start-Portal.ps1`
- HTTPS ログイン確認：`dotnet run --project src/SalesSupportPortal/SalesSupportPortal.Web --launch-profile https`

Common の配布済みパッケージを同じ版数で上書きしない。変更時は `src/SalesSupport.Common/Directory.Build.props` と利用側の PackageReference を同時に更新する。

依頼された変更を実装し、影響範囲の検証を通し、残る制約や未検証事項を報告した時点を完了とする。実 DB、実 SMTP、SQL Server の行ロック、共有 Cookie の複数アプリ往復は、明示的な環境と権限がない限り未検証事項として扱う。

## 生成物

`bin/`、`obj/`、`.local/`、`artifacts/`、生成済みパッケージ、チェックログは生成物である。依頼に必要な場合を除き、手作業で編集しない。`Portal:Preview:*` とダミーデータはローカル確認専用であり、本番の認証・認可・公開状態の代替にしない。
