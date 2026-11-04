<#
.SYNOPSIS
  Opens the pull request on microsoft/winget-pkgs for a released version of Software Dock.

.DESCRIPTION
  First time: renders the templates next to this script and submits them as a new package.
  Afterwards: `wingetcreate update`, which copies the accepted manifests and changes version, URL and
  hash. Does nothing when the version is already in winget-pkgs or the first submission is still
  waiting for review (a second PR would only be closed as a duplicate).

  The token is a classic GitHub PAT with `public_repo`: wingetcreate forks winget-pkgs into that
  account and opens the PR from the fork. The default GITHUB_TOKEN of a workflow cannot do this.

.EXAMPLE
  ./packaging/winget/Submit-WinGet.ps1 -Version 0.3.0 -Token $env:WINGET_TOKEN
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Token,
    [string] $Repository = 'molidestroyer/windows-software-mcp',
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$id = 'MiguelMolina.SoftwareDock'
$manifestPath = "manifests/m/MiguelMolina/SoftwareDock"
$url = "https://github.com/$Repository/releases/download/v$Version/SoftwareDock-win-Setup.exe"
$headers = @{ Authorization = "Bearer $Token"; 'User-Agent' = 'SoftwareDock-release'; Accept = 'application/vnd.github+json' }

function Test-WinGetPath([string] $path) {
    try {
        Invoke-RestMethod -Headers $headers "https://api.github.com/repos/microsoft/winget-pkgs/contents/$path" | Out-Null
        return $true
    } catch {
        if ($_.Exception.Response.StatusCode.value__ -eq 404) { return $false }
        throw
    }
}

if (Test-WinGetPath "$manifestPath/$Version") {
    Write-Host "$id $Version is already in winget-pkgs."
    return
}

$query = [uri]::EscapeDataString("repo:microsoft/winget-pkgs is:pr is:open in:title `"$id`"")
$open = Invoke-RestMethod -Headers $headers "https://api.github.com/search/issues?q=$query"
if ($open.total_count -gt 0) {
    Write-Host "A pull request for $id is still open, not submitting another one:"
    $open.items | ForEach-Object { Write-Host "  $($_.html_url)" }
    return
}

$wingetcreate = Join-Path ([IO.Path]::GetTempPath()) 'wingetcreate.exe'
Invoke-WebRequest https://aka.ms/wingetcreate/latest -OutFile $wingetcreate

if (Test-WinGetPath $manifestPath) {
    Write-Host "Updating $id to $Version from $url"
    $arguments = @('update', $id, '--version', $Version, '--urls', $url)
    if (-not $DryRun) { $arguments += @('--submit', '--token', $Token) }
    & $wingetcreate @arguments
    if ($LASTEXITCODE -ne 0) { throw "wingetcreate exited with $LASTEXITCODE" }
} else {
    Write-Host "First submission of $id $Version"
    $installer = Join-Path ([IO.Path]::GetTempPath()) 'SoftwareDock-win-Setup.exe'
    Invoke-WebRequest $url -OutFile $installer
    $sha256 = (Get-FileHash $installer -Algorithm SHA256).Hash
    $release = Invoke-RestMethod -Headers $headers "https://api.github.com/repos/$Repository/releases/tags/v$Version"
    $date = ([datetime]$release.published_at).ToString('yyyy-MM-dd')

    $out = Join-Path ([IO.Path]::GetTempPath()) "winget-$Version"
    New-Item -ItemType Directory -Force $out | Out-Null
    Get-ChildItem $PSScriptRoot -Filter '*.yaml' | ForEach-Object {
        (Get-Content $_.FullName -Raw -Encoding utf8).
            Replace('{{VERSION}}', $Version).Replace('{{URL}}', $url).
            Replace('{{SHA256}}', $sha256).Replace('{{DATE}}', $date) |
            Set-Content (Join-Path $out $_.Name) -Encoding utf8 -NoNewline
    }
    Get-ChildItem $out | ForEach-Object { Write-Host "--- $($_.Name)"; Get-Content $_.FullName }

    if ($DryRun) {
        Write-Host "Dry run: manifests rendered in $out, nothing submitted."
    } else {
        & $wingetcreate submit --token $Token $out
        if ($LASTEXITCODE -ne 0) { throw "wingetcreate exited with $LASTEXITCODE" }
    }
}
