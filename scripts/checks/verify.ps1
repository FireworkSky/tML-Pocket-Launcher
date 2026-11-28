[CmdletBinding()]
param(
    [switch]$Android,
    [switch]$Assemble
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repoRoot
try {
    & (Join-Path $PSScriptRoot 'check-layout.ps1')
    dotnet build MobileLauncher.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Managed build failed' }
    dotnet format whitespace MobileLauncher.slnx --no-restore --verify-no-changes --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Managed formatting check failed; run dotnet format whitespace MobileLauncher.slnx' }
    if ($Android -or $Assemble) {
        & (Join-Path $PSScriptRoot 'check-runtime.ps1')
        Push-Location (Join-Path $repoRoot 'android')
        try {
            $tasks = @('--console=plain')
            $tasks += if ($Assemble) { ':app:assembleDebug' } else { ':app:compileDebugKotlin' }
            & .\gradlew.bat @tasks
            if ($LASTEXITCODE -ne 0) { throw 'Android checks failed' }
        } finally { Pop-Location }
    }
    if ($Assemble) {
        & (Join-Path $PSScriptRoot 'check-apk.ps1') -ApkPath (Join-Path $repoRoot 'android/app/build/outputs/apk/debug/app-arm64-v8a-debug.apk')
    }
} finally { Pop-Location }
