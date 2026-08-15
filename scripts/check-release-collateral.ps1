# Checks the parts of a release that can be verified without the game installed: version
# agreement between the build and the modlinks manifest, the manifest's own shape, and - when
# a built zip is passed - its layout and assembly versions.
#
# Runs in CI on every push (manifest half) and again inside the release gate (zip half), and
# is worth running by hand before tagging anything.
#
#   pwsh scripts/check-release-collateral.ps1
#   pwsh scripts/check-release-collateral.ps1 -Zip dist/ToolAssistedSteelsoul.zip
[CmdletBinding()]
param(
    # Optional built release zip. Without it only the repository's collateral is checked.
    [string]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

$problems = [System.Collections.Generic.List[string]]::new()
function Add-Problem([string]$message) { $problems.Add($message); Write-Host "  FAIL  $message" }
function Add-Pass([string]$message) { Write-Host "  ok    $message" }

# The two DLLs plus the README, all at the root of the archive: ModLoader.LoadModsInit reads
# Mods\<one folder>\*.dll and does not recurse, so anything nested deeper never loads.
$expectedEntries = @(
    "ToolAssistedSteelsoul.dll",
    "ToolAssistedSteelsoul.Core.dll",
    "README.md"
)

Write-Host "Repository collateral"

$version = ([xml](Get-Content "$root\Directory.Build.props")).SelectSingleNode("//ModVersion").InnerText.Trim()
if ($version -notmatch '^\d+\.\d+\.\d+\.\d+$') {
    Add-Problem "ModVersion '$version' in Directory.Build.props is not a four-part version"
} else {
    Add-Pass "ModVersion is $version"
}

$manifestPath = "$root\modlinks-manifest.xml"
$manifest = [xml](Get-Content $manifestPath)

$manifestVersion = $manifest.SelectSingleNode("//Manifest/Version").InnerText.Trim()
if ($manifestVersion -ne $version) {
    Add-Problem "modlinks-manifest.xml Version is $manifestVersion but the build produces $version"
} else {
    Add-Pass "manifest Version matches the build"
}

foreach ($element in "Name", "Description", "Link", "Repository") {
    $node = $manifest.SelectSingleNode("//Manifest/$element")
    if ($null -eq $node -or [string]::IsNullOrWhiteSpace($node.InnerText)) {
        Add-Problem "modlinks-manifest.xml has no <$element>"
    }
}

$link = $manifest.SelectSingleNode("//Manifest/Link")
if ($null -ne $link) {
    $url = $link.InnerText.Trim()
    if ($url -notmatch "/download/v$([regex]::Escape($version))/") {
        Add-Problem "manifest <Link> does not point at the v$version release: $url"
    } else {
        Add-Pass "manifest <Link> points at the v$version release"
    }

    # The hash is filled in by the release workflow, so the placeholder is expected until then.
    $declared = $link.GetAttribute("SHA256")
    if ($declared -eq "REPLACE_WITH_SHA256") {
        Add-Pass "manifest SHA256 is still the placeholder (filled in at release time)"
    } elseif ($declared -notmatch '^[0-9A-Fa-f]{64}$') {
        Add-Problem "manifest SHA256 is neither the placeholder nor a 64-character hash: '$declared'"
    } else {
        Add-Pass "manifest SHA256 is a well-formed hash"
    }
}

if ($Zip) {
    Write-Host ""
    Write-Host "Release zip"

    if (-not (Test-Path $Zip)) {
        Add-Problem "no zip at $Zip"
    } else {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Zip))
        try {
            $entries = @($archive.Entries | ForEach-Object { $_.FullName })
        } finally {
            $archive.Dispose()
        }

        $nested = @($entries | Where-Object { $_ -match '[\\/]' })
        if ($nested.Count -gt 0) {
            Add-Problem ("zip nests entries in a folder, which installs the DLLs a level too " +
                "deep for ModLoader to find: " + ($nested -join ", "))
        } else {
            Add-Pass "zip has no nested folders"
        }

        $missing = @($expectedEntries | Where-Object { $entries -notcontains $_ })
        if ($missing.Count -gt 0) {
            Add-Problem "zip is missing: $($missing -join ', ')"
        } else {
            Add-Pass "zip contains the expected files"
        }

        $unexpected = @($entries | Where-Object { $expectedEntries -notcontains $_ -and $nested -notcontains $_ })
        if ($unexpected.Count -gt 0) {
            Add-Problem "zip contains unexpected entries: $($unexpected -join ', ')"
        }

        # A zip built from stale bin\ output is the failure this catches: the assemblies inside
        # have to carry the version the manifest is about to advertise.
        $extract = Join-Path ([IO.Path]::GetTempPath()) ("tas-zip-" + [Guid]::NewGuid().ToString("N"))
        [IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $Zip), $extract)
        try {
            foreach ($dll in "ToolAssistedSteelsoul.dll", "ToolAssistedSteelsoul.Core.dll") {
                $path = Join-Path $extract $dll
                if (-not (Test-Path $path)) { continue }
                $assemblyVersion = [Reflection.AssemblyName]::GetAssemblyName($path).Version.ToString()
                if ($assemblyVersion -ne $version) {
                    Add-Problem "$dll is version $assemblyVersion but the release is $version"
                } else {
                    Add-Pass "$dll is version $assemblyVersion"
                }
            }
        } finally {
            Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
        }

        Write-Host ""
        Write-Host "  SHA256  $((Get-FileHash $Zip -Algorithm SHA256).Hash)"
    }
}

Write-Host ""
if ($problems.Count -gt 0) {
    Write-Host "$($problems.Count) problem(s) found."
    exit 1
}

Write-Host "All release collateral checks passed."
exit 0
