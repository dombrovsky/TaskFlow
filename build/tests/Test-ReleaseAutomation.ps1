$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$promoteScript = Join-Path $root 'build/Promote-PublicApi.ps1'
$artifactsScript = Join-Path $root 'build/Get-ReleaseArtifacts.ps1'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Write-TestFile([string] $Path, [string[]] $Lines) {
    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    [System.IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), $utf8NoBom)
}

function New-TestRepository([string] $Name, [string[]] $Shipped, [string[]] $Unshipped) {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-$Name-$([guid]::NewGuid())"
    [System.IO.Directory]::CreateDirectory($path) | Out-Null
    Write-TestFile (Join-Path $path 'Package/Package.csproj') @(
        '<Project Sdk="Microsoft.NET.Sdk">',
        '  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>',
        '</Project>')
    Write-TestFile (Join-Path $path 'Package/PublicAPI.Shipped.txt') (@('#nullable enable') + $Shipped)
    Write-TestFile (Join-Path $path 'Package/PublicAPI.Unshipped.txt') (@('#nullable enable') + $Unshipped)
    return $path
}

function Assert-Equal([string] $Expected, [string] $Actual, [string] $Message) {
    $normalizedExpected = $Expected.Replace("`r`n", "`n")
    $normalizedActual = $Actual.Replace("`r`n", "`n")
    if ($normalizedExpected -cne $normalizedActual) { throw "$Message`nExpected: $Expected`nActual:   $Actual" }
}

function Assert-Throws([scriptblock] $Action, [string] $Message) {
    try { & $Action; throw "Expected failure: $Message" }
    catch {
        if ($_.Exception.Message -eq "Expected failure: $Message") { throw }
    }
}

function Read-OutputMap([string] $Path) {
    $map = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $index = $line.IndexOf('=')
        if ($index -lt 1) {
            throw "Malformed GitHub output line: $line"
        }

        $name = $line.Substring(0, $index)
        $value = $line.Substring($index + 1)
        if (-not $map.TryAdd($name, $value)) {
            throw "Duplicate GitHub output key: $name"
        }
    }

    return $map
}

function New-TestPackage([string] $Directory, [string] $Id, [string] $Version, [switch] $Symbols) {
    $content = Join-Path $Directory ([guid]::NewGuid().ToString())
    [System.IO.Directory]::CreateDirectory($content) | Out-Null
    Write-TestFile (Join-Path $content "$Id.nuspec") @(
        '<?xml version="1.0"?>',
        '<package><metadata>',
        "<id>$Id</id><version>$Version</version>",
        '<authors>tests</authors><description>tests</description>',
        '</metadata></package>')
    $suffix = if ($Symbols) { '.snupkg' } else { '.nupkg' }
    $path = Join-Path $Directory "$Id.$Version$suffix"
    [System.IO.Compression.ZipFile]::CreateFromDirectory($content, $path)
    [System.IO.Directory]::Delete($content, $true)
}

