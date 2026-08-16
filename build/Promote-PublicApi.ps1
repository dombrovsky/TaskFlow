[CmdletBinding()]
param(
    [string] $RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch] $Verify
)

$ErrorActionPreference = 'Stop'
$nullableHeader = '#nullable enable'
$removedPrefix = '*REMOVED*'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Get-ApiLines([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Missing public API baseline: $Path"
    }

    $lines = [System.IO.File]::ReadAllLines($Path)
    if ($lines.Count -eq 0 -or $lines[0] -cne $nullableHeader) {
        throw "The first line of '$Path' must be '$nullableHeader'."
    }

    $entries = @($lines | Select-Object -Skip 1 | Where-Object { $_.Length -gt 0 })
    $duplicates = @($entries | Group-Object -CaseSensitive | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        throw "Duplicate public API entry in '$Path': $($duplicates[0].Name)"
    }

    foreach ($entry in $entries) {
        if ($entry.Trim() -cne $entry -or $entry.StartsWith('#')) {
            throw "Malformed public API entry in '$Path': $entry"
        }
    }

    return $entries
}

function Write-ApiLines([string] $Path, [string[]] $Entries) {
    $ordered = [string[]] @($Entries)
    [System.Array]::Sort($ordered, [System.StringComparer]::Ordinal)
    $content = @($nullableHeader) + $ordered
    [System.IO.File]::WriteAllText(
        $Path,
        (($content -join [Environment]::NewLine) + [Environment]::NewLine),
        $utf8NoBom)
}

$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$projects = @(Get-ChildItem -LiteralPath $root -Recurse -Filter '*.csproj' -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
$packableProjects = [System.Collections.Generic.List[System.IO.FileInfo]]::new()

foreach ($project in $projects) {
    $isPackable = (& dotnet msbuild $project.FullName -nologo -getProperty:IsPackable 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Could not evaluate IsPackable for '$($project.FullName)': $isPackable"
    }

    if ($isPackable -ceq 'true') {
        $packableProjects.Add($project)
    }
}

if ($packableProjects.Count -eq 0) {
    throw "No packable projects were found under '$root'."
}

$changed = [System.Collections.Generic.List[string]]::new()
foreach ($project in $packableProjects) {
    $directory = $project.DirectoryName
    $shippedPath = Join-Path $directory 'PublicAPI.Shipped.txt'
    $unshippedPath = Join-Path $directory 'PublicAPI.Unshipped.txt'
    $shipped = @(Get-ApiLines $shippedPath)
    $unshipped = @(Get-ApiLines $unshippedPath)
    $shippedSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($entry in $shipped) {
        $null = $shippedSet.Add($entry)
    }

    foreach ($entry in $unshipped) {
        if ($entry.StartsWith($removedPrefix, [System.StringComparison]::Ordinal)) {
            $removed = $entry.Substring($removedPrefix.Length)
            if ([string]::IsNullOrWhiteSpace($removed)) {
                throw "Empty removal marker in '$unshippedPath'."
            }
            if (-not $shippedSet.Remove($removed)) {
                throw "Removal marker in '$unshippedPath' does not match a shipped API: $removed"
            }
        }
        elseif (-not $shippedSet.Add($entry)) {
            throw "API is declared in both shipped and unshipped baselines for '$($project.Name)': $entry"
        }
    }

    if ($unshipped.Count -gt 0) {
        $changed.Add($project.Name)
        if (-not $Verify) {
            Write-ApiLines $shippedPath @($shippedSet)
            Write-ApiLines $unshippedPath @()
        }
    }
}

if ($Verify) {
    Write-Host "Validated $($packableProjects.Count) public API baseline pairs; $($changed.Count) would change."
}
else {
    Write-Host "Promoted public APIs for $($changed.Count) of $($packableProjects.Count) packable projects."
}
