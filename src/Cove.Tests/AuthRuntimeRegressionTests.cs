using System.Net;
using Cove.Core.Interfaces;
using Cove.Api.Middleware;
using Cove.Data.Auth;
using Microsoft.AspNetCore.Http;

namespace Cove.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_uses_argon2id_and_verifies()
    {
        var hash = PasswordHasher.HashPassword("correct horse battery staple");

        Assert.StartsWith("$argon2id$", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash, PasswordHasher.Algorithm));
        Assert.False(PasswordHasher.Verify("wrong", hash, PasswordHasher.Algorithm));
        Assert.False(PasswordHasher.NeedsRehash(hash, PasswordHasher.Algorithm));
    }

    [Fact]
    public void Verify_supports_bcrypt_and_requests_upgrade()
    {
        var bcryptHash = BCrypt.Net.BCrypt.HashPassword("hunter2", workFactor: 4);

        Assert.True(PasswordHasher.Verify("hunter2", bcryptHash, "bcrypt"));
        Assert.True(PasswordHasher.NeedsRehash(bcryptHash, "bcrypt"));
    }
}

public class AuthDisabledRequestGuardTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.2", true)]
    [InlineData("172.16.0.5", true)]
    [InlineData("192.168.1.7", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("1.1.1.1", false)]
    public void Trusts_expected_ipv4_ranges(string address, bool expected)
    {
        Assert.Equal(expected, AuthDisabledRequestGuard.IsTrustedLocalAddress(IPAddress.Parse(address)));
    }

    [Theory]
    [InlineData("::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("fd12:3456:789a::1", true)]
    [InlineData("2001:4860:4860::8888", false)]
    public void Trusts_expected_ipv6_ranges(string address, bool expected)
    {
        Assert.Equal(expected, AuthDisabledRequestGuard.IsTrustedLocalAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void Uses_forwarded_for_only_from_configured_public_proxy()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.10");
        context.Request.Host = new HostString("cove.local");
        context.Request.Headers["X-Forwarded-For"] = "8.8.8.8";

        var trustedProxyConfig = new AuthConfig { KnownProxies = ["198.51.100.10"] };
        var untrustedProxyConfig = new AuthConfig();

        Assert.Equal(IPAddress.Parse("8.8.8.8"), AuthDisabledRequestGuard.GetEffectiveRemoteAddress(context, trustedProxyConfig));
        Assert.Equal(IPAddress.Parse("198.51.100.10"), AuthDisabledRequestGuard.GetEffectiveRemoteAddress(context, untrustedProxyConfig));
        Assert.False(AuthDisabledRequestGuard.IsTrustedLocalRequest(context, trustedProxyConfig));
        Assert.False(AuthDisabledRequestGuard.IsTrustedLocalRequest(context, untrustedProxyConfig));
    }

    [Fact]
    public void Uses_forwarded_for_from_trusted_local_proxy_without_known_proxy_configuration()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.10");
        context.Request.Host = new HostString("cove.local");
        context.Request.Headers["X-Forwarded-For"] = "8.8.8.8";

        Assert.Equal(IPAddress.Parse("8.8.8.8"), AuthDisabledRequestGuard.GetEffectiveRemoteAddress(context, new AuthConfig()));
        Assert.False(AuthDisabledRequestGuard.IsTrustedLocalRequest(context, new AuthConfig()));
    }

    [Fact]
    public void Supports_known_proxy_cidr_entries()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.50.25");
        context.Request.Host = new HostString("cove.local");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";

        var config = new AuthConfig { KnownProxies = ["192.168.50.0/24"] };

        Assert.Equal(IPAddress.Parse("203.0.113.9"), AuthDisabledRequestGuard.GetEffectiveRemoteAddress(context, config));
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("192.168.1.25", true)]
    [InlineData("cove.local", true)]
    [InlineData("stash.ski23.net", false)]
    public void Treats_public_hostnames_as_outside_even_from_local_proxy(string host, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = new HostString(host);

        Assert.Equal(expected, AuthDisabledRequestGuard.IsTrustedLocalRequest(context, new AuthConfig()));
    }

    [Fact]
    public void Treats_forwarded_public_host_as_outside()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Host = new HostString("localhost:5032");
        context.Request.Headers["X-Forwarded-Host"] = "stash.ski23.net";

        Assert.False(AuthDisabledRequestGuard.IsTrustedLocalRequest(context, new AuthConfig()));
    }
}