namespace SalesSupport.Portal.Web.Models;

/// <summary>画面へ表示するFAQ1件です。</summary>
/// <param name="Number">画面の表示順に採番した番号です。DBへは保持しません。</param>
/// <param name="Question">質問です。プレーンテキストと改行だけを表示します。</param>
/// <param name="Answer">回答です。プレーンテキストと改行だけを表示します。</param>
public sealed record FaqEntry(int Number, string Question, string Answer);

/// <summary>FAQカテゴリ1件と、その中のFAQです。</summary>
/// <param name="CategoryName">カテゴリの表示名です。</param>
/// <param name="Items">カテゴリ内の表示順で並べたFAQです。</param>
public sealed record FaqCategoryView(string CategoryName, IReadOnlyList<FaqEntry> Items);

/// <summary>P005 利用マニュアル・FAQの表示情報です。</summary>
/// <param name="Categories">公開中のFAQをカテゴリごとにまとめた一覧です。</param>
public sealed record HelpViewModel(IReadOnlyList<FaqCategoryView> Categories);
