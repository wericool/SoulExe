namespace SoulExe.Services;

/// <summary>Resolves a LAN IPv4 address suitable for mobile clients on the same network.</summary>
public static class LocalNetworkInfo
{
    public static string GetPreferredIpv4()
    {
        try
        {
            var preferred = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                                  && adapter.NetworkInterfaceType is not System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                                  && adapter.NetworkInterfaceType is not System.Net.NetworkInformation.NetworkInterfaceType.Tunnel
                                  && adapter.GetIPProperties().GatewayAddresses.Any(gateway => gateway.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork))
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                           && !System.Net.IPAddress.IsLoopback(address)
                                           && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal));

            if (preferred is not null) return preferred.ToString();

            return System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList
                .FirstOrDefault(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                                           && !System.Net.IPAddress.IsLoopback(address)
                                           && !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))?.ToString()
                   ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }
}
