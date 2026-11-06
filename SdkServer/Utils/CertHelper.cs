using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MikuSB.Util;

namespace MikuSB.SdkServer.Utils;

public static class CertHelper
{
    private static readonly Logger Logger = new("SdkServer");
    private const string Password = "MikuSB.SdkServer.LocalTLS";
    private static readonly ConcurrentDictionary<string, X509Certificate2> Certificates =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<X509Certificate2> WildcardCert =
        new(() => LoadOrCreatePersisted("wildcard.xoyo.games", "*.xoyo.games"));

    private static string CertificateDirectory => Path.Combine(AppContext.BaseDirectory, "sdk-certs");

    public static X509Certificate2 GetOrCreate(string? serverName)
    {
        if (string.IsNullOrEmpty(serverName) ||
            serverName.EndsWith(".xoyo.games", StringComparison.OrdinalIgnoreCase))
            return WildcardCert.Value;

        var normalized = serverName.Trim().TrimEnd('.').ToLowerInvariant();
        return Certificates.GetOrAdd(normalized, host => LoadOrCreatePersisted(host, host));
    }

    private static X509Certificate2 LoadOrCreatePersisted(string fileHost, string subjectHost)
    {
        Directory.CreateDirectory(CertificateDirectory);
        var pfxPath = Path.Combine(CertificateDirectory, $"{SanitizeFileName(fileHost)}.pfx");
        if (File.Exists(pfxPath))
            return LoadPkcs12(File.ReadAllBytes(pfxPath));

        var certificate = CreateSelfSigned(subjectHost);
        try
        {
            File.WriteAllBytes(pfxPath, certificate.Export(X509ContentType.Pfx, Password));
        }
        catch (Exception ex)
        {
            // A key that could only be created in memory (see LoadPkcs12's fallback) cannot be
            // exported again, so caching it on disk is optional: keep the live certificate and
            // regenerate on the next start instead of losing the listener.
            Logger.Warn($"TLS certificate for {fileHost} cannot be cached to disk ({ex.Message}); it will be regenerated on the next start.");
            return certificate;
        }

        return LoadPkcs12(File.ReadAllBytes(pfxPath));
    }

    private static X509Certificate2 LoadPkcs12(byte[] pfx)
    {
        // Persisted user key set on purpose -- do NOT "optimise" this to EphemeralKeySet.
        //
        // History (see docs/progress/021): when this server is started from a shell that may only write
        // inside the repository, persisting the key into the Windows user key store
        // (%APPDATA%\Microsoft\Crypto\...) is denied and the import throws "Access denied", which used
        // to kill the whole Kestrel/dispatch listener. EphemeralKeySet avoids that write, but on Windows
        // SChannel/SslStream cannot use an ephemeral private key: Kestrel still binds and the port shows
        // up as LISTENING, yet every TLS handshake dies with
        // "System.IO.IOException: Received an unexpected EOF or 0 bytes from the transport stream",
        // i.e. the client can never log in. A hard failure at startup (surfaced by SdkServer.Start's
        // try/catch) is much easier to diagnose than a listener that accepts nothing.
        try
        {
            return X509CertificateLoader.LoadPkcs12(
                pfx,
                Password,
                X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        }
        catch (Exception ex)
        {
            // Some host processes cannot write the CNG key container at all (a restricted token, a
            // service account without a profile, a locked-down %APPDATA%). The message is localized, so
            // the fallback keys off "any failure", not an English/Access-denied check: without it the
            // whole SDK listener dies and the client has nothing to talk to. The in-memory key may
            // fail the handshake on SChannel, which then shows up as a TLS error in the client log -
            // still far better than a dead port with no explanation.
            Logger.Warn($"Could not persist the TLS key into the user key store ({ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}); using an in-memory key instead.");
            return X509CertificateLoader.LoadPkcs12(pfx, Password, X509KeyStorageFlags.EphemeralKeySet);
        }
    }

    private static X509Certificate2 CreateSelfSigned(string host)
    {
        using var rsa = RSA.Create(2048);

        var req = new CertificateRequest(
            new X500DistinguishedName($"CN={host}"),
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        req.CertificateExtensions.Add(san.Build());

        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DigitalSignature,
            critical: false));

        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
            critical: false));

        var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddYears(10));

        var pfx = cert.Export(X509ContentType.Pfx, Password);
        return LoadPkcs12(pfx);
    }

    private static string SanitizeFileName(string host)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(host.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }
}
