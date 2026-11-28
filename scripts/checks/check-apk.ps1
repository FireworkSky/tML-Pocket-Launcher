[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ApkPath
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $ApkPath)) { throw "APK not found: $ApkPath" }

$jarCommand = Get-Command jar -ErrorAction SilentlyContinue
if ($null -ne $jarCommand) {
    $entries = @(& $jarCommand.Source tf $ApkPath)
    if ($LASTEXITCODE -ne 0) { throw 'jar could not inspect the APK' }
} else {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ApkPath))
    try {
        $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    } finally {
        $archive.Dispose()
    }
}

foreach ($required in @(
    'lib/arm64-v8a/libmain.so',
    'lib/arm64-v8a/libtheorafile.so',
    'lib/arm64-v8a/libdotnethost.so',
    'lib/arm64-v8a/libSDL3.so',
    'lib/arm64-v8a/libSDL2_compat.so',
    'lib/arm64-v8a/libFNA3D_sdl3.so',
    'lib/arm64-v8a/libFAudio_sdl3.so',
    'lib/arm64-v8a/libc++_shared.so',
    'lib/arm64-v8a/libterraria_converter.so',
    'assets/mobile-controls/control-sprites.json',
    'assets/mobile-controls/control-textures.json',
    'assets/mobile-controls/asset-aliases.json',
    'assets/runtime/dotnet.tar.xz',
    'assets/launcher/manifest.json',
    'assets/launcher/touch-drawings.json',
    'assets/launcher/MobileLauncher.Host.dll',
    'assets/launcher/MobileLauncher.Host.runtimeconfig.json',
    'assets/launcher/MobileLauncher.Bootstrap.dll',
    'assets/launcher/MobileLauncher.Patches.dll',
    'assets/launcher/RotatingartLauncher-LICENSE.txt',
    'assets/launcher/MobileLauncher.Controls.dll',
    'assets/launcher/Stores/MobileLauncher.Stores.dll',
    'assets/launcher/Stores/MobileLauncher.Stores.runtimeconfig.json',
    'assets/launcher/Stores/SteamKit2.dll',
    'assets/ui/index.html',
    'assets/ui/game.html',
    'assets/ui/icons.svg',
    'assets/ui/animate-ui-license.txt',
    'assets/ui/inspira-ui-license.txt',
    'assets/ui/steamkit-license.txt'
)) {
    if (-not ($entries -contains $required)) { throw "APK is missing $required" }
    Write-Output "Found $required"
}

$gameEntries = @($entries | Where-Object {
    $_ -like 'assets/game/*' -or
    $_ -like '*/tModLoader.zip' -or
    $_ -like '*.rawimg' -or
    $_ -like 'assets/launcher/control-sprites/*' -or
    $_ -like '*/control-art.zip' -or
    $_ -like 'assets/launcher/BuiltinMods/*' -or
    $_ -match '(?i)(^|/)(tModLoader|FNA)\.dll$' -or
    $_ -match '(?i)^assets/(launcher/)?Content/'
})
if ($gameEntries.Count -gt 0) {
    throw "APK contains game resources: $($gameEntries -join ', ')"
}
$legacyProfileEntries = @($entries | Where-Object { $_ -match '^assets/mobile-terraria/.*\.(zip|png|xnb)$' })
if ($legacyProfileEntries.Count -gt 0) {
    throw "APK contains obsolete mobile Terraria files: $($legacyProfileEntries -join ', ')"
}
Write-Output 'Verified APK excludes the tModLoader release ZIP and game resources'
if ($entries -contains 'assets/ui/preview.html' -or @($entries | Where-Object { $_ -like 'assets/ui/*.map' -or $_ -like 'assets/ui/assets/*.map' }).Count -gt 0) {
    throw 'Production UI must not contain development previews or source maps'
}

