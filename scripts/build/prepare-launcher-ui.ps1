[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$uiRoot = Join-Path $repoRoot 'ui/launcher'
$assetRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'android/app/src/main/assets/ui'))
if (-not $assetRoot.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'UI asset output must remain inside the checkout'
}
Push-Location $uiRoot
try {
    # Android Studio may not inherit Node added to a terminal-only PATH. NODE_PATH
    # still identifies the installation's module directory on this machine.
    $nodeCommand = Get-Command node.exe -ErrorAction SilentlyContinue
    $node = if ($nodeCommand) { $nodeCommand.Source } else { $null }
    $nodeRoot = if ($node) { Split-Path -Parent $node } else { $null }
    if (-not $node -and $env:NODE_HOME) {
        $candidate = Join-Path $env:NODE_HOME 'node.exe'
        if (Test-Path -LiteralPath $candidate) {
            $node = $candidate
            $nodeRoot = $env:NODE_HOME
        }
    }
    if (-not $node -and $env:NODE_PATH) {
        foreach ($moduleDirectory in ($env:NODE_PATH -split [IO.Path]::PathSeparator)) {
            $candidate = Join-Path (Split-Path -Parent $moduleDirectory) 'node.exe'
            if (Test-Path -LiteralPath $candidate) {
                $node = $candidate
                $nodeRoot = Split-Path -Parent $candidate
                break
            }
        }
    }
    if (-not $node) { throw 'Node.js was not found. Add node.exe to PATH or set NODE_HOME/NODE_PATH.' }

    if (-not (Test-Path -LiteralPath 'node_modules/vite/bin/vite.js')) {
        $npmCommand = Get-Command npm.cmd -ErrorAction SilentlyContinue
        $npm = if ($npmCommand) { $npmCommand.Source } else { Join-Path $nodeRoot 'npm.cmd' }
        if (-not (Test-Path -LiteralPath $npm)) { throw "npm.cmd was not found beside Node.js: $nodeRoot" }
        & $npm ci
        if ($LASTEXITCODE -ne 0) { throw 'UI dependency installation failed' }
    }
    & $node 'node_modules/typescript/lib/tsc.js' --noEmit
    if ($LASTEXITCODE -ne 0) { throw 'UI type check failed' }
    & $node 'node_modules/vite/bin/vite.js' build
    if ($LASTEXITCODE -ne 0) { throw 'UI build failed' }
    if (Test-Path -LiteralPath $assetRoot) { Remove-Item -LiteralPath $assetRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
    Copy-Item -Path 'dist/*' -Destination $assetRoot -Recurse -Force
    # Development previews and source maps are not production entry points.
    Get-ChildItem -LiteralPath $assetRoot -Recurse -File -Filter '*.map' | Remove-Item -Force
    $preview = Join-Path $assetRoot 'preview.html'
    if (Test-Path -LiteralPath $preview) { Remove-Item -LiteralPath $preview -Force }
} finally { Pop-Location }
Write-Output 'Prepared offline production launcher UI.'
