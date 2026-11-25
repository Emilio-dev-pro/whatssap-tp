using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace CaramelChat.Services;

/// <summary>
/// Certificat auto-signé du poste : il permet d'ouvrir l'interface en HTTPS
/// (https://ip:5443) pour que le navigateur autorise le microphone (vocaux et appels).
/// </summary>
public static class CertificateFactory
{
    public static X509Certificate2 LoadOrCreate(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var path = Path.Combine(dataDirectory, "whatsup-selfsigned.pfx");

        if (File.Exists(path))
        {
            try
            {
                return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.Exportable);
            }
            catch
            {
                // Certificat illisible (copie partielle) : on le régénère.
                try { File.Delete(path); } catch { /* ignoré */ }
            }
        }

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=vibeChat (reseau local)",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Environment.MachineName);
        names.AddIpAddress(IPAddress.Loopback);
        names.AddIpAddress(IPAddress.IPv6Loopback);
        if (IPAddress.TryParse(LocalIdentity.DetectLocalIp(), out var localIp))
        {
            names.AddIpAddress(localIp);
        }
        request.CertificateExtensions.Add(names.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(5));

        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
        return new X509Certificate2(path, (string?)null, X509KeyStorageFlags.Exportable);
    }
}
