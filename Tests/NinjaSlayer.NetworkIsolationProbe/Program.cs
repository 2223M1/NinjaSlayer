using System.Net;
using System.Net.Sockets;

if (args.Length != 2 ||
    !IPAddress.TryParse(args[0], out IPAddress? address) ||
    IPAddress.IsLoopback(address) ||
    !int.TryParse(args[1], out int port) ||
    port is < 1 or > 65535)
{
    Console.Error.WriteLine("Expected a non-loopback IP address and TCP port.");
    return 2;
}

using var client = new TcpClient(address.AddressFamily);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
try
{
    await client.ConnectAsync(address, port, timeout.Token);
}
catch (SocketException)
{
    Console.WriteLine("Outbound TCP connection was blocked.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Outbound TCP connection timed out while isolated.");
    return 0;
}

Console.Error.WriteLine("Outbound TCP connection unexpectedly succeeded.");
return 1;
