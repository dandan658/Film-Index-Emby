using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace StrmAssistant.Common
{
    public static class ProxyRules
    {
        public static bool HostMatches(string host, string entries) => (entries ?? string.Empty)
            .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(entry => entry.Trim().TrimEnd('.'))
            .Where(entry => entry.Length > 0).Any(entry => entry.StartsWith("*.", StringComparison.Ordinal)
                ? host.EndsWith(entry.Substring(1), StringComparison.OrdinalIgnoreCase) && host.Length > entry.Length - 1
                : string.Equals(host.TrimEnd('.'), entry, StringComparison.OrdinalIgnoreCase));
        public static bool IsLocal(Uri destination)
        {
            if (destination.IsLoopback || destination.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
            if (!IPAddress.TryParse(destination.Host.Trim('[', ']'), out var address)) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return bytes[0] == 10 || bytes[0] == 127 || bytes[0] == 169 && bytes[1] == 254 ||
                    bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31 || bytes[0] == 192 && bytes[1] == 168;
            return address.IsIPv6LinkLocal || (bytes[0] & 0xfe) == 0xfc || IPAddress.IsLoopback(address);
        }
    }
}
