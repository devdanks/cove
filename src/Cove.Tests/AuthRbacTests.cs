using Cove.Core.Auth;

namespace Cove.Tests;

public class CovePrincipalTests
{
    [Fact]
    public void System_principal_has_wildcard()
    {
        var p = CovePrincipal.System();
        Assert.True(p.Has("anything.you.want"));
        Assert.True(p.Has("scenes.delete.file"));
    }

    [Fact]
    public void Anonymous_principal_has_no_permissions()
    {
        var p = CovePrincipal.Anonymous();
        Assert.False(p.Has("scenes.read"));
        Assert.Equal(PrincipalKind.Anonymous, p.Kind);
    }

    [Fact]
    public void User_with_resource_wildcard_has_all_actions_in_that_resource()
    {
        var p = new CovePrincipal
        {
            Kind = PrincipalKind.User, UserId = 5, Username = "alice",
            Roles = new HashSet<string> { "Custom" },
            Permissions = new HashSet<string> { "scenes.*" },
        };
        Assert.True(p.Has("scenes.read"));
        Assert.True(p.Has("scenes.delete"));
        Assert.True(p.Has("scenes.delete.file"));
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
        Assert.True(p.Has("scenes.read"));
        Assert.True(p.Has("performers.read"));
        Assert.False(p.Has("scenes.delete"));
    }

    [Fact]
    public void User_with_explicit_keys_only_matches_those_keys()
    {
        var p = new CovePrincipal
        {
            Kind = PrincipalKind.User, UserId = 5, Username = "alice",
            Roles = new HashSet<string>(),
            Permissions = new HashSet<string> { "scenes.read", "performers.read" },
        };
        Assert.True(p.Has("scenes.read"));
        Assert.True(p.Has("performers.read"));
        Assert.False(p.Has("scenes.delete"));
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
        Assert.Contains("scenes.read", keys);
        Assert.Contains("users.write", keys);
        Assert.Contains("audit.read", keys);
        Assert.Contains("system.wipe", keys);
    }

    [Fact]
    public void Expand_resolves_implies_recursively()
    {
        var reg = new PermissionRegistry();
        // scenes.delete.file implies scenes.delete which implies scenes.read.
        var expanded = reg.Expand(["scenes.delete.file"]);
        Assert.Contains("scenes.delete.file", expanded);
        Assert.Contains("scenes.delete", expanded);
        Assert.Contains("scenes.read", expanded);
        // scenes.write is NOT implied by scenes.delete.
        Assert.DoesNotContain("scenes.write", expanded);
    }

    [Fact]
    public void RegisterExtensionPermissions_silently_drops_unprefixed_keys()
    {
        var reg = new PermissionRegistry();
        reg.RegisterExtensionPermissions("notif", new[]
        {
            new PermissionDefinition("totally.invalid", "Other", "x", false, null, "extension:notif"),
        });
        Assert.False(reg.IsKnown("totally.invalid"));
    }

    [Fact]
    public void RegisterExtensionPermissions_silently_drops_wildcard()
    {
        var reg = new PermissionRegistry();
        var beforeCount = reg.All.Count;
        reg.RegisterExtensionPermissions("notif", new[]
        {
            new PermissionDefinition("*", "Other", "x", false, null, "extension:notif"),
        });
        // "*" is a core meta-permission already; the extension cannot redefine it,
        // and no new keys should have been added.
        Assert.Equal(beforeCount, reg.All.Count);
    }

    [Fact]
    public void RegisterExtensionPermissions_accepts_prefixed_keys()
    {
        var reg = new PermissionRegistry();
        reg.RegisterExtensionPermissions("notif", new[]
        {
            new PermissionDefinition("notif.read", "Notif", "Read notifications", false, null, "extension:notif"),
            new PermissionDefinition("notif.write", "Notif", "Write notifications", false, ["notif.read"], "extension:notif"),
        });
        Assert.True(reg.IsKnown("notif.read"));
        Assert.True(reg.IsKnown("notif.write"));
        var expanded = reg.Expand(["notif.write"]);
        Assert.Contains("notif.read", expanded);
    }
}
