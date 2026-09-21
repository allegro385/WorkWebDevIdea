namespace SalesSupport.Portal.Web.Models;

/// <summary>ツール一覧の1行分の表示情報です。</summary>
/// <param name="ToolId">ツールの識別子です。</param>
/// <param name="CategoryName">ツールカテゴリの表示名です。</param>
/// <param name="ToolName">ツール名です。</param>
/// <param name="Summary">概要です。省略せず全文を折り返して表示します。</param>
/// <param name="Version">現在のバージョンです。未選択・履歴0件では画面表示だけを補完した値です。</param>
/// <param name="UpdatedOn">現在のバージョンのリリース日です。未選択・履歴0件ではnullです。</param>
/// <param name="Status">TOOL_STATUSのコード値です。表示名と色はCodeMasterから取得します。</param>
/// <param name="IsFavorite">ログイン中の利用者がお気に入り登録しているかどうかです。</param>
/// <param name="CanOpenDetail">詳細画面へのリンクを表示してよいかどうかです。認可の代替にはしません。</param>
/// <param name="IsLimited">限定公開として薄いグレーで表示するかどうかです。</param>
public sealed record ToolListItem(string ToolId, string CategoryName, string ToolName, string? Summary,
    string Version, DateOnly? UpdatedOn, string Status, bool IsFavorite, bool CanOpenDetail, bool IsLimited);

/// <summary>P003 ツール一覧・P008 お気に入りツールの表示情報です。</summary>
/// <param name="FavoritesOnly">お気に入りツールタブを選択しているかどうかです。</param>
/// <param name="AllCount">全ツールタブの件数です。</param>
/// <param name="FavoriteCount">お気に入りツールタブの件数です。</param>
/// <param name="Tools">選択中のタブに表示する行です。</param>
public sealed record ToolListViewModel(bool FavoritesOnly, int AllCount, int FavoriteCount, IReadOnlyList<ToolListItem> Tools);

/// <summary>ツール詳細のバージョン更新情報1行です。</summary>
/// <param name="Version">バージョン番号です。</param>
/// <param name="ReleasedAt">バージョンのリリース日です。</param>
/// <param name="ChangeDescription">更新内容です。</param>
/// <param name="IsCurrent">現在のバージョンとして選択されている行かどうかです。</param>
public sealed record ToolVersionView(string Version, DateOnly ReleasedAt, string ChangeDescription, bool IsCurrent);

/// <summary>ツール詳細から取得できるファイル1件です。物理パスは含めません。</summary>
/// <param name="FileId">取得URLに使用するファイル識別子です。</param>
/// <param name="DisplayName">登録された表示名です。</param>
/// <param name="OriginalFileName">配布・取得時のファイル名です。</param>
/// <param name="FileSizeBytes">ファイルの容量です。</param>
public sealed record ToolFileLink(int FileId, string DisplayName, string OriginalFileName, long FileSizeBytes);

/// <summary>P004 ツール詳細の表示情報です。</summary>
/// <param name="ToolId">ツールの識別子です。</param>
/// <param name="CategoryName">ツールカテゴリの表示名です。</param>
/// <param name="ToolName">ツール名です。</param>
/// <param name="Summary">概要です。</param>
/// <param name="Remarks">利用者向けの備考です。</param>
/// <param name="ToolType">TOOL_TYPEのコード値です。</param>
/// <param name="Status">TOOL_STATUSのコード値です。</param>
/// <param name="Version">現在のバージョンです。未選択・履歴0件では画面表示だけを補完した値です。</param>
/// <param name="UpdatedOn">現在のバージョンのリリース日です。</param>
/// <param name="IsFavorite">ログイン中の利用者がお気に入り登録しているかどうかです。</param>
/// <param name="Notices">公開中のツールお知らせです。</param>
/// <param name="Versions">現在のバージョン以下の更新情報を番号の降順で並べた一覧です。</param>
/// <param name="HasWebLaunch">登録済みのWeb起動URLがあるかどうかです。</param>
/// <param name="AppFile">登録済みの配布アプリです。未登録ではnullです。</param>
/// <param name="References">登録済みの参考資料を表示順で並べた一覧です。</param>
public sealed record ToolDetailViewModel(string ToolId, string CategoryName, string ToolName, string? Summary, string? Remarks,
    string ToolType, string Status, string Version, DateOnly? UpdatedOn, bool IsFavorite,
    IReadOnlyList<PortalNoticeView> Notices, IReadOnlyList<ToolVersionView> Versions,
    bool HasWebLaunch, ToolFileLink? AppFile, IReadOnlyList<ToolFileLink> References)
{
    /// <summary>戻り先を一覧またはお気に入り一覧に切り替えるための指定です。</summary>
    public bool FromFavorites { get; init; }
}
