using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SalesSupport.Common.Authentication;
using SalesSupport.Common.Configuration;
using SalesSupport.Common.Contracts;
using SalesSupport.Common.DateTime;
using SalesSupport.Common.Entities.Identity;
using SalesSupport.Common.Logging;
using SalesSupport.Common.Mail;
using SalesSupport.Portal.Web.Controllers;
using SalesSupport.Portal.Web.Data;
using SalesSupport.Portal.Web.Entities;
using SalesSupport.Portal.Web.Mail;
using SalesSupport.Portal.Web.Models;
using SalesSupport.Portal.Web.Services;
using Xunit;

namespace SalesSupport.Portal.Tests;

/// <summary>ロール通知許可と本人設定、送信時の再確認、直接画面・APIの拒否を確認します。</summary>
public sealed class NotificationPolicyTests
{
    /// <summary>ロール許可と本人設定の両方を満たすユーザーだけがお知らせ宛先になります。</summary>
    [Theory]
    [InlineData("SYSTEM", "ADMIN", true, true, true, true, 1)]
    [InlineData("SYSTEM", "ADMIN", false, true, true, true, 0)]
    [InlineData("SYSTEM", "A", true, false, true, true, 0)]
    [InlineData("SYSTEM", "B", true, true, false, true, 0)]
    [InlineData("SYSTEM", "C", true, true, true, true, 1)]
    [InlineData("SYSTEM", "D", false, true, true, true, 0)]
    [InlineData("SYSTEM", "UNDEFINED", true, true, true, true, 0)]
    [InlineData("TOOL", "A", true, true, true, true, 1)]
    [InlineData("TOOL", "A", true, true, true, false, 0)]
    [InlineData("TOOL", "D", false, true, true, true, 0)]
    [InlineData("TOOL", "ADMIN", true, true, true, true, 1)]
    public async Task NoticeRecipientsRequireAllConditions(string noticeType, string roleCode, bool roleAllowed,
        bool personalEnabled, bool active, bool assigned, int expected)
    {
        await using var db = CreateContext();
        var user = await SeedAsync(db, roleCode, roleAllowed, personalEnabled, active);
        db.Tools.Add(new Tool { ToolId = "T001", ToolName = "試験ツール", ToolType = "WEB", Status = "HIDDEN", OwnerUserId = user.Id });
        db.UserToolFavorites.Add(new UserToolFavorite { UserId = user.Id, ToolId = "T001" });
        if (assigned && roleCode != "ADMIN") db.ToolRoles.Add(new ToolRole { ToolId = "T001", RoleCode = roleCode });
        var toolId = noticeType == "TOOL" ? "T001" : null;
        db.Notices.Add(new Notice { NoticeId = 1, NoticeType = noticeType, ToolId = toolId, Title = "通知", Content = "本文" });
        await db.SaveChangesAsync();
        var mail = new CaptureMail();
        var service = CreateNotices(db, mail);
        var plan = Assert.IsType<NoticeSendPlan>(await service.PrepareSendAsync(user.Id, noticeType, toolId, [1]));
        Assert.Equal(expected, plan.RecipientCount);
        await service.SendAsync(user.Id, plan.ConfirmationId);
        Assert.Equal(expected, mail.Requests.Count);
        if (expected == 1) Assert.Equal([user.Email!], mail.Requests[0].Bcc);
    }

    /// <summary>確認後にロール通知許可を取り消すと、SMTPを呼ばず再確認を求めます。</summary>
    [Fact]
    public async Task RoleFlagIsRecheckedBeforeSending()
    {
        await using var db = CreateContext();
        var user = await SeedAsync(db, "A", true, true);
        db.Notices.Add(new Notice { NoticeId = 1, NoticeType = "SYSTEM", Title = "通知", Content = "本文" });
        await db.SaveChangesAsync();
        var mail = new CaptureMail();
        var service = CreateNotices(db, mail);
        var plan = Assert.IsType<NoticeSendPlan>(await service.PrepareSendAsync(user.Id, "SYSTEM", null, [1]));
        db.Roles.Single().NoticeMailEnabled = false;
        await db.SaveChangesAsync();
        Assert.Equal(NoticeSendOutcome.Conflict, await service.SendAsync(user.Id, plan.ConfirmationId));
        Assert.Empty(mail.Requests);
    }

    /// <summary>通知許可OFFでは本人設定がONでも画面GET/POST・API GET/PUT・Service保存を拒否します。</summary>
    [Theory]
    [InlineData("D")]
    [InlineData("ADMIN")]
    [InlineData("UNDEFINED")]
    public async Task DirectPreferencesAreDeniedAndValuesPreserved(string roleCode)
    {
        await using var db = CreateContext();
        var user = await SeedAsync(db, roleCode, false, true);
        var current = new CurrentUserAccessor();
        current.SetVerified(new CurrentUser(user.Id, user.DisplayName, roleCode));
        var service = new PreferenceService(db, new SilentActivity());
        var page = new PreferencesController(service, current);
        var api = new UsersApiController(current, service);
        Assert.IsType<ForbidResult>(await page.Index(default(CancellationToken)));
        Assert.IsType<ForbidResult>(await page.Index(new PreferenceInput(), default));
        Assert.IsType<ForbidResult>((await api.GetPreferences(default)).Result);
        Assert.IsType<ForbidResult>((await api.PutPreferences(new(false, false, 0), default)).Result);
        Assert.Equal(PreferenceOutcome.Denied, (await service.SaveAsync(user.Id, new(false, false, 0))).Outcome);
        Assert.True(db.UserPreferences.Single().SystemNoticeMailEnabled);
        Assert.True(db.UserPreferences.Single().FavoriteToolNoticeMailEnabled);
    }

