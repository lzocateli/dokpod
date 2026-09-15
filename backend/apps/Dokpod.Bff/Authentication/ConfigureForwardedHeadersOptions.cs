using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Authentication;

public sealed class ConfigureForwardedHeadersOptions(IOptions<BffSecurityOptions> securityOptions)
    : IConfigureOptions<ForwardedHeadersOptions>
{
    public void Configure(ForwardedHeadersOptions options)
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
            | ForwardedHeaders.XForwardedHost
            | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedPrefix;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in securityOptions.Value.TrustedProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in securityOptions.Value.TrustedNetworks)
        {
            if (System.Net.IPNetwork.TryParse(network, out var parsedNetwork))
            {
                options.KnownIPNetworks.Add(parsedNetwork);
            }
        }
    }
}