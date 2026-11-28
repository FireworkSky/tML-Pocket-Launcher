[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$requiredSubmodules = @(
    [pscustomobject]@{
        Path = 'external/SDL'
        Revision = 'a11d64ff44fdeb22af604d7bc1c77e941bfd0d67'
    }
    [pscustomobject]@{
        Path = 'external/FNA3D'
        Revision = 'd8f5025adc24e0afdd2c747e2c93b7b03f34616d'
    }
    [pscustomobject]@{
        Path = 'external/FAudio'
        Revision = 'c370ada0eedcab8d9c270b5c5fc2274f4e7f3ec6'
    }
)

foreach ($submodule in $requiredSubmodules) {
    $path = Join-Path $repoRoot $submodule.Path
    if (-not (Test-Path -LiteralPath $path -PathType Container) -or
        -not (Test-Path -LiteralPath (Join-Path $path '.git'))) {
        throw "Submodule '$($submodule.Path)' is not initialized. Run 'git submodule update --init --recursive external/SDL external/FNA3D external/FAudio'."
    }

    $gitDirectory = (& git -C $repoRoot rev-parse --absolute-git-dir).Trim()
    $moduleDirectory = Join-Path $gitDirectory ('modules/' + $submodule.Path)
    if (Test-Path -LiteralPath $moduleDirectory -PathType Container) {
        $head = (& git --git-dir $moduleDirectory --work-tree $path rev-parse HEAD 2>$null).Trim()
    } else {
        $head = (& git -C $path rev-parse HEAD 2>$null).Trim()
    }
    if ($LASTEXITCODE -ne 0 -or $head -ne $submodule.Revision) {
        throw "Submodule '$($submodule.Path)' is at '$head', expected '$($submodule.Revision)'. Check out the pinned commit."
    }

    Write-Output "Verified $($submodule.Path) at $head"
}
