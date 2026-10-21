using MediaBrowser.Common.Net;
using StrmAssistant.Options;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace FilmIndex.IntegrationTests;

internal static class NetworkChecks
{
    public static async Task Run(IHttpClient native, PluginOptions options, Action<PluginOptions> save, Action<bool, string> check)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serving = Serve(listener, stop.Token);
        try
        {
            options.NetworkOptions.ProxyMode = ProxyMode.MetadataOnly;
            options.NetworkOptions.ProxyUrl = "http://fixture-user:fixture%3Apassword@127.0.0.1:" + port;
            options.NetworkOptions.MetadataHosts = "film-index-proxy-fixture.invalid";
            save(options);
            using (var response = await native.Get(new MediaBrowser.Common.Net.HttpRequestOptions
            { Url = "http://film-index-proxy-fixture.invalid/metadata?nonce=" + Guid.NewGuid().ToString("N"), CancellationToken = stop.Token }))
            using (var reader = new StreamReader(response))
                check(await reader.ReadToEndAsync() == "film-index-proxy-ok", "Native Emby HTTP requests use the metadata whitelist proxy");
            var destination = new Uri("http://127.0.0.1:9876/local");
            check(HttpClient.DefaultProxy.IsBypassed(destination), "Proxy adapter bypasses loopback services");
            var endpoint = HttpClient.DefaultProxy.GetProxy(new Uri("http://film-index-proxy-fixture.invalid/test"))!;
            check(string.IsNullOrEmpty(endpoint.UserInfo) && endpoint.Port == port, "Proxy credentials are not embedded in the routed endpoint");
            var credentials = HttpClient.DefaultProxy.Credentials?.GetCredential(endpoint, "Basic");
            check(credentials?.UserName == "fixture-user" && credentials.Password == "fixture:password", "URL-encoded proxy credentials are supported");
            var unrelated = HttpClient.DefaultProxy.Credentials?.GetCredential(new Uri("http://unrelated.example:1234"), "Basic");
            check(unrelated?.UserName != "fixture-user", "Proxy credentials are not supplied to unrelated endpoints");
            options.NetworkOptions.ProxyMode = ProxyMode.Global;
            options.NetworkOptions.MetadataHosts = "not-the-fixture.invalid";
            save(options);
            using (var response = await native.Get(new MediaBrowser.Common.Net.HttpRequestOptions
            { Url = "http://film-index-proxy-fixture.invalid/global?nonce=" + Guid.NewGuid().ToString("N"), CancellationToken = stop.Token }))
            using (var reader = new StreamReader(response))
                check(await reader.ReadToEndAsync() == "film-index-proxy-ok", "Global proxy mode applies outside the metadata whitelist");
            options.NetworkOptions.LocalDiscoveryAddress = "http://127.0.0.1:18766/reverse-proxy";
            save(options);
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            var question = Encoding.UTF8.GetBytes("who is EmbyServer?");
            await udp.SendAsync(question, new IPEndPoint(IPAddress.Loopback, 7359), stop.Token);
            var answer = await udp.ReceiveAsync(stop.Token);
            using var discovery = JsonDocument.Parse(answer.Buffer);
            check(discovery.RootElement.GetProperty("Address").GetString() == "http://127.0.0.1:18766/reverse-proxy" &&
                !string.IsNullOrWhiteSpace(discovery.RootElement.GetProperty("Id").GetString()), "Native UDP discovery advertises the configured reverse-proxy URL and preserves the server ID");
        }
        finally
        {
            options.NetworkOptions.ProxyMode = ProxyMode.Off;
            options.NetworkOptions.ProxyUrl = string.Empty;
            options.NetworkOptions.LocalDiscoveryAddress = string.Empty;
            save(options);
            stop.Cancel(); listener.Stop();
            try { await serving; } catch (OperationCanceledException) { } catch (SocketException) { }
        }
    }

    private static async Task Serve(TcpListener listener, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var client = await listener.AcceptTcpClientAsync(stop);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var count = 0;
            while (await reader.ReadLineAsync(stop) is { Length: > 0 } line)
            { count += line.Length; if (count > 16384) throw new InvalidOperationException("Fixture proxy request was too large"); }
            var bytes = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: text/plain\r\nContent-Length: 19\r\n\r\nfilm-index-proxy-ok");
            await stream.WriteAsync(bytes, stop);
        }
    }
}
