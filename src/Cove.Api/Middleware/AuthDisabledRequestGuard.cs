using System.Net;
using System.Net.Sockets;

namespace Cove.Api.Middleware;

public static class AuthDisabledRequestGuard
{
    public static bool IsTrustedLocalAddress(IPAddress? address)
    {
        if (address is null)
            return false;

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsTrustedIpv4(address),
            AddressFamily.InterNetworkV6 => IsTrustedIpv6(address),
            _ => false,
        };
    }

    private static bool IsTrustedIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            10 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 168 => true,
            169 when bytes[1] == 254 => true,
            _ => false,
        };
    }

    private static bool IsTrustedIpv6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal)
            return true;

        var bytes = address.GetAddressBytes();
        return (bytes[0] & 0xfe) == 0xfc;
    }
}