    /// <summary>本人設定が初期OFFでも通知許可ONのロールは個人設定を利用できます。</summary>
    [Fact]
    public async Task PersonalOffDoesNotHideSettingsAndRoleChangesAreImmediate()
    {
        await using var db = CreateContext();
        var user = await SeedAsync(db, "A", true, false);
        var service = new PreferenceService(db, new SilentActivity());
        Assert.True(await service.IsAllowedAsync(user.Id));
        var value = Assert.IsType<UserPreferencesDto>(await service.GetAsync(user.Id));
        Assert.False(value.SystemNoticeMailEnabled);
        Assert.False(value.FavoriteToolNoticeMailEnabled);
        db.Roles.Single().NoticeMailEnabled = false;
        await db.SaveChangesAsync();
        Assert.False(await service.IsAllowedAsync(user.Id));
        db.Roles.Single().NoticeMailEnabled = true;
        await db.SaveChangesAsync();
        Assert.True(await service.IsAllowedAsync(user.Id));
        Assert.False((await service.GetAsync(user.Id))!.SystemNoticeMailEnabled);
    }

    /// <summary>資格情報の連携が通知許可OFFでも問い合わせメールの契約を変えないことを確認します。</summary>
    [Fact]
    public void InquiryAndUserEditingDoNotAcceptRoleNotificationOrExternalFields()
    {
        var properties = typeof(SalesSupport.Portal.Web.Areas.Admin.Models.UserEditInput).GetProperties().Select(x => x.Name);
        Assert.DoesNotContain("LoginId", properties);
        Assert.DoesNotContain("Email", properties);
        Assert.DoesNotContain("DisplayName", properties);
        Assert.DoesNotContain("Password", properties);
        Assert.DoesNotContain("NoticeMailEnabled", properties);
        Assert.DoesNotContain(typeof(InquiriesController).GetConstructors().SelectMany(x => x.GetParameters()),
            x => x.ParameterType == typeof(IPreferenceService));
    }

    /// <summary>SQLやSMTPへ接続しない使い捨てのEFデータベースを作成します。</summary>
    private static PortalDbContext CreateContext() => new(new DbContextOptionsBuilder<PortalDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    /// <summary>ロールの有無と本人の通知値を試験条件に従って投入します。</summary>
    private static async Task<ApplicationUser> SeedAsync(PortalDbContext db, string roleCode, bool allowed, bool personal, bool active = true)
    {
        if (roleCode != "UNDEFINED") db.Roles.Add(new Role { RoleCode = roleCode, RoleName = roleCode, NoticeMailEnabled = allowed });
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = "staff001", NormalizedUserName = "STAFF001",
            Email = "staff@example.invalid", NormalizedEmail = "STAFF@EXAMPLE.INVALID", DisplayName = "試験利用者",
            RoleCode = roleCode, IsActive = active, SecurityStamp = "test", ConcurrencyStamp = "test" };
        db.Users.Add(user);
        db.UserPreferences.Add(new UserPreference { UserId = user.Id, SystemNoticeMailEnabled = personal, FavoriteToolNoticeMailEnabled = personal });
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>宛先選定を実行し、SMTPだけを記録用の境界へ置き換えます。</summary>
    private static NoticeService CreateNotices(PortalDbContext db, CaptureMail mail)
    {
        var clock = new ApplicationClock(TimeProvider.System);
        var templates = new MailTemplateOptions();
        NoticeMailTemplates.AddDefaults(templates);
        return new(db, clock, mail, new MailTemplateRenderer(Options.Create(templates),
            Options.Create(new CommonOptions { ApplicationName = "試験" })), new ConfirmationStore(clock), new SilentActivity());
    }

    /// <summary>外部通信せず宛先を記録し、SQL Serverの送信成功日時更新へ進めません。</summary>
    private sealed class CaptureMail : IMailSender
    {
        public List<MailRequest> Requests { get; } = [];

        /// <summary>組み立てたメールを保持し、送信失敗の固定結果を返します。</summary>
        public Task<MailSendResult> SendAsync(MailRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MailSendResult(DeliveryOutcome.Failed));
        }
    }

    /// <summary>検証で生成した操作ログを外部へ保存しません。</summary>
    private sealed class SilentActivity : IActivityLogger
    {
        /// <summary>ログの保存を省略して業務処理の結果を維持します。</summary>
        public Task<LogWriteResult> WriteAsync(ActivityEvent entry, CancellationToken ct = default) => Task.FromResult(LogWriteResult.Skipped);
    }
}
