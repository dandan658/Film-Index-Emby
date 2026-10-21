using HarmonyLib;
using StrmAssistant.Options;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;

namespace StrmAssistant.Common
{
    internal static class NativeNetworkApi
    {
        private sealed class Settings
        {
            public ProxyMode Mode;
            public Uri Endpoint;
            public NetworkCredential Credential;
            public string Hosts;
            public string DiscoveryAddress;
        }
        private static Settings _settings = new Settings();
        private static Harmony Patch;
        private static IWebProxy _originalDefault;
        private static RoutingProxy _default;

        public static void Initialize()
        {
            Update();
            if (_default != null) return;
            _originalDefault = HttpClient.DefaultProxy;
            _default = new RoutingProxy(_originalDefault);
            HttpClient.DefaultProxy = _default;
            try
            {
                Patch = new Harmony("film-index.emby.network");
                var type = Assembly.Load("Emby.Server.Implementations").GetType("Emby.Server.Implementations.ApplicationHost", true);
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(method => method.Name == "CreateHttpClientHandler" && typeof(HttpMessageHandler).IsAssignableFrom(method.ReturnType)).ToArray();
                if (methods.Length == 0) throw new NotSupportedException("HTTP handler factory was not found");
                foreach (var method in methods) Patch.Patch(method, postfix: new HarmonyMethod(typeof(NativeNetworkApi), nameof(HandlerCreated)));
            }
            catch (Exception error)
            {
                Plugin.Instance.Logger.Warn("FilmIndex: native proxy adapter could not attach: " + error.Message);
            }
            try
            {
                var udp = Assembly.Load("Emby.Server.Implementations").GetType("Emby.Server.Implementations.Udp.UdpServer", true);
                var send = udp.GetMethod("SendMessage", BindingFlags.NonPublic | BindingFlags.Instance);
                if (Patch != null && send != null) Patch.Patch(send, prefix: new HarmonyMethod(typeof(NativeNetworkApi), nameof(DiscoveryMessage)));
            }
            catch (Exception error) { Plugin.Instance.Logger.Warn("FilmIndex: local discovery adapter could not attach: " + error.Message); }
        }

        public static void Update()
        {
            var options = Plugin.Instance.GetPluginOptions().NetworkOptions;
            var next = new Settings { Mode = options.ProxyMode, Hosts = options.MetadataHosts };
            if (Uri.TryCreate(options.LocalDiscoveryAddress, UriKind.Absolute, out var discovery) &&
                (discovery.Scheme == "http" || discovery.Scheme == "https") && string.IsNullOrEmpty(discovery.UserInfo) &&
                string.IsNullOrEmpty(discovery.Query) && string.IsNullOrEmpty(discovery.Fragment)) next.DiscoveryAddress = discovery.AbsoluteUri.TrimEnd('/');
            if (options.ProxyMode != ProxyMode.Off && Uri.TryCreate(options.ProxyUrl, UriKind.Absolute, out var uri) &&
                new[] { "http", "https", "socks4", "socks5" }.Contains(uri.Scheme))
            {
                var userInfo = uri.UserInfo.Split(new[] { ':' }, 2);
                if (userInfo.Length == 2) next.Credential = new NetworkCredential(Uri.UnescapeDataString(userInfo[0]), Uri.UnescapeDataString(userInfo[1]));
                next.Endpoint = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri;
            }
            else next.Mode = ProxyMode.Off;
            Volatile.Write(ref _settings, next);
        }

        public static void HandlerCreated(HttpMessageHandler __result)
        {
            while (__result is DelegatingHandler delegating && delegating.InnerHandler != null) __result = delegating.InnerHandler;
            if (__result is HttpClientHandler client)
            { client.Proxy = client.Proxy is RoutingProxy ? client.Proxy : new RoutingProxy(client.Proxy ?? _originalDefault); client.UseProxy = true; }
            else if (__result is SocketsHttpHandler sockets)
            { sockets.Proxy = sockets.Proxy is RoutingProxy ? sockets.Proxy : new RoutingProxy(sockets.Proxy ?? _originalDefault); sockets.UseProxy = true; }
        }

        public static void Dispose()
        {
            Patch?.UnpatchAll(Patch.Id);
            if (ReferenceEquals(HttpClient.DefaultProxy, _default)) HttpClient.DefaultProxy = _originalDefault;
        }

        public static void DiscoveryMessage(ref string __0)
        {
            var address = Volatile.Read(ref _settings).DiscoveryAddress;
            // In 4.10.1 the method receives a local URL; Emby constructs the discovery JSON afterward.
            if (!string.IsNullOrEmpty(address) && Uri.TryCreate(__0, UriKind.Absolute, out var original) &&
                (original.Scheme == "http" || original.Scheme == "https")) __0 = address;
        }

        private sealed class RoutingProxy : IWebProxy, ICredentials
        {
            private readonly IWebProxy _fallback;
            private ICredentials _fallbackCredentials;
            public RoutingProxy(IWebProxy fallback) { _fallback = fallback; }
            public ICredentials Credentials { get => this; set => _fallbackCredentials = value; }
            public Uri GetProxy(Uri destination)
            {
                var current = Volatile.Read(ref _settings);
                if (ProxyRules.IsLocal(destination)) return destination;
                if (current.Endpoint != null && (current.Mode == ProxyMode.Global || current.Mode == ProxyMode.MetadataOnly && ProxyRules.HostMatches(destination.DnsSafeHost, current.Hosts))) return current.Endpoint;
                return _fallback?.GetProxy(destination) ?? destination;
            }
            public bool IsBypassed(Uri destination) => GetProxy(destination) == destination;
            public NetworkCredential GetCredential(Uri uri, string authenticationType)
            {
                var current = Volatile.Read(ref _settings);
                if (current.Endpoint != null && current.Credential != null &&
                    string.Equals(uri.Host, current.Endpoint.Host, StringComparison.OrdinalIgnoreCase) && uri.Port == current.Endpoint.Port)
                    return current.Credential;
                return (_fallbackCredentials ?? _fallback?.Credentials)?.GetCredential(uri, authenticationType);
            }
        }
    }
}
