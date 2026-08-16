param(
    [string] $SiteRoot,
    [switch] $SkipSnippetCompilation
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$documentationFiles = @(
    Get-Item (Join-Path $repositoryRoot 'README.md'),
        (Join-Path $repositoryRoot 'TaskFlow/README.md'),
        (Join-Path $repositoryRoot 'TaskFlow.Extensions.Time/README.md'),
        (Join-Path $repositoryRoot 'TaskFlow.Extensions.Microsoft.DependencyInjection/README.md'),
        (Join-Path $repositoryRoot 'TaskFlow.Extensions.Microsoft.Logging/README.md')
) + @(Get-ChildItem (Join-Path $repositoryRoot 'docs') -Recurse -Filter '*.md')

function Get-MarkdownAnchors([string] $Path)
{
    $anchors = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $duplicates = @{}

    foreach ($line in Get-Content -LiteralPath $Path)
    {
        if ($line -notmatch '^#{1,6}\s+(.+?)\s*#*\s*$') { continue }

        $heading = $Matches[1]
        $heading = $heading -replace '`', ''
        $heading = $heading -replace '<[^>]+>', ''
        $heading = $heading -replace '\[([^]]+)\]\([^)]+\)', '$1'
        $anchor = $heading.ToLowerInvariant()
        $anchor = $anchor -replace '[^\p{L}\p{Nd}\s-]', ''
        $anchor = ($anchor -replace '\s+', '-' -replace '-+', '-').Trim('-')

        if ($duplicates.ContainsKey($anchor))
        {
            $duplicates[$anchor]++
            $anchor = "$anchor-$($duplicates[$anchor])"
        }
        else
        {
            $duplicates[$anchor] = 0
        }

        [void]$anchors.Add($anchor)
    }

    return $anchors
}

function Test-MarkdownAnchor([string] $Path, [string] $Anchor, [System.Collections.Generic.List[string]] $Errors)
{
    if ([string]::IsNullOrWhiteSpace($Anchor)) { return }

    $decodedAnchor = [Uri]::UnescapeDataString($Anchor)
    if (-not (Get-MarkdownAnchors $Path).Contains($decodedAnchor))
    {
        $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $Path)
        $Errors.Add("Missing anchor '#$decodedAnchor' in $relativePath")
    }
}

$permalinkFiles = @{}
foreach ($markdownFile in Get-ChildItem (Join-Path $repositoryRoot 'docs') -Recurse -Filter '*.md')
{
    $markdown = Get-Content -Raw -LiteralPath $markdownFile.FullName
    if ($markdown -match '(?m)^permalink:\s*(\S+)\s*$')
    {
        $permalinkFiles[$Matches[1].TrimEnd('/') + '/'] = $markdownFile.FullName
    }
}

$linkErrors = [System.Collections.Generic.List[string]]::new()
$linkCount = 0

foreach ($markdownFile in $documentationFiles)
{
    $markdown = Get-Content -Raw -LiteralPath $markdownFile.FullName
    $links = [regex]::Matches($markdown, '!?(?:\[[^]]*\])\((?<target>[^)\s]+)(?:\s+["''][^"'']*["''])?\)')

    foreach ($link in $links)
    {
        $linkCount++
        $target = $link.Groups['target'].Value.Trim('<', '>')
        if ($target -match '^(mailto:|tel:|javascript:|data:)') { continue }

        $pathAndFragment = $target -split '#', 2
        $targetPath = [Uri]::UnescapeDataString($pathAndFragment[0])
        $fragment = if ($pathAndFragment.Count -eq 2) { $pathAndFragment[1] } else { '' }

        if ($target -match '^https?://')
        {
            $uri = [Uri]$target
            if ($uri.Host -ne 'dombrovsky.github.io' -or -not $uri.AbsolutePath.StartsWith('/TaskFlow', [StringComparison]::OrdinalIgnoreCase))
            {
                continue
            }

            $permalink = $uri.AbsolutePath.Substring('/TaskFlow'.Length).TrimEnd('/') + '/'
            if (-not $permalinkFiles.ContainsKey($permalink))
            {
                $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $markdownFile.FullName)
                $linkErrors.Add("Missing canonical page '$($uri.AbsolutePath)' linked from $relativePath")
                continue
            }

            Test-MarkdownAnchor $permalinkFiles[$permalink] $uri.Fragment.TrimStart('#') $linkErrors
            continue
        }

        $linkedFile = if ([string]::IsNullOrWhiteSpace($targetPath))
        {
            $markdownFile.FullName
        }
        else
        {
            [IO.Path]::GetFullPath((Join-Path $markdownFile.DirectoryName $targetPath))
        }

        if (-not (Test-Path -LiteralPath $linkedFile -PathType Leaf))
        {
            $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $markdownFile.FullName)
            $linkErrors.Add("Missing file '$targetPath' linked from $relativePath")
            continue
        }

        if ([IO.Path]::GetExtension($linkedFile) -eq '.md')
        {
            Test-MarkdownAnchor $linkedFile $fragment $linkErrors
        }
    }
}

