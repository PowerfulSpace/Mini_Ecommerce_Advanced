<#
    check-doc-links.ps1

    Назначение: проверка резолюции ссылок в .md файлах.

    Стратегии резолюции:
      - inline Markdown links [text](target) — строго от каталога исходного файла;
      - backticked .md пути (doc-link) — строго от каталога исходного файла;
      - прочие backticked пути (resource) — по порядку: каталог исходного файла,
        корень репозитория, затем src/Frontend/PS.MiniEcommerce/ (только для
        путей, начинающихся с "src/" и не "src/Backend/" и не "src/Frontend/").

    Пропускаются (не считаются): MIME-типы, scoped npm-пакеты, HTTP-маршруты
    и шаблоны маршрутов, а также bare-имена без "/" (кроме известных корневых
    файлов и .md-имён).

    Использование:
        pwsh -File scripts/check-doc-links.ps1
        pwsh -File scripts/check-doc-links.ps1 -ShowAll
    Коды выхода: 0 = OK, 1 = есть битые ссылки.
#>

[CmdletBinding()]
param(
    [switch]$ShowAll
)

$ErrorActionPreference = 'Stop'

# Repo root is the parent of the directory that contains this script.
$scriptDir = $PSScriptRoot
if (-not $scriptDir) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$repoRoot = Split-Path -Parent $scriptDir
$frontendRoot = Join-Path -Path $repoRoot -ChildPath 'src\Frontend\PS.MiniEcommerce'

$excludedDirs = @('.git', 'node_modules', 'bin', 'obj', 'dist', '.vs', '.idea', '.vite', '.vitest-cache')
$rootFileNames = @('README.md', 'AGENTS.md', 'LICENSE')
$backtick = [char]96
$fenceMarker = ([string]$backtick) * 3

function Test-ExcludedPath {
    param([string]$RelativePath)

    $segments = $RelativePath -split '[\\/]'
    foreach ($segment in $segments) {
        if ($excludedDirs -contains $segment) { return $true }
    }
    return $false
}

