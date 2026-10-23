using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DoViFixer.Application.Abstractions;
using DoViFixer.Application.Operations;
using Microsoft.Extensions.Logging;

namespace DoViFixer.App.Startup;

/// <summary>File Explorer's Open and Convert commands for the running DoViFixer.App.exe.</summary>
public interface IFileExplorerIntegration
{
    /// <summary>Whether Windows 11 would show the commands in its first menu once this PC trusts the package's certificate.</summary>
    bool NeedsCertificateTrust { get; }

    /// <summary>
    /// Shows the commands, in Windows 11's first menu when the package can be registered and otherwise as classic
    /// commands under "Show more options", or removes them. Returns whether Windows 11's menu shows them.
    /// </summary>
    Task<bool> UpdateAsync(bool show);

    /// <summary>
    /// Asks Windows, through an administrator prompt, to trust the package's certificate on this PC, then updates the
    /// commands. Returns whether Windows 11's menu shows them; <see langword="false"/> when the prompt is declined.
    /// </summary>
    Task<bool> TrustCertificateAsync(bool show);
}

/// <summary>
/// CI signs the package with a self-signed certificate and publishes its public part next to the app. Windows registers
/// a package only when its certificate is trusted on the computer, which takes administrator rights once.
/// </summary>
internal sealed class FileExplorerIntegration(IFileExplorerMenu classic, ILogger<FileExplorerIntegration> logger) : IFileExplorerIntegration
{
    public const string CertificateFileName = "DoViFixer.FileExplorer.cer";
    // ERROR_CANCELLED: the user declined the administrator prompt.
    private const int Cancelled = 1223;
    private readonly SemaphoreSlim updating = new(1, 1);
    private readonly string executable = Path.ChangeExtension(typeof(App).Assembly.Location, ".exe");

    private string Folder => Path.GetDirectoryName(executable)!;
    private string Certificate => Path.Combine(Folder, CertificateFileName);

    public bool NeedsCertificateTrust => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
        && File.Exists(Path.Combine(Folder, FileExplorerPackage.FileName)) && File.Exists(Certificate) && !IsCertificateTrusted();

    public async Task<bool> UpdateAsync(bool show)
    {
        if (!File.Exists(executable))
        {
            return false;
        }

        // A change of the setting waits for the update at startup, so two never change the menu at once.
        await updating.WaitAsync();
        try
        {
            bool packaged = false;
            try
            {
                // Without a trusted certificate, Windows refuses the package; a registered one is removed.
                packaged = await FileExplorerPackage.UpdateAsync(show && IsCertificateTrusted(), Folder);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not update the Windows 11 File Explorer menu package; using the classic File Explorer menu");
            }

            try
            {
                classic.Update(show && !packaged, executable);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
            {
                // The app works without the menu; files can still be added in the window.
                logger.LogWarning(ex, "Could not update the File Explorer menu");
            }

            return packaged;
        }
        finally
        {
            updating.Release();
        }
    }

    public async Task<bool> TrustCertificateAsync(bool show)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "certutil.exe"), $"-f -addstore TrustedPeople \"{Certificate}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("certutil did not start.");
            await process.WaitForExitAsync();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == Cancelled)
        {
            return false;
        }

        bool trusted = IsCertificateTrusted();
        OperationLog.Audit(logger, "TrustFileExplorerCertificate", Certificate, trusted ? "Completed" : "Failed");
        return trusted && await UpdateAsync(show);
    }

    private bool IsCertificateTrusted()
    {
        try
        {
            using var certificate = X509CertificateLoader.LoadCertificateFromFile(Certificate);
            foreach (var name in new[] { StoreName.TrustedPeople, StoreName.Root })
            {
                using var store = new X509Store(name, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false);
                bool trusted = found.Count > 0;
                foreach (var match in found)
                {
                    match.Dispose();
                }

                if (trusted)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not check the File Explorer package certificate");
        }

        return false;
    }
}
