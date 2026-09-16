using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Configuration;

public sealed class DownstreamApiOptionsValidator(IHostEnvironment environment) : IValidateOptions<DownstreamApiOptions>
{
    public ValidateOptionsResult Validate(string? name, DownstreamApiOptions options)
    {
        if (options.TimeoutSeconds <= 0 || options.TimeoutSeconds > DownstreamApiOptions.MaxTimeoutSeconds)
            return ValidateOptionsResult.Fail($"Downstream:Api:TimeoutSeconds deve estar entre 1 e {DownstreamApiOptions.MaxTimeoutSeconds}.");
        if (options.MaxRequestContentLengthBytes <= 0
            || options.MaxRequestContentLengthBytes > DownstreamApiOptions.MaxBodyContentLengthBytes
            || options.MaxResponseContentLengthBytes <= 0
            || options.MaxResponseContentLengthBytes > DownstreamApiOptions.MaxBodyContentLengthBytes)
            return ValidateOptionsResult.Fail($"Os limites de corpo do downstream devem estar entre 1 e {DownstreamApiOptions.MaxBodyContentLengthBytes} bytes.");
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
            || !string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
            return ValidateOptionsResult.Fail("Downstream:Api:BaseUrl deve ser uma URL absoluta sem query ou fragmento.");
        return baseUrl.Scheme == Uri.UriSchemeHttps || (environment.IsDevelopment() && baseUrl.Scheme == Uri.UriSchemeHttp)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("O downstream do BFF deve usar HTTPS fora do desenvolvimento local.");
    }
}