[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$hostProject = Join-Path $repoRoot 'managed/MobileLauncher.Host/MobileLauncher.Host.csproj'
$publishRoot = Join-Path $repoRoot 'build/launcher-publish'
$assetParent = [IO.Path]::GetFullPath((Join-Path $repoRoot 'android/app/src/main/assets'))
$assetRoot = Join-Path $assetParent 'launcher'

# Both are generated directories with fixed names inside this checkout.
foreach ($generatedPath in @($publishRoot, $assetRoot, (Join-Path $assetParent 'game'))) {
    $resolvedPath = [IO.Path]::GetFullPath($generatedPath)
    if (-not $resolvedPath.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean generated files outside the checkout: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

dotnet publish $hostProject -c Release -r linux-arm64 --self-contained false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "Launcher publish failed with exit code $LASTEXITCODE" }

dotnet publish (Join-Path $repoRoot 'managed/MobileLauncher.Stores/MobileLauncher.Stores.csproj') -c Release -r linux-arm64 --self-contained false -o (Join-Path $publishRoot 'Stores')
if ($LASTEXITCODE -ne 0) { throw "Store service publish failed with exit code $LASTEXITCODE" }

New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
Copy-Item -Path (Join-Path $publishRoot '*') -Destination $assetRoot -Recurse -Force
python (Join-Path $PSScriptRoot 'draw-control-art.py') (Join-Path $assetRoot 'touch-drawings.json')
if ($LASTEXITCODE -ne 0) { throw 'Touch drawing compilation failed' }
& (Join-Path $PSScriptRoot 'prepare-game-references.ps1')
dotnet build (Join-Path $repoRoot 'managed/MobileLauncher.Controls/MobileLauncher.Controls.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Built-in controls patch build failed' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'managed/MobileLauncher.Controls/bin/Release/net10.0/MobileLauncher.Controls.dll') -Destination $assetRoot -Force
dotnet build (Join-Path $repoRoot 'external/tML-Pocket-Patches/MobileLauncher.Patches.csproj') -c Release --nologo `
    "-p:LauncherRoot=$repoRoot" "-p:DirectoryBuildPropsPath=$(Join-Path $repoRoot 'managed/Directory.Build.props')"
if ($LASTEXITCODE -ne 0) { throw 'Built-in patches build failed' }
Copy-Item -LiteralPath (Join-Path $repoRoot 'external/tML-Pocket-Patches/bin/Release/net10.0/MobileLauncher.Patches.dll') -Destination $assetRoot -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'external/tML-Pocket-Patches/LICENSE') -Destination (Join-Path $assetRoot 'RotatingartLauncher-LICENSE.txt') -Force
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $assetRoot -Recurse -File | ForEach-Object {
    $relative = $_.FullName.Substring($assetRoot.Length + 1).Replace('\', '/')
    $stream = [IO.File]::OpenRead($_.FullName)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $hashes[$relative] = [BitConverter]::ToString($sha256.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    } finally {
        $sha256.Dispose()
        $stream.Dispose()
    }
}
foreach ($required in @('MobileLauncher.Host.dll', 'MobileLauncher.Host.deps.json', 'MobileLauncher.Host.runtimeconfig.json', 'MobileLauncher.Bootstrap.dll', 'MobileLauncher.Patches.dll', 'MobileLauncher.Controls.dll')) {
    if (-not $hashes.Contains($required)) { throw "Launcher publish is missing $required" }
}
@{ formatVersion = 3; assembly = 'MobileLauncher.Host.dll'; runtime = '10.0.4'; files = $hashes } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $assetRoot 'manifest.json') -Encoding utf8
Write-Output 'Prepared launcher-only assets. tML, FNA.dll and Terraria Content are supplied by imported packages.'