if ($linkErrors.Count -gt 0)
{
    $linkErrors | ForEach-Object { Write-Error $_ }
    throw "Documentation link validation failed with $($linkErrors.Count) error(s)."
}

Write-Output "Validated $linkCount Markdown links across $($documentationFiles.Count) files."

if (-not [string]::IsNullOrWhiteSpace($SiteRoot))
{
    $resolvedSiteRoot = (Resolve-Path $SiteRoot).Path
    $renderErrors = [System.Collections.Generic.List[string]]::new()

    foreach ($permalink in $permalinkFiles.Keys)
    {
        $outputPath = Join-Path $resolvedSiteRoot ($permalink.Trim('/') -replace '/', [IO.Path]::DirectorySeparatorChar)
        if ($permalink -eq '/') { $outputPath = $resolvedSiteRoot }
        $outputFile = Join-Path $outputPath 'index.html'

        if (-not (Test-Path -LiteralPath $outputFile -PathType Leaf))
        {
            $renderErrors.Add("Missing rendered page for permalink '$permalink': $outputFile")
        }
    }

    foreach ($htmlFile in Get-ChildItem $resolvedSiteRoot -Recurse -Filter '*.html')
    {
        $html = Get-Content -Raw -LiteralPath $htmlFile.FullName
        foreach ($match in [regex]::Matches($html, '(?i)href=["''](?<href>[^"'']+)["'']'))
        {
            $href = [Net.WebUtility]::HtmlDecode($match.Groups['href'].Value)
            if ($href -match '^(mailto:|tel:|javascript:|data:)' -or $href.StartsWith('#')) { continue }

            $fragment = ''
            $hrefParts = $href -split '#', 2
            $linkPath = $hrefParts[0] -replace '\?.*$', ''
            if ($hrefParts.Count -eq 2) { $fragment = [Uri]::UnescapeDataString($hrefParts[1]) }

            if ($linkPath -match '^https?://')
            {
                $uri = [Uri]$linkPath
                if ($uri.Host -ne 'dombrovsky.github.io' -or -not $uri.AbsolutePath.StartsWith('/TaskFlow', [StringComparison]::OrdinalIgnoreCase))
                {
                    continue
                }
                $linkPath = $uri.AbsolutePath
            }

            if ($linkPath.StartsWith('/TaskFlow', [StringComparison]::OrdinalIgnoreCase))
            {
                $linkPath = $linkPath.Substring('/TaskFlow'.Length)
            }

            $decodedPath = [Uri]::UnescapeDataString($linkPath)
            $candidate = if ($decodedPath.StartsWith('/'))
            {
                Join-Path $resolvedSiteRoot $decodedPath.TrimStart('/')
            }
            else
            {
                Join-Path $htmlFile.DirectoryName $decodedPath
            }

            if ([string]::IsNullOrWhiteSpace($decodedPath)) { $candidate = $htmlFile.FullName }
            $candidate = [IO.Path]::GetFullPath($candidate)
            if ($decodedPath.EndsWith('/')) { $candidate = Join-Path $candidate 'index.html' }
            if (-not [IO.Path]::HasExtension($candidate) -and -not (Test-Path -LiteralPath $candidate -PathType Leaf))
            {
                $candidate = Join-Path $candidate 'index.html'
            }

            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf))
            {
                $relativeHtml = [IO.Path]::GetRelativePath($resolvedSiteRoot, $htmlFile.FullName)
                $renderErrors.Add("Missing rendered target '$href' linked from $relativeHtml")
                continue
            }

            if (-not [string]::IsNullOrWhiteSpace($fragment) -and [IO.Path]::GetExtension($candidate) -eq '.html')
            {
                $targetHtml = Get-Content -Raw -LiteralPath $candidate
                $escapedFragment = [regex]::Escape($fragment)
                $anchorPattern = '(?i)\bid=["'']{0}["'']' -f $escapedFragment
                if ($targetHtml -notmatch $anchorPattern)
                {
                    $relativeHtml = [IO.Path]::GetRelativePath($resolvedSiteRoot, $htmlFile.FullName)
                    $renderErrors.Add("Missing rendered anchor '#$fragment' for '$href' linked from $relativeHtml")
                }
            }
        }
    }

    if ($renderErrors.Count -gt 0)
    {
        $renderErrors | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
        throw "Rendered-site validation failed with $($renderErrors.Count) error(s)."
    }

    Write-Output "Validated the rendered site in $resolvedSiteRoot."
}

