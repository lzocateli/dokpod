using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Configuration;

public sealed class DownstreamApiOptionsValidator(IHostEnvironment environment) : IValidateOptions<DownstreamApiOptions>
{
    public ValidateOptionsResult Validate(string? name, DownstreamApiOptions options)
    {
        if (options.TimeoutSeconds <= 0
            || options.MaxRequestContentLengthBytes <= 0
            || options.MaxResponseContentLengthBytes <= 0)
            return ValidateOptionsResult.Fail("Timeout e limites de corpo do downstream devem ser positivos.");
        if (string.IsNullOrWhiteSpace(options.BaseUrl)) return ValidateOptionsResult.Success;
        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
            || !string.IsNullOrEmpty(baseUrl.Query) || !string.IsNullOrEmpty(baseUrl.Fragment))
            return ValidateOptionsResult.Fail("Downstream:Api:BaseUrl deve ser uma URL absoluta sem query ou fragmento.");
        return baseUrl.Scheme == Uri.UriSchemeHttps || (environment.IsDevelopment() && baseUrl.Scheme == Uri.UriSchemeHttp)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("O downstream do BFF deve usar HTTPS fora do desenvolvimento local.");
    }
}