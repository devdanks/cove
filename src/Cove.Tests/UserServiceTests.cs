using Cove.Core.Auth;
using Cove.Core.Entities.Auth;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Auth;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cove.Tests;

/// <summary>
/// Integration tests for UserService against an in-memory CoveContext.
/// Focused on lockout, password verification, and audit emission behavior.
/// </summary>
public class UserServiceTests
{
    private static CoveContext NewDb(string name = "users")
    {
        var options = new DbContextOptionsBuilder<CoveContext>()
            .UseInMemoryDatabase($"{name}-{Guid.NewGuid():N}")
            .Options;
        return new TestCoveContext(options);
    }

    [Fact]
    public async Task RecordLoginFailure_locks_account_after_threshold()
    {
        await using var db = NewDb("lockout");
        db.Users.Add(new User
        {
            Username = "bob",
            DisplayName = "Bob",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("correct-horse-battery-staple", workFactor: 4),
            PasswordAlgo = "bcrypt",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var userId = (await db.Users.AsNoTracking().FirstAsync(u => u.Username == "bob")).Id;

        var svc = new UserService(db, new NoopAudit(), NullLogger<UserService>.Instance);

        for (var i = 0; i < UserService.MaxFailedLogins - 1; i++)
            await svc.RecordLoginFailureAsync(userId);

        var midway = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.False(midway.IsLocked);
        Assert.Equal(UserService.MaxFailedLogins - 1, midway.FailedLoginCount);

        await svc.RecordLoginFailureAsync(userId);

        var locked = await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId);
        Assert.True(locked.IsLocked);
        Assert.Equal(UserService.MaxFailedLogins, locked.FailedLoginCount);
        Assert.NotNull(locked.LockedUntil);
    }

    [Fact]
    public async Task VerifyPassword_returns_false_for_wrong_password()
    {
        await using var db = NewDb("verify");
        db.Users.Add(new User
        {
            Username = "alice",
            DisplayName = "Alice",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("hunter2", workFactor: 4),
            PasswordAlgo = "bcrypt",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var userId = (await db.Users.AsNoTracking().FirstAsync()).Id;

        var svc = new UserService(db, new NoopAudit(), NullLogger<UserService>.Instance);
        Assert.True(await svc.VerifyPasswordAsync(userId, "hunter2"));
        Assert.False(await svc.VerifyPasswordAsync(userId, "wrong"));
        Assert.False(await svc.VerifyPasswordAsync(99999, "anything"));
    }

    [Fact]
    public void Username_validation_rejects_empty_and_too_long()
    {
        Assert.Throws<InvalidOperationException>(() => UserService.Validation.Username(""));
        Assert.Throws<InvalidOperationException>(() => UserService.Validation.Username(new string('a', 200)));
        UserService.Validation.Username("good_name");
    }

    [Fact]
    public void Password_validation_rejects_short()
    {
        Assert.Throws<InvalidOperationException>(() => UserService.Validation.Password("short"));
        UserService.Validation.Password("longenough123");
    }

    [Fact]
    public async Task BootstrapOwner_creates_single_owner_account()
    {
        await using var db = NewDb("bootstrap-owner");
        var svc = new UserService(db, new NoopAudit(), NullLogger<UserService>.Instance);

        Assert.False(await svc.OwnerExistsAsync());

        var owner = await svc.BootstrapOwnerAsync("owner", "longenough123", null);

        Assert.True(owner.IsSystem);
        Assert.True(owner.HasPassword);
        Assert.Contains(BuiltinRoles.Owner, owner.Roles);
        Assert.True(await svc.OwnerExistsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.BootstrapOwnerAsync("other", "longenough123", null));
    }

    [Fact]
    public async Task Invite_can_set_initial_password_once()
    {
        await using var db = NewDb("invite-redeem");
        var svc = new UserService(db, new NoopAudit(), NullLogger<UserService>.Instance);
        var user = await svc.CreateAsync(new CreateUserRequest("invitee", null, DisplayName: "Invitee"), null);

        Assert.False(user.HasPassword);
        Assert.True(user.MustChangePassword);

        var invite = await svc.CreateInviteAsync(user.Id, "http://cove.local", null);
        Assert.Contains("/auth/redeem-invite?token=", invite.Url, StringComparison.Ordinal);

        var redeemed = await svc.RedeemInviteAsync(invite.Token, "newpassword123", null, null);

        Assert.True(redeemed.HasPassword);
        Assert.False(redeemed.MustChangePassword);
        Assert.True(await svc.VerifyPasswordAsync(user.Id, "newpassword123"));
        await Assert.ThrowsAsync<InviteTokenException>(() => svc.RedeemInviteAsync(invite.Token, "anotherpass123", null, null));
    }

    [Fact]
    public async Task Pending_invite_can_create_user_with_recipient_username()
    {
        await using var db = NewDb("pending-invite-redeem");
        var svc = new UserService(db, new NoopAudit(), NullLogger<UserService>.Instance);

        var invite = await svc.CreatePendingInviteAsync(new CreateInviteRequest(DisplayName: "Invited User", Email: "invitee@example.test"), "http://cove.local", null);
        var info = await svc.GetInviteInfoAsync(invite.Token);

        Assert.NotNull(info);
        Assert.True(info.UsernameRequired);
        Assert.Null(info.Username);

        await Assert.ThrowsAsync<InviteTokenException>(() => svc.RedeemInviteAsync(invite.Token, "newpassword123", null, null));

        var redeemed = await svc.RedeemInviteAsync(invite.Token, "newpassword123", "chosen-name", null);

        Assert.Equal("chosen-name", redeemed.Username);
        Assert.Equal("Invited User", redeemed.DisplayName);
        Assert.Equal("invitee@example.test", redeemed.Email);
        Assert.True(redeemed.HasPassword);
        Assert.True(await svc.VerifyPasswordAsync(redeemed.Id, "newpassword123"));
    }

    [Fact]
    public async Task Setup_token_bootstraps_owner_once()
    {
        await using var db = NewDb("setup-token");
        var svc = new UserService(db, new NoopAudit(), NullLogger<UserService>.Instance);

        var setup = await svc.CreateSetupTokenAsync(null);
        Assert.True(await svc.HasSetupTokenAsync());

        var owner = await svc.RedeemSetupTokenAsync(setup.Token, "ownerpass123", "owner", null);

        Assert.Equal("owner", owner.Username);
        Assert.Contains(BuiltinRoles.Owner, owner.Roles);
        Assert.False(await svc.HasSetupTokenAsync());
        await Assert.ThrowsAsync<InviteTokenException>(() => svc.RedeemSetupTokenAsync(setup.Token, "ownerpass123", "owner", null));
    }

    [Fact]
    public async Task Issued_jwt_has_no_age_expiry_and_refresh_uses_configured_ttl()
    {
        await using var db = NewDb("token-age");
        var now = DateTime.UtcNow;
        db.Users.Add(new User
        {
            Id = 1,
            Username = "owner",
            PasswordHash = "hash",
            PasswordAlgo = "test",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.Roles.Add(new Role
        {
            Id = 1,
            Name = BuiltinRoles.Owner,
            Description = "Owner",
            IsBuiltin = true,
            IsSystem = true,
            Source = "core",
            CreatedAt = now,
            UpdatedAt = now,
        });
        db.RolePermissions.Add(new RolePermission { RoleId = 1, PermissionKey = Permissions.All });
        db.UserRoleAssignments.Add(new UserRoleAssignment { UserId = 1, RoleId = 1, GrantedAt = now });
        await db.SaveChangesAsync();

        var config = new CoveConfiguration { Auth = { JwtSecret = "test-secret-that-is-long-enough-for-hmac", RefreshTokenDays = 30 } };
        var tokens = new TokenService(db, config, new PermissionRegistry(), NullLogger<TokenService>.Instance);

        var pair = await tokens.IssueForUserAsync(1, "127.0.0.1", "test");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(pair.AccessToken);
        var principal = await tokens.ResolveAsync($"Bearer {pair.AccessToken}", "127.0.0.1", "test");

        Assert.DoesNotContain(jwt.Claims, claim => string.Equals(claim.Type, JwtRegisteredClaimNames.Exp, StringComparison.Ordinal));
        Assert.NotNull(principal);
        Assert.InRange(pair.RefreshExpires - DateTime.UtcNow, TimeSpan.FromDays(29), TimeSpan.FromDays(31));
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_malformed_bearer_token()
    {
        await using var db = NewDb("token-malformed");
        var config = new CoveConfiguration { Auth = { JwtSecret = "test-secret-that-is-long-enough-for-hmac", RefreshTokenDays = 30 } };
        var tokens = new TokenService(db, config, new PermissionRegistry(), NullLogger<TokenService>.Instance);

        var principal = await tokens.ResolveAsync("Bearer not-a-jwt", "127.0.0.1", "test");

        Assert.Null(principal);
    }

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }

    private sealed class NoopAudit : IAuditService
    {
        public Task LogAsync(string action, string outcome, CovePrincipal? actor = null,
            string? targetKind = null, string? targetId = null, object? detail = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
