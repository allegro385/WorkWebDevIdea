---
name: sales-support-portal
description: 営業支援ポータルの機能追加、修正、レビュー、検証を行う。設計書、.NET 10 構成、Common ライブラリ、認証・ファイル・運用上の制約が関係する作業で使用する。
---

# 営業支援ポータル

このスキルは、営業支援ポータル固有の設計判断が必要な作業を支援する。一般的な .NET の質問や、このリポジトリに変更を加えない説明だけの依頼では読み込まない。

## 進め方

`AGENTS.md` の[開発方針・作業サイクル](AGENTS.md#開発方針作業サイクル)に従う。開発中のため後方互換・旧版からのデータ移行は行わず、互換層・移行SQL・将来のためだけの抽象化を追加しない。

1. `AGENTS.md` と対象モジュールの `README.md`、変更内容に対応する設計書だけを読む。
2. **資料を更新する。** 実装より先に要件・設計・SQL仕様・画面モック・手順を更新し、矛盾や古い情報を解消する。
3. **テストを書く。** 期待する振る舞いのテストを先に追加・修正し、意図した理由で失敗することを確認する。
4. **最小実装する。** 既存の Common 契約と実装パターンを確認し、テストを通す最小限の変更で実装する。
5. **最終確認する。** 不要コード・不要テスト・資料矛盾を整理し、影響範囲の検証を再実行する。操作履歴と未検証事項を更新して報告まで行い完了とする。

## 資料の選び方

- 要件や対象範囲を変える：`設計書/01_要件定義.md`
- 画面、入力制限、遷移、状態別可否、メールを変える：`設計書/02_画面・機能設計.md`
- アーキテクチャ、認証、Common、ログ、ファイル、開発標準を変える：`設計書/03_システム構成・開発標準.md`
- Entity、DbContext、SQL、コード値、設定を変える：`設計書/04_DB設計.md` と `SalesSupport/sql/README.md`
- 配置、リリース、運用、定期処理を変える：`設計書/05_導入・運用.md`
- 仕様が未確定、または複数の解釈がある：`設計書/06_検討事項.md`
- Cookie、トークン、試行制限、Data Protection の判断が必要：`設計書/09_セキュリティ参考資料.md`
- Common の契約・DI・マッピングを変える：`設計書/10_Common詳細設計.md`
- Portal のサービス・保存単位・認証フローを変える：`設計書/11_Portal詳細設計.md`
- テンプレート・サンプル・生成スクリプトを変える：`設計書/12_サンプル・テンプレートツール設計方針.md`
- 開発サーバーの構築・配備手順を変える：`設計書/13_開発サーバー導入手順.md`

資料にない判断で恒久仕様を作らない。仮実装が必要なら、仮であることと解消条件を明示する。

## 実装判断

- 標準構成は `Controller -> Service -> EF Core DbContext -> SQL Server`。
- 通常の DB 処理に汎用 Repository や SQL 抽象層を追加しない。
- 共通処理は `SalesSupport.Common.*` を優先し、重複実装しない。
- ツール固有の Entity と DbContext は各ツール内に保つ。
- 認証・認可、秘密情報、ファイルパス、アップロード、ログでは `AGENTS.md` の不変条件を守る。
- 設計済みの範囲を勝手に簡略化せず、初期対象外の機能をついでに追加しない。

## 検証の選び方

リポジトリのルートから実行する。

```powershell
# ポータルのビルドと単体テスト
dotnet build SalesSupport/SalesSupport.Portal.slnx
dotnet test SalesSupport/SalesSupport.Portal.slnx --no-build

# Common のビルドと単体テスト
dotnet build SalesSupport/SalesSupport.Common.slnx
dotnet test SalesSupport/SalesSupport.Common.slnx --no-build

# ポータルのローカル起動
dotnet run --project SalesSupport/src/Portal/SalesSupport.Portal.Web --launch-profile https
```

小さな変更では関連する検証だけを実行する。認証フローや画面挙動を変えた場合は、必要に応じて HTTPS 起動後に対象画面も確認する。実 DB、実 SMTP、本番設定など外部状態を変更する検証は、依頼と権限が明確な場合だけ実行する。

Common はソースプロジェクトへの `ProjectReference` で共有する。変更時はPortalと影響する各ツールを検証し、配備時には対象アプリ全体を再発行する。テンプレートは `SalesSupport/template.slnx`、サンプルは `SalesSupport/SampleEstimate.slnx` でビルド・テストする。設定と起動手順は各アプリのREADME、SQLの適用・検証は `SalesSupport/sql/README.md` を参照する。
