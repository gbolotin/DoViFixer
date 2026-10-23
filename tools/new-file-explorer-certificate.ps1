<#
.SYNOPSIS
Creates the self-signed certificate that signs DoViFixer's Windows 11 File Explorer package.

.DESCRIPTION
Run it once on your own Windows PC. It creates a code-signing certificate whose subject equals the package's publisher,
exports it with its private key to a password-protected PFX file, and removes it from your certificate store again.
Then store the PFX and its password as the repository secrets FILE_EXPLORER_CERTIFICATE (Base64) and
FILE_EXPLORER_CERTIFICATE_PASSWORD; CI signs every package with them. Keep the PFX private, and delete the local
copy once the secrets are set: anyone with it could sign packages that PCs trusting the certificate accept.

.EXAMPLE
powershell -ExecutionPolicy Bypass -File tools\new-file-explorer-certificate.ps1
#>
param(
    # Must equal the Publisher in src/DoViFixer.ShellExtension/Package/AppxManifest.xml.
    [string] $Subject = 'CN=gbolotin',
    [string] $OutputFolder = (Get-Location).Path
)

$ErrorActionPreference = 'Stop'
$certificate = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
    -FriendlyName 'DoViFixer File Explorer package' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(10) `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
try {
    # Works in Windows PowerShell 5.1 as well as PowerShell 7.
    $bytes = New-Object byte[] 24
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    $password = [Convert]::ToBase64String($bytes)
    $pfx = Join-Path $OutputFolder 'DoViFixer.FileExplorer.pfx'
    Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password (ConvertTo-SecureString $password -AsPlainText -Force) | Out-Null
    $base64 = Join-Path $OutputFolder 'DoViFixer.FileExplorer.pfx.base64.txt'
    [IO.File]::WriteAllText($base64, [Convert]::ToBase64String([IO.File]::ReadAllBytes($pfx)))
}
finally {
    # Only the PFX keeps the private key.
    Remove-Item $certificate.PSPath
}

Write-Host "Created $pfx (thumbprint $($certificate.Thumbprint), valid until $($certificate.NotAfter.ToShortDateString()))."
Write-Host ''
Write-Host 'Add the repository secrets on GitHub (Settings > Secrets and variables > Actions > New repository secret):'
Write-Host "  FILE_EXPLORER_CERTIFICATE          = the contents of $base64"
Write-Host "  FILE_EXPLORER_CERTIFICATE_PASSWORD = $password"
Write-Host 'or with the GitHub CLI:'
Write-Host "  Get-Content `"$base64`" | gh secret set FILE_EXPLORER_CERTIFICATE --repo gbolotin/DoViFixer"
Write-Host "  gh secret set FILE_EXPLORER_CERTIFICATE_PASSWORD --repo gbolotin/DoViFixer --body `"$password`""
Write-Host 'Then delete the .pfx and .txt files.'
