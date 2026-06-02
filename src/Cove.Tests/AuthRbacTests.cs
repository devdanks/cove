using Cove.Core.Auth;

namespace Cove.Tests;

public class CovePrincipalTests
{
    [Fact]
    public void System_principal_has_wildcard()
    {
        var p = CovePrincipal.System();
        Assert.True(p.Has("anything.you.want"));
        Assert.True(p.Has("videos.delete.file"));
    }

    [Fact]
    public void Anonymous_principal_has_no_permissions()
    {
        var p = CovePrincipal.Anonymous();
        Assert.False(p.Has("videos.read"));
        Assert.Equal(PrincipalKind.Anonymous, p.Kind);
    }

    [Fact]
    public void User_with_resource_wildcard_has_all_actions_in_that_resource()
    {
        var p = new CovePrincipal
        {
            Kind = PrincipalKind.User, UserId = 5, Username = "alice",
            Roles = new HashSet<string> { "Custom" },
            Permissions = new HashSet<string> { "videos.*" },
        };
        Assert.True(p.Has("videos.read"));
        Assert.True(p.Has("videos.delete"));
        Assert.True(p.Has("videos.delete.file"));
        Assert.False(p.Has("performers.read"));
    }

    [Fact]
    public void User_with_action_wildcard_has_that_verb_on_all_resources()
    {
        var p = new CovePrincipal
        {
            Kind = PrincipalKind.User, UserId = 5, Username = "alice",
            Roles = new HashSet<string>(),
            Permissions = new HashSet<string> { "*.read" },
        };
        Assert.True(p.Has("videos.read"));
        Assert.True(p.Has("performers.read"));
        Assert.False(p.Has("videos.delete"));
    }

    [Fact]
    public void User_with_explicit_keys_only_matches_those_keys()
    {
        var p = new CovePrincipal
        {
            Kind = PrincipalKind.User, UserId = 5, Username = "alice",
            Roles = new HashSet<string>(),
            Permissions = new HashSet<string> { "videos.read", "performers.read" },
        };
        Assert.True(p.Has("videos.read"));
        Assert.True(p.Has("performers.read"));
        Assert.False(p.Has("videos.delete"));
        Assert.False(p.Has("studios.read"));
    }
}

public class PermissionRegistryTests
{
    [Fact]
    public void Bootstrap_registers_all_core_permissions()
    {
        var reg = new PermissionRegistry();
        var keys = reg.All.Select(p => p.Key).ToHashSet();
        Assert.Contains("videos.read", keys);
        Assert.Contains("users.write", keys);
        Assert.Contains("audit.read", keys);
        Assert.Contains("system.wipe", keys);
        Assert.Contains("system.shutdown", keys);
        Assert.Contains("aidata.clear", keys);
    }

    [Fact]
    public void Expand_resolves_implies_recursively()
    {
        var reg = new PermissionRegistry();
        // videos.delete.file implies videos.delete which implies videos.read.
        var expanded = reg.Expand(["videos.delete.file"]);
        Assert.Contains("videos.delete.file", expanded);
        Assert.Contains("videos.delete", expanded);
        Assert.Contains("videos.read", expanded);
        // videos.write is NOT implied by videos.delete.
        Assert.DoesNotContain("videos.write", expanded);
    }

    [Fact]
    public void RegisterExtensionPermissions_reports_unprefixed_keys()
    {
        var reg = new PermissionRegistry();
        var rejected = reg.RegisterExtensionPermissions("notif", new[]
        {
            new PermissionDefinition("totally.invalid", "Other", "x", false, null, "extension:notif"),
        });
        Assert.False(reg.IsKnown("totally.invalid"));
        var rejection = Assert.Single(rejected);
        Assert.Equal("notif", rejection.ExtensionId);
        Assert.Equal("totally.invalid", rejection.PermissionKey);
        Assert.Contains("notif.", rejection.Reason);
    }

    [Fact]
    public void RegisterExtensionPermissions_reports_wildcard()
    {
        var reg = new PermissionRegistry();
        var beforeCount = reg.All.Count;
        var rejected = reg.RegisterExtensionPermissions("notif", new[]
        {
            new PermissionDefinition("*", "Other", "x", false, null, "extension:notif"),
        });
        // "*" is a core meta-permission already; the extension cannot redefine it,
        // and no new keys should have been added.
        Assert.Equal(beforeCount, reg.All.Count);
        var rejection = Assert.Single(rejected);
        Assert.Equal("*", rejection.PermissionKey);
        Assert.Contains("wildcard", rejection.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RegisterExtensionPermissions_accepts_prefixed_keys()
    {
        var reg = new PermissionRegistry();
        var rejected = reg.RegisterExtensionPermissions("notif", new[]
        {
            new PermissionDefinition("notif.read", "Notif", "Read notifications", false, null, "extension:notif"),
            new PermissionDefinition("notif.write", "Notif", "Write notifications", false, ["notif.read"], "extension:notif"),
        });
        Assert.Empty(rejected);
        Assert.True(reg.IsKnown("notif.read"));
        Assert.True(reg.IsKnown("notif.write"));
        var expanded = reg.Expand(["notif.write"]);
        Assert.Contains("notif.read", expanded);
    }
}