$temporaryRoots = [System.Collections.Generic.List[string]]::new()
try {
    $basic = New-TestRepository 'promotion' @('Z.Api', 'Removed.Api') @('A.Api', '*REMOVED*Removed.Api')
    $temporaryRoots.Add($basic)
    & $promoteScript -RepositoryRoot $basic -Verify
    Assert-Equal "#nullable enable`nZ.Api`nRemoved.Api`n" ([IO.File]::ReadAllText((Join-Path $basic 'Package/PublicAPI.Shipped.txt'))) 'Verify must not modify shipped APIs.'
    & $promoteScript -RepositoryRoot $basic
    Assert-Equal "#nullable enable`nA.Api`nZ.Api`n" ([IO.File]::ReadAllText((Join-Path $basic 'Package/PublicAPI.Shipped.txt'))) 'Promotion must sort additions and consume removals.'
    Assert-Equal "#nullable enable`n" ([IO.File]::ReadAllText((Join-Path $basic 'Package/PublicAPI.Unshipped.txt'))) 'Promotion must empty the unshipped baseline.'
    & $promoteScript -RepositoryRoot $basic

    $duplicate = New-TestRepository 'duplicate' @('A.Api', 'A.Api') @()
    $temporaryRoots.Add($duplicate)
    Assert-Throws { & $promoteScript -RepositoryRoot $duplicate -Verify } 'duplicate API'

    $malformed = New-TestRepository 'malformed' @() @(' A.Api')
    $temporaryRoots.Add($malformed)
    Assert-Throws { & $promoteScript -RepositoryRoot $malformed -Verify } 'malformed API'

    $missingHeader = New-TestRepository 'header' @() @()
    $temporaryRoots.Add($missingHeader)
    Write-TestFile (Join-Path $missingHeader 'Package/PublicAPI.Shipped.txt') @('A.Api')
    Assert-Throws { & $promoteScript -RepositoryRoot $missingHeader -Verify } 'missing nullable header'

    $missingPair = New-TestRepository 'pair' @() @()
    $temporaryRoots.Add($missingPair)
    [System.IO.File]::Delete((Join-Path $missingPair 'Package/PublicAPI.Unshipped.txt'))
    Assert-Throws { & $promoteScript -RepositoryRoot $missingPair -Verify } 'missing baseline pair'

    $missingRemoval = New-TestRepository 'removal' @() @('*REMOVED*Missing.Api')
    $temporaryRoots.Add($missingRemoval)
    Assert-Throws { & $promoteScript -RepositoryRoot $missingRemoval -Verify } 'unmatched removal'

    $packages = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-packages-$([guid]::NewGuid())"
    $stage = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-stage-$([guid]::NewGuid())"
    $githubOutput = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-output-$([guid]::NewGuid()).txt"
    $temporaryRoots.Add($packages)
    $temporaryRoots.Add($stage)
    $temporaryRoots.Add($githubOutput)
    [System.IO.Directory]::CreateDirectory($packages) | Out-Null
    $ids = @('TaskFlow', 'TaskFlow.Microsoft.Extensions.DependencyInjection', 'TaskFlow.Microsoft.Extensions.Logging', 'TaskFlow.Extensions.Time')
    foreach ($id in $ids) {
        New-TestPackage $packages $id '1.2.3-rc1'
        New-TestPackage $packages $id '1.2.3-rc1' -Symbols
    }
    & $artifactsScript -SearchRoot $packages -StagingDirectory $stage -GitHubOutput $githubOutput
    Assert-Equal '8' ([string] @(Get-ChildItem $stage -File).Count) 'All validated artifacts must be staged.'
    $outputs = Read-OutputMap $githubOutput
    Assert-Equal '1.2.3-rc1' $outputs['version'] 'Version output must match validated package version.'
    Assert-Equal 'true' $outputs['prerelease'] 'RC versions must be marked as prerelease.'

    $wrongIdStage = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-wrong-id-stage-$([guid]::NewGuid())"
    $temporaryRoots.Add($wrongIdStage)
    Assert-Throws {
        & $artifactsScript -SearchRoot $packages -StagingDirectory $wrongIdStage -ExpectedPackageIds @('TaskFlow', 'Wrong.Package', 'TaskFlow.Microsoft.Extensions.Logging', 'TaskFlow.Extensions.Time')
    } 'unexpected package ID'

    $badPackages = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-bad-packages-$([guid]::NewGuid())"
    $badStage = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-bad-stage-$([guid]::NewGuid())"
    $temporaryRoots.Add($badPackages)
    $temporaryRoots.Add($badStage)
    [System.IO.Directory]::CreateDirectory($badPackages) | Out-Null
    foreach ($id in $ids) {
        $version = if ($id -eq 'TaskFlow.Extensions.Time') { '2.0.0' } else { '1.2.3' }
        New-TestPackage $badPackages $id $version
        New-TestPackage $badPackages $id $version -Symbols
    }
    Assert-Throws { & $artifactsScript -SearchRoot $badPackages -StagingDirectory $badStage } 'mismatched versions'

    $stablePackages = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-stable-packages-$([guid]::NewGuid())"
    $stableStage = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-stable-stage-$([guid]::NewGuid())"
    $stableOutput = Join-Path ([System.IO.Path]::GetTempPath()) "taskflow-stable-output-$([guid]::NewGuid()).txt"
    $temporaryRoots.Add($stablePackages)
    $temporaryRoots.Add($stableStage)
    $temporaryRoots.Add($stableOutput)
    [System.IO.Directory]::CreateDirectory($stablePackages) | Out-Null
    foreach ($id in $ids) {
        New-TestPackage $stablePackages $id '3.4.5'
        New-TestPackage $stablePackages $id '3.4.5' -Symbols
    }
    & $artifactsScript -SearchRoot $stablePackages -StagingDirectory $stableStage -GitHubOutput $stableOutput
    $stableOutputs = Read-OutputMap $stableOutput
    Assert-Equal '3.4.5' $stableOutputs['version'] 'Stable versions must be returned unchanged.'
    Assert-Equal 'false' $stableOutputs['prerelease'] 'Stable versions must not be marked as prerelease.'

    Write-Host 'Release automation tests passed.'
}
finally {
    foreach ($path in $temporaryRoots) {
        if (-not (Test-Path -LiteralPath $path)) {
            continue
        }

        $item = Get-Item -LiteralPath $path
        if ($item -is [System.IO.DirectoryInfo]) {
            [System.IO.Directory]::Delete($path, $true)
        }
        else {
            [System.IO.File]::Delete($path)
        }
    }
}
