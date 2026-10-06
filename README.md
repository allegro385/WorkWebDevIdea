# 営業支援ポータル

既存のデスクトップツールとWebツールを一つの社内ポータルから利用するためのシステムです。.NET 10、ASP.NET Core MVC、EF Core、SQL Serverを使用し、IIS上へ配置します。

## 資料と構成

- [設計資料一覧](設計書/README.md)：要件、画面、共通基盤、DB、導入・運用、未確定事項。
- [Portal](SalesSupport/src/Portal/SalesSupport.Portal.Web/README.md)：認証、ツール一覧、お知らせ、問い合わせ、管理画面、起動設定。
- [共通設定](SalesSupport/config/README.md)：Portalと各Webツールが使用する共通設定ファイル。
- [SQL](SalesSupport/sql/README.md)：新規DB構築、既存DB移行、開発用データ、検証SQL。
- [テンプレート](SalesSupport/src/Template/SalesSupport.Template.Web/README.md)・[ツール生成手順](SalesSupport/scripts/README.md)：ToolIdから新しいWebツールを作成する手順。
- [サンプル見積ツール](SalesSupport/src/SampleEstimate/SalesSupport.SampleEstimate.Web/README.md)：入力・出力・EF Core操作の実演と機能追加ガイド。
- [画面モック](設計書/screens/index.html)：静的な画面構成と操作イメージ。
- [プロジェクト操作履歴](作業記録/プロジェクト操作履歴.md)：操作と検証の実行記録。

Commonは `SalesSupport/src/Common/SalesSupport.Common` に置き、Portalと各Webツールから `ProjectReference` で参照します。

## ローカル検証

リポジトリルートから実行します。SDKは `SalesSupport/global.json` の版を使用してください。

```powershell
dotnet build SalesSupport/SalesSupport.Common.slnx
dotnet test SalesSupport/SalesSupport.Common.slnx --no-build
dotnet build SalesSupport/SalesSupport.Portal.slnx
dotnet test SalesSupport/SalesSupport.Portal.slnx --no-build
dotnet build SalesSupport/template.slnx
dotnet test SalesSupport/template.slnx --no-build
dotnet build SalesSupport/SampleEstimate.slnx
dotnet test SalesSupport/SampleEstimate.slnx --no-build
```

起動には共通設定ファイル、開発用DB、鍵・保存領域が必要です。設定とHTTPS起動手順はPortalのREADMEを参照してください。

リリース前の判断・環境確認事項は [検討事項](設計書/06_検討事項.md) に従って確定し、実DB、SMTP、IIS、共有Cookieの複数アプリ往復を対象環境で検証します。
