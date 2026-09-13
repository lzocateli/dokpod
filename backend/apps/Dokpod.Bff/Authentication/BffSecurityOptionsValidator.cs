using Microsoft.Extensions.Options;

namespace Dokpod.Bff.Authentication;

public sealed class BffSecurityOptionsValidator(IHostEnvironment environment) : IValidateOptions<BffSecurityOptions>
{
    public ValidateOptionsResult Validate(string? name, BffSecurityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DataProtectionKeysPath))
            return ValidateOptionsResult.Fail("Security:DataProtectionKeysPath é obrigatório.");
        if (!environment.IsDevelopment()
            && (string.IsNullOrWhiteSpace(options.DataProtectionCertificatePath)
                || string.IsNullOrWhiteSpace(options.DataProtectionCertificatePassword)))
            return ValidateOptionsResult.Fail(
                "Certificado e senha para proteção do key ring são obrigatórios fora de Development.");
        if (options.AllowedOrigins.Count == 0 || options.AllowedOrigins.Any(origin => !IsValidOrigin(origin)))
            return ValidateOptionsResult.Fail("Security:AllowedOrigins deve conter origens absolutas válidas.");
        if (options.TrustedNetworks.Any(network => network is "0.0.0.0/0" or "::/0"))
            return ValidateOptionsResult.Fail("Security:TrustedNetworks não pode confiar em redes universais.");
        if (!environment.IsDevelopment() && options.AllowedOrigins.Any(origin =>
                !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            return ValidateOptionsResult.Fail("Origens HTTP só podem ser usadas em desenvolvimento.");
        return ValidateOptionsResult.Success;
    }

    private static bool IsValidOrigin(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var origin)
        && origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.Query)
        && string.IsNullOrEmpty(origin.Fragment)
        && (origin.Scheme == Uri.UriSchemeHttps || origin.IsLoopback);
}