function Get-MarkdownFiles {
    param([string]$Root)

    Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.md' -ErrorAction SilentlyContinue |
        Where-Object {
            $relative = $_.FullName.Substring($Root.Length).TrimStart('\', '/')
            -not (Test-ExcludedPath -RelativePath $relative)
        }
}

function Strip-LinkLabels {
    # Remove the visible text of inline-link labels so that backticks inside
    # [label](target) are not mistaken for standalone path references.
    # The link target itself is extracted separately and is left untouched.
    param([string]$Line)

    return [regex]::Replace($Line, '\[[^\]]*\](?=\()', '[label]')
}

function Test-MimeType {
    # e.g. application/json — a MIME type, not a filesystem path.
    param([string]$Span)
    return ($Span -match '^[a-z0-9.+-]+/[a-z0-9.+-]+$')
}

function Test-ScopedNpmPackage {
    # e.g. @vue/test-utils — an npm package name, not a filesystem path.
    param([string]$Span)
    return ($Span -match '^@[a-z0-9._-]+/[a-z0-9._-]+$')
}

function Test-RouteTemplate {
    # API routes and route templates, e.g. /products/:id or "GET /health".
    param([string]$Span)

    if ($Span.StartsWith('/')) { return $true }
    if ($Span -match ':[A-Za-z_][A-Za-z0-9_]*') { return $true }
    if ($Span -match '^(GET|POST|PUT|DELETE|PATCH)\s') { return $true }
    return $false
}

function Test-DocLinkSpan {
    # A .md path: "foo.md", "foo.md#anchor", or a bare ".md" file name.
    param([string]$Span)
    return ($Span -match '\.md(#.*)?$')
}

function Get-LineReferences {
    param(
        [string]$RelativeFile,
        [int]$LineNumber,
        [string]$Line
    )

    $results = New-Object System.Collections.Generic.List[object]
    $skipped = 0

    # 1) Inline Markdown links: [label](target) -> doc-link, source dir only.
    foreach ($match in [regex]::Matches($Line, '\[[^\]]*\]\(([^)]+)\)')) {
        $raw = $match.Groups[1].Value.Trim()
        $target = $raw

        # Unwrap angle-bracket destinations: <path>
        if ($target.StartsWith('<') -and $target.EndsWith('>')) {
            $target = $target.Substring(1, $target.Length - 2).Trim()
        }
        # Drop an optional title: (path "title")
        if ($target -match '^(\S+)\s+["''].*["'']$') {
            $target = $Matches[1]
        }
        # Strip a fragment/anchor and validate only the path part.
        $pathPart = $target.Split('#')[0].Trim()

        if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
        if ($pathPart -match '^(https?://|mailto:|#|//)') { continue }

        $results.Add([pscustomobject]@{
            Source   = 'inline'
            Category = 'doc-link'
            Raw      = $raw
            PathPart = $pathPart
        })
    }

    # 2) Backticked path candidates. Strip link labels first (see hint).
    $scanLine = Strip-LinkLabels -Line $Line
    $segments = $scanLine.Split($backtick)
    for ($i = 1; $i -lt $segments.Length; $i += 2) {
        $span = $segments[$i].Trim()

        if ([string]::IsNullOrWhiteSpace($span)) { continue }
        if ($span -match '\s') { $skipped++; continue }
        if ($span -match '^(https?://|mailto:)') { $skipped++; continue }

        # Skip non-path artifacts entirely; they are not resolved and not counted.
        if (Test-MimeType -Span $span) { $skipped++; continue }
        if (Test-ScopedNpmPackage -Span $span) { $skipped++; continue }
        if (Test-RouteTemplate -Span $span) { $skipped++; continue }

        if (Test-DocLinkSpan -Span $span) {
            $results.Add([pscustomobject]@{
                Source   = 'backtick'
                Category = 'doc-link'
                Raw      = $span
                PathPart = $span.Split('#')[0]
            })
            continue
        }

        # Resource paths: explicit directory component or a known root file.
        $hasSlash = $span.Contains('/')
        $isRootName = $rootFileNames -contains $span
        if (-not $hasSlash -and -not $isRootName) {
            $skipped++
            continue
        }

        $results.Add([pscustomobject]@{
            Source   = 'backtick'
            Category = 'resource'
            Raw      = $span
            PathPart = $span
        })
    }

    return [pscustomobject]@{
        References = $results
        Skipped    = $skipped
    }
}

function Resolve-Reference {
    param(
        [string]$SourceFile,
        [string]$PathPart,
        [switch]$AllowFallbacks
    )

    $sourceDir = Split-Path -Parent $SourceFile
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add((Join-Path -Path $sourceDir -ChildPath $PathPart))

    if ($AllowFallbacks) {
        # Resource paths may be written relative to the repo root.
        $candidates.Add((Join-Path -Path $repoRoot -ChildPath $PathPart))

        # Frontend source paths are written relative to the frontend project.
        $isFrontendProjectPath = $PathPart.StartsWith('src/') -and
            -not $PathPart.StartsWith('src/Backend/') -and
            -not $PathPart.StartsWith('src/Frontend/')
        if ($isFrontendProjectPath) {
            $candidates.Add((Join-Path -Path $frontendRoot -ChildPath $PathPart))
        }
    }

    $firstResolved = $null
    foreach ($candidate in $candidates) {
        try {
            $resolved = [System.IO.Path]::GetFullPath($candidate)
        }
        catch {
            continue
        }
        if ($null -eq $firstResolved) { $firstResolved = $resolved }
        if (Test-Path -LiteralPath $resolved) {
            return [pscustomobject]@{ Resolved = $resolved; Exists = $true }
        }
    }

    if ($null -eq $firstResolved) { $firstResolved = $candidates[0] }
    return [pscustomobject]@{ Resolved = $firstResolved; Exists = $false }
}

# --- Collect references -------------------------------------------------------

$rootFull = (Resolve-Path -LiteralPath $repoRoot).Path
$files = Get-MarkdownFiles -Root $rootFull
$references = New-Object System.Collections.Generic.List[object]
$skippedTotal = 0

foreach ($file in $files) {
    $relativeFile = $file.FullName.Substring($rootFull.Length).TrimStart('\', '/')
    $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)
    $inFence = $false

    for ($lineIndex = 0; $lineIndex -lt $lines.Length; $lineIndex++) {
        $line = $lines[$lineIndex]

        # Skip fenced code blocks entirely. A fence line toggles the state.
        if ($line -match ('^\s*' + [regex]::Escape($fenceMarker))) {
            $inFence = -not $inFence
            continue
        }
        if ($inFence) { continue }

        $lineResult = Get-LineReferences -RelativeFile $relativeFile -LineNumber ($lineIndex + 1) -Line $line
        $skippedTotal += $lineResult.Skipped

        foreach ($reference in $lineResult.References) {
            $allowFallbacks = ($reference.Category -eq 'resource')
            $resolution = Resolve-Reference -SourceFile $file.FullName -PathPart $reference.PathPart -AllowFallbacks:$allowFallbacks

            # A bare .md name with no sibling file is a document-name mention,
            # not a relative link (e.g. AGENTS.md refers to DECISIONS.md).
            $isBareMd = ($reference.Source -eq 'backtick') -and
                ($reference.Category -eq 'doc-link') -and
                (-not $reference.PathPart.Contains('/'))
            if ($isBareMd -and -not $resolution.Exists) {
                $skippedTotal++
                continue
            }

            $references.Add([pscustomobject]@{
                File     = $relativeFile
                Line     = $lineIndex + 1
                Source   = $reference.Source
                Category = $reference.Category
                Raw      = $reference.Raw
                Resolved = $resolution.Resolved
                Exists   = $resolution.Exists
            })
        }
    }
}

$broken = @($references | Where-Object { -not $_.Exists })
$inlineCount = @($references | Where-Object { $_.Source -eq 'inline' }).Count
$backtickCount = @($references | Where-Object { $_.Source -eq 'backtick' }).Count

# --- Output -------------------------------------------------------------------

$toShow = $broken
if ($ShowAll) { $toShow = $references }

$rows = @($toShow | ForEach-Object {
    [pscustomobject]@{
        'File:Line'     = "$($_.File):$($_.Line)"
        'Category'      = $_.Category
        'Source'        = $_.Source
        'Raw value'     = $_.Raw
        'Resolved path' = $_.Resolved
        'Exists'        = $_.Exists
    }
})

if ($rows.Count -gt 0) {
    $rows | Format-Table -AutoSize -Wrap | Out-String -Width 4096 | Write-Output
}
else {
    Write-Output '(no broken references)'
}

Write-Output ''
Write-Output ("Scanned: {0} references ({1} inline, {2} backticked)" -f $references.Count, $inlineCount, $backtickCount)
Write-Output ("Broken:  {0}" -f $broken.Count)
Write-Output ("Skipped: {0}" -f $skippedTotal)

if ($broken.Count -gt 0) {
    Write-Output 'Broken rows:'
    foreach ($item in $broken) {
        Write-Output ("  {0}:{1} [{2}/{3}] {4} -> {5}" -f $item.File, $item.Line, $item.Category, $item.Source, $item.Raw, $item.Resolved)
    }
    exit 1
}

exit 0
