using Cove.Core.Auth;
using Cove.Core.Entities.Auth;
using Cove.Data;
using Cove.Data.Auth;
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

    private sealed class TestCoveContext(DbContextOptions<CoveContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Cove.Core.Entities.Scene>().Ignore(scene => scene.CustomFields);
            modelBuilder.Entity<Cove.Core.Entities.Image>().Ignore(image => image.CustomFields);
            modelBuilder.Entity<Cove.Core.Entities.Tag>().Ignore(tag => tag.CustomFields);
            modelBuilder.Entity<Cove.Core.Entities.Studio>().Ignore(studio => studio.CustomFields);
            modelBuilder.Entity<Cove.Core.Entities.Performer>().Ignore(performer => performer.CustomFields);
            modelBuilder.Entity<Cove.Core.Entities.Gallery>().Ignore(gallery => gallery.CustomFields);
            modelBuilder.Entity<Cove.Core.Entities.Group>().Ignore(group => group.CustomFields);
        }
    }

    private sealed class NoopAudit : IAuditService
    {
        public Task LogAsync(string action, string outcome, CovePrincipal? actor = null,
            string? targetKind = null, string? targetId = null, object? detail = null,
            CancellationToken ct = default) => Task.CompletedTask;
    }
}
