[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SearchRoot,
    [Parameter(Mandatory)]
    [string] $StagingDirectory,
    [string] $Configuration,
    [string[]] $ExpectedPackageIds = @(
        'TaskFlow',
        'TaskFlow.Microsoft.Extensions.DependencyInjection',
        'TaskFlow.Microsoft.Extensions.Logging',
        'TaskFlow.Extensions.Time'
    ),
    [string] $GitHubOutput
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-PackageIdentity([string] $Path) {
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $nuspecs = @($archive.Entries | Where-Object FullName -Like '*.nuspec')
        if ($nuspecs.Count -ne 1) {
            throw "Package '$Path' must contain exactly one nuspec."
        }
        $reader = [System.IO.StreamReader]::new($nuspecs[0].Open())
        try { [xml] $nuspec = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
        return [pscustomobject]@{
            Id = [string] $nuspec.package.metadata.id
            Version = [string] $nuspec.package.metadata.version
            Path = $Path
        }
    }
    finally { $archive.Dispose() }
}

$root = (Resolve-Path -LiteralPath $SearchRoot).Path
$packages = @(Get-ChildItem -LiteralPath $root -Recurse -File |
    Where-Object {
        ($_.Name -like '*.nupkg' -or $_.Name -like '*.snupkg') -and
        ([string]::IsNullOrEmpty($Configuration) -or
            $_.FullName -match "[\\/]bin[\\/]$([regex]::Escape($Configuration))[\\/]")
    })
$primary = @($packages | Where-Object { $_.Name -notlike '*.snupkg' -and $_.Name -notlike '*.symbols.nupkg' })
$symbols = @($packages | Where-Object { $_.Name -like '*.snupkg' -or $_.Name -like '*.symbols.nupkg' })

if ($primary.Count -ne $ExpectedPackageIds.Count) {
    throw "Expected $($ExpectedPackageIds.Count) package files, but found $($primary.Count)."
}

$identities = @($primary | ForEach-Object { Read-PackageIdentity $_.FullName })
$actualIds = @($identities.Id | Sort-Object -CaseSensitive)
$expectedIds = @($ExpectedPackageIds | Sort-Object -CaseSensitive)
if (($actualIds -join "`n") -cne ($expectedIds -join "`n")) {
    throw "Unexpected package IDs. Expected '$($expectedIds -join ', ')'; found '$($actualIds -join ', ')'."
}

$versions = @($identities.Version | Sort-Object -Unique)
if ($versions.Count -ne 1 -or [string]::IsNullOrWhiteSpace($versions[0])) {
    throw "All packages must have the same non-empty version. Found: $($versions -join ', ')"
}

if ($symbols.Count -ne $ExpectedPackageIds.Count) {
    throw "Expected one symbol package per package ID, but found $($symbols.Count)."
}
$symbolIdentities = @($symbols | ForEach-Object { Read-PackageIdentity $_.FullName })
if ((@($symbolIdentities.Id | Sort-Object -CaseSensitive) -join "`n") -cne ($expectedIds -join "`n") -or
    @($symbolIdentities.Version | Sort-Object -Unique).Count -ne 1 -or
    $symbolIdentities[0].Version -cne $versions[0]) {
    throw 'Symbol package IDs and versions must match the primary packages.'
}

$stage = [System.IO.Path]::GetFullPath($StagingDirectory)
if (Test-Path -LiteralPath $stage) {
    if (@(Get-ChildItem -LiteralPath $stage -Force).Count -gt 0) {
        throw "Staging directory must be empty: $stage"
    }
}
else {
    [System.IO.Directory]::CreateDirectory($stage) | Out-Null
}

foreach ($package in $packages) {
    Copy-Item -LiteralPath $package.FullName -Destination $stage
}

$version = $versions[0]
$prerelease = $version.Contains('-').ToString().ToLowerInvariant()
if ($GitHubOutput) {
    Add-Content -LiteralPath $GitHubOutput -Value "version=$version"
    Add-Content -LiteralPath $GitHubOutput -Value "prerelease=$prerelease"
    Add-Content -LiteralPath $GitHubOutput -Value "directory=$stage"
}

Write-Host "Validated $($primary.Count) packages and $($symbols.Count) symbol packages for version $version."