if ($SkipSnippetCompilation) { exit 0 }

$generatedRoot = Join-Path $repositoryRoot 'obj/documentation-validation/generated'
if (Test-Path -LiteralPath $generatedRoot)
{
    Remove-Item -Recurse -Force -LiteralPath $generatedRoot
}
New-Item -ItemType Directory -Path $generatedRoot | Out-Null

$project = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <CodeAnalysisTreatWarningsAsErrors>false</CodeAnalysisTreatWarningsAsErrors>
    <EnableNETAnalyzers>false</EnableNETAnalyzers>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.11" />
    <ProjectReference Include="../../../TaskFlow/TaskFlow.csproj" />
    <ProjectReference Include="../../../TaskFlow.Extensions.Microsoft.DependencyInjection/TaskFlow.Extensions.Microsoft.DependencyInjection.csproj" />
    <ProjectReference Include="../../../TaskFlow.Extensions.Microsoft.Logging/TaskFlow.Extensions.Microsoft.Logging.csproj" />
  </ItemGroup>
</Project>
'@
Set-Content -LiteralPath (Join-Path $generatedRoot 'DocumentationExamples.csproj') -Value $project

$support = @'
global using System;
global using System.IO;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Threading.Tasks.Flow;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;

public interface IDataStore { Task SaveAsync(Data data, CancellationToken token); }
public sealed record Data;
public interface IReadingStore { Task AppendAsync(Reading reading, CancellationToken token); }
public interface IReadingSink { Task HandleAsync(Reading reading, CancellationToken token); }
public interface IReadingSource { event EventHandler<ReadingEventArgs> ReadingReceived; }
public sealed record Reading;
public sealed class ReadingEventArgs : EventArgs { public required Reading Reading { get; init; } }
public interface ITokenClient { Task<AccessToken> RequestAsync(CancellationToken token); }
public sealed record AccessToken(DateTimeOffset ExpiresAt);
public interface ISearchClient { Task<SearchResults> SearchAsync(string query, CancellationToken token); }
public interface IResultView { Task ShowAsync(SearchResults results, CancellationToken token); }
public sealed record SearchResults;
public interface IInbox { Task ProcessAvailableAsync(CancellationToken token); }
public sealed class TransientInboxException : Exception;
public sealed record Report;
'@
Set-Content -LiteralPath (Join-Path $generatedRoot 'Support.cs') -Value $support

$snippetNumber = 0
$manifest = [System.Collections.Generic.List[string]]::new()