# The stores host loads libmain directly, without the game's SDL preloads.
# Validate its transitive DT_NEEDED entries against actual APK filenames.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ApkPath))
try {
    $prefix = 'lib/arm64-v8a/'
    $systemLibraries = @('libc.so', 'libm.so', 'libdl.so', 'liblog.so', 'libandroid.so', 'libOpenSLES.so', 'libGLESv1_CM.so', 'libGLESv2.so', 'libGLESv3.so', 'libEGL.so', 'libvulkan.so', 'libz.so', 'libjnigraphics.so', 'libaaudio.so')
    $pending = [Collections.Generic.Queue[string]]::new()
    $visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending.Enqueue('libmain.so')
    $pending.Enqueue('libtheorafile.so')
    $pending.Enqueue('libterraria_converter.so')
    while ($pending.Count -gt 0) {
        $name = $pending.Dequeue()
        if (-not $visited.Add($name)) { continue }
        $entry = $archive.GetEntry($prefix + $name)
        if ($null -eq $entry) { throw "APK is missing native dependency $name" }
        $stream = $entry.Open()
        $memory = [IO.MemoryStream]::new()
        try {
            $stream.CopyTo($memory)
            $elf = $memory.ToArray()
        } finally {
            $memory.Dispose()
            $stream.Dispose()
        }
        if ($elf.Length -lt 64 -or [BitConverter]::ToUInt32($elf, 0) -ne 0x464c457f -or $elf[4] -ne 2 -or $elf[5] -ne 1) {
            throw "Expected a little-endian ELF64 native library: $name"
        }
        $sectionOffset = [BitConverter]::ToUInt64($elf, 40)
        $sectionSize = [BitConverter]::ToUInt16($elf, 58)
        $sectionCount = [BitConverter]::ToUInt16($elf, 60)
        if ($sectionSize -lt 64 -or $sectionCount -eq 0 -or $sectionOffset + $sectionSize * $sectionCount -gt $elf.Length) {
            throw "Invalid ELF section headers: $name"
        }
        $foundDynamic = $false
        for ($index = 0; $index -lt $sectionCount; $index++) {
            $section = [int]($sectionOffset + $index * $sectionSize)
            if ([BitConverter]::ToUInt32($elf, $section + 4) -ne 6) { continue } # SHT_DYNAMIC
            $foundDynamic = $true
            $offset = [BitConverter]::ToUInt64($elf, $section + 24)
            $size = [BitConverter]::ToUInt64($elf, $section + 32)
            $link = [BitConverter]::ToUInt32($elf, $section + 40)
            $stride = [BitConverter]::ToUInt64($elf, $section + 56)
            if ($link -ge $sectionCount -or $stride -lt 16 -or $offset + $size -gt $elf.Length) { throw "Invalid ELF dynamic table: $name" }
            $strings = [int]($sectionOffset + $link * $sectionSize)
            $stringOffset = [BitConverter]::ToUInt64($elf, $strings + 24)
            $stringSize = [BitConverter]::ToUInt64($elf, $strings + 32)
            if ($stringOffset + $stringSize -gt $elf.Length) { throw "Invalid ELF string table: $name" }
            for ($position = $offset; $position + 16 -le $offset + $size; $position += $stride) {
                $tag = [BitConverter]::ToInt64($elf, [int]$position)
                if ($tag -eq 0) { break } # DT_NULL
                if ($tag -ne 1) { continue } # DT_NEEDED
                $value = [BitConverter]::ToUInt64($elf, [int]($position + 8))
                if ($value -ge $stringSize) { throw "Invalid ELF dependency name: $name" }
                $start = [int]($stringOffset + $value)
                $end = $start
                while ($end -lt $stringOffset + $stringSize -and $elf[$end] -ne 0) { $end++ }
                if ($end -eq $stringOffset + $stringSize) { throw "Unterminated ELF dependency name: $name" }
                $needed = [Text.Encoding]::UTF8.GetString($elf, $start, $end - $start)
                if ($systemLibraries -ccontains $needed) { continue }
                if ($null -eq $archive.GetEntry($prefix + $needed)) {
                    throw "APK cannot load $name directly: dependency $needed is not packaged (game preloading must not be required by the stores host)"
                }
                $pending.Enqueue($needed)
            }
        }
        if (-not $foundDynamic) { throw "ELF dynamic table not found: $name" }
    }
    Write-Output 'Verified libmain and Terraria converter native dependency closure without game preloads'
} finally {
    $archive.Dispose()
}
