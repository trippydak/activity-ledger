[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'Install the .NET 10 SDK, then run this script again.'
    }

    $sdkVersion = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdkVersion -notmatch '^(1[0-9]|[2-9][0-9])\.') {
        throw 'This project requires the .NET 10 SDK or newer.'
    }

    $artifactRoot = Join-Path $PSScriptRoot 'artifacts'
    $publishDirectory = Join-Path $artifactRoot $Runtime
    [xml]$project = Get-Content (Join-Path $PSScriptRoot 'ActivityLedger.csproj')
    $version = [string]$project.Project.PropertyGroup.Version
    $architecture = $Runtime.Replace('win-', '')
    $installerName = "ActivityLedgerSetup-v$version-Windows-$architecture.exe"
    $standaloneInstaller = Join-Path $artifactRoot $installerName
    $packageDirectory = Join-Path $artifactRoot "ActivityLedger-v$version-$Runtime"
    $archivePath = Join-Path $artifactRoot "ActivityLedger-v$version-$Runtime.zip"

    & dotnet publish (Join-Path $PSScriptRoot 'ActivityLedger.csproj') `
        -r $Runtime -c Release --self-contained true -o $publishDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path (Join-Path $publishDirectory 'ActivityLedger.exe'))) {
        throw 'Publish completed without the expected ActivityLedger.exe.'
    }

    if (Test-Path $packageDirectory) {
        Remove-Item -LiteralPath $packageDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'ActivityLedger.exe') -Destination $standaloneInstaller -Force
    # The self-contained executable recognizes versioned and duplicate-download setup names.
    # Package one copy; Setup creates ActivityLedger.exe in the install folder.
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'ActivityLedger.exe') `
        -Destination (Join-Path $packageDirectory $installerName)
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QUICKSTART.txt') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'browser-extension') -Destination $packageDirectory -Recurse

    if (Test-Path $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Host "Created $standaloneInstaller"
    Write-Host "Created $archivePath"
}
finally {
    Pop-Location
}
