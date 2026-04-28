using System.Net;
using Cove.Api.Middleware;
using Cove.Data.Auth;

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
}