foreach ($markdownFile in $documentationFiles)
{
    $markdown = Get-Content -Raw -LiteralPath $markdownFile.FullName
    $matches = [regex]::Matches($markdown, '(?ms)^```csharp\s*\r?\n(.*?)^```\s*$')

    foreach ($match in $matches)
    {
        $snippetNumber++
        $namespace = "DocumentationSnippet$($snippetNumber.ToString('D2'))"
        $code = $match.Groups[1].Value.Trim()
        $lines = [System.Collections.Generic.List[string]]::new()
        foreach ($line in ($code -split '\r?\n')) { $lines.Add($line) }

        $usings = [System.Collections.Generic.List[string]]::new()
        while ($lines.Count -gt 0 -and ($lines[0] -match '^using\s+(?:static\s+)?[A-Za-z_][A-Za-z0-9_.]*\s*;\s*$' -or [string]::IsNullOrWhiteSpace($lines[0])))
        {
            if (-not [string]::IsNullOrWhiteSpace($lines[0])) { $usings.Add($lines[0]) }
            $lines.RemoveAt(0)
        }

        $remaining = ($lines -join "`n").Trim()
        $typeMatch = [regex]::Match($remaining, '(?m)^public\s+(?:sealed\s+|static\s+|abstract\s+)?(?:class|record|interface)\s+')
        $statements = $remaining
        $types = ''
        if ($typeMatch.Success)
        {
            $statements = $remaining.Substring(0, $typeMatch.Index).Trim()
            $types = $remaining.Substring($typeMatch.Index).Trim()
        }

        $builder = [Text.StringBuilder]::new()
        [void]$builder.AppendLine('#pragma warning disable')
        foreach ($using in $usings) { [void]$builder.AppendLine($using) }
        [void]$builder.AppendLine("namespace $namespace")
        [void]$builder.AppendLine('{')

        if ($statements.Length -gt 0)
        {
            [void]$builder.AppendLine('    public static class Example')
            [void]$builder.AppendLine('    {')
            [void]$builder.AppendLine('        public static async Task RunAsync()')
            [void]$builder.AppendLine('        {')

            if ($statements -match '\bflow\b' -and $statements -notmatch '(?:var|ITaskFlow|TaskFlow|CurrentThreadTaskFlow|DedicatedThreadTaskFlow)\s+flow\s*=')
            {
                [void]$builder.AppendLine('            await using var flow = new TaskFlow();')
            }
            if ($statements -match '\blogger\b' -and $statements -notmatch '(?:var|ILogger)\s+logger\s*=')
            {
                [void]$builder.AppendLine('            ILogger logger = NullLogger.Instance;')
            }
            if ($statements -match '\blifetimeToken\b' -and $statements -notmatch 'CancellationToken\s+lifetimeToken')
            {
                [void]$builder.AppendLine('            CancellationToken lifetimeToken = default;')
            }
            if ($statements -match '\bservices\b' -and $statements -notmatch '(?:var|IServiceCollection)\s+services\s*=')
            {
                [void]$builder.AppendLine('            IServiceCollection services = new ServiceCollection();')
            }
            if ($statements -match '\bfactory\b' -and $statements -notmatch 'ITaskFlowFactory\s+factory')
            {
                [void]$builder.AppendLine('            ITaskFlowFactory factory = null!;')
            }

            foreach ($line in ($statements -split '\r?\n')) { [void]$builder.AppendLine("            $line") }

            $helpers = @{
                'SaveAsync' = 'static Task SaveAsync(string value, CancellationToken token) => Task.CompletedTask;'
                'SearchAsync' = 'static Task SearchAsync(CancellationToken token) => Task.CompletedTask;'
                'SendUpdateAsync' = 'static Task SendUpdateAsync(CancellationToken token) => Task.CompletedTask;'
                'PersistOrdersAsync' = 'static Task PersistOrdersAsync(CancellationToken token) => Task.CompletedTask;'
                'PersistAsync' = 'static Task PersistAsync(CancellationToken token) => Task.CompletedTask;'
                'ImportAsync' = 'static Task ImportAsync(CancellationToken token) => Task.CompletedTask;'
                'ExportAsync' = 'static Task ExportAsync(CancellationToken token) => Task.CompletedTask;'
                'RefreshAsync' = 'static Task RefreshAsync(CancellationToken token) => Task.CompletedTask;'
                'ProcessAsync' = 'static Task ProcessAsync(string value, CancellationToken token) => Task.CompletedTask;'
            }
            foreach ($name in $helpers.Keys)
            {
                if ($statements -match "\b$name\s*\(" -and $statements -notmatch "(?:static\s+)?(?:async\s+)?Task(?:<[^>]+>)?\s+$name\s*\(")
                {
                    [void]$builder.AppendLine("            $($helpers[$name])")
                }
            }

            [void]$builder.AppendLine('        }')
            [void]$builder.AppendLine('    }')
        }

        if ($types.Length -gt 0)
        {
            foreach ($line in ($types -split '\r?\n')) { [void]$builder.AppendLine("    $line") }
        }
        [void]$builder.AppendLine('}')

        $fileName = "Snippet$($snippetNumber.ToString('D2')).cs"
        Set-Content -LiteralPath (Join-Path $generatedRoot $fileName) -Value $builder.ToString()
        $relativePath = [IO.Path]::GetRelativePath($repositoryRoot, $markdownFile.FullName)
        $manifest.Add("$fileName`t$relativePath")
    }
}

Set-Content -LiteralPath (Join-Path $generatedRoot 'manifest.txt') -Value $manifest
Write-Output "Generated $snippetNumber C# snippets from $($documentationFiles.Count) Markdown files."

dotnet build (Join-Path $generatedRoot 'DocumentationExamples.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
