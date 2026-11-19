using SalesSupport.Common.Authentication;
using SalesSupport.Common.Contracts;
using Xunit;

namespace SalesSupport.Common.Tests;

/// <summary>本人向けデータの所有者を決められる、ログインした利用者かどうかの判定を確認します。</summary>
public sealed class CurrentUserIdentificationTests
{
    /// <summary>共有Cookieで確認したログイン利用者だけを本人として扱います。</summary>
    [Fact]
    public void SignedInUserIsIdentified()
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(Guid.NewGuid(), "試験太郎", "A"));

        Assert.True(current.HasIdentifiedUser());
    }

    /// <summary>匿名モードの固定共通ユーザーはAspNetUsersに存在しないため、本人として扱いません。</summary>
    [Fact]
    public void AnonymousSiteUserIsNotIdentified()
    {
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(AnonymousSiteUser.UserId, AnonymousSiteUser.DisplayName, AnonymousSiteUser.RoleCode));

        Assert.False(current.HasIdentifiedUser());
    }

    /// <summary>単独開発などで利用者がいない場合は本人として扱いません。</summary>
    [Fact]
    public void MissingUserIsNotIdentified() => Assert.False(new CurrentUserAccessor().HasIdentifiedUser());
}
