param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("win-arm64", "win-x64")]
    [string]$RuntimeIdentifier,
    [string]$OutputDirectory = "artifacts/release"
)

$ErrorActionPreference = "Stop"
$version = if ($env:TRADERELAY_VERSION) { $env:TRADERELAY_VERSION } else { "1.0.0" }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDirectory = Join-Path $repoRoot "artifacts/publish/$RuntimeIdentifier"
$archiveDirectory = Join-Path $repoRoot $OutputDirectory
$archive = Join-Path $archiveDirectory "TradeRelay-$version-$RuntimeIdentifier.zip"

Remove-Item $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item $publishDirectory -ItemType Directory -Force | Out-Null
New-Item $archiveDirectory -ItemType Directory -Force | Out-Null

dotnet publish (Join-Path $repoRoot "src/TradeRelay.Desktop/TradeRelay.Desktop.csproj") `
    --configuration Release `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    --no-restore `
    --output $publishDirectory `
    -p:DebugType=None `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$hasCertificate = -not [string]::IsNullOrWhiteSpace($env:WINDOWS_CERTIFICATE_BASE64)
$hasPassword = -not [string]::IsNullOrWhiteSpace($env:WINDOWS_CERTIFICATE_PASSWORD)
if ($hasCertificate -xor $hasPassword) {
    throw "WINDOWS_CERTIFICATE_BASE64 and WINDOWS_CERTIFICATE_PASSWORD must be configured together."
}

$signingState = "unsigned"
if ($hasCertificate) {
    $certificatePath = Join-Path $env:RUNNER_TEMP "traderelay-signing.pfx"
    try {
        [IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($env:WINDOWS_CERTIFICATE_BASE64))
        & signtool sign /fd SHA256 /td SHA256 /tr http://timestamp.digicert.com /f $certificatePath /p $env:WINDOWS_CERTIFICATE_PASSWORD (Join-Path $publishDirectory "TradeRelay.exe")
        if ($LASTEXITCODE -ne 0) { throw "Windows signing failed." }
        $signingState = "signed"
    }
    finally {
        Remove-Item $certificatePath -Force -ErrorAction SilentlyContinue
    }
}

Remove-Item $archive -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $archive -CompressionLevel Optimal
Set-Content -Path "$archive.signing-state" -Value $signingState -NoNewline
Write-Output "Created $archive ($signingState)"
