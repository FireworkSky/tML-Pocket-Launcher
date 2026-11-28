[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$sourceRoot = Join-Path $repoRoot 'android/app/src/main/java'
foreach ($required in @('android/settings.gradle.kts', 'MobileLauncher.slnx', 'managed/Directory.Build.props', '.editorconfig', '.gitattributes', 'docs/architecture.md', 'docs/code-style.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $required))) { throw "Missing project entry: $required" }
}
foreach ($feature in @('installation', 'releases')) {
    $featureRoot = Join-Path $sourceRoot "app/com/tmlpocket/feature/$feature"
    Get-ChildItem -LiteralPath $featureRoot -Recurse -File -Filter '*.kt' | ForEach-Object {
        if ([IO.File]::ReadAllText($_.FullName) -match '(?m)^import\s+(android\.|app\.com\.tmlpocket\.(core|feature\.(launcher|game))\.)') {
            throw "Package and release logic must remain independent of Android UI/runtime: $($_.FullName)"
        }
    }
}
$managedRoot = Join-Path $repoRoot 'managed'
Get-ChildItem -LiteralPath $managedRoot -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $text = [IO.File]::ReadAllText($_.FullName)
        $declaration = [regex]::Match($text, '(?m)^namespace\s+([\w.]+);')
        if ($declaration.Success) {
            $expected = $_.DirectoryName.Substring($managedRoot.Length + 1).Replace('\', '.').Replace('/', '.')
            $vendoredControls = $expected -match '^MobileLauncher\.Controls(\.|$)' -and $declaration.Groups[1].Value -eq 'MobileJoystick'
            if ($declaration.Groups[1].Value -ne $expected -and -not $vendoredControls) { throw "Managed namespace does not match directory: $($_.FullName)" }
        }
        if ($text -match 'TmlAndroidBootstrap|TmlAndroidHost|FnaTemplate') { throw "Retired managed namespace: $($_.FullName)" }
    }
foreach ($root in @($sourceRoot)) {
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.kt' | ForEach-Object {
        $text = [IO.File]::ReadAllText($_.FullName)
        $declaration = [regex]::Match($text, '(?m)^package\s+([\w.]+)')
        if (-not $declaration.Success) { throw "Missing Kotlin package: $($_.FullName)" }
        $expected = Join-Path $root ($declaration.Groups[1].Value.Replace('.', '/') + '/' + $_.Name)
        if ([IO.Path]::GetFullPath($expected) -ne $_.FullName) { throw "Kotlin package does not match directory: $($_.FullName)" }
        if ($text -match 'com\.example\.fnatemplate') { throw "Template namespace remains in source: $($_.FullName)" }
    }
}
$gradle = [IO.File]::ReadAllText((Join-Path $repoRoot 'android/app/build.gradle.kts'))
if ($gradle -match 'build-tmodloader|prepare-game|FnaTemplate\.csproj') { throw 'Retired source-build dependency remains active' }
if ($gradle -notmatch 'applicationId\s*=\s*"app\.com\.tmlpocket"' -or $gradle -notmatch 'namespace\s*=\s*"app\.com\.tmlpocket"') {
    throw 'Application ID and source namespace must both be app.com.tmlpocket'
}
$native = [IO.File]::ReadAllText((Join-Path $repoRoot 'android/native/src/dotnet/dotnet_launcher.cpp'))
if ($native -notmatch 'Java_app_com_tmlpocket_core_platform_NativeCoreClr_nativeLaunch') { throw 'JNI names do not match the Kotlin source namespace' }
Write-Output 'PASS: project entries, package/namespace directories, feature boundaries, JNI namespace and launcher-only build'
