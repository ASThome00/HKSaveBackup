# Builds a release zip ready for a GitHub release + modlinks submission, checks the release
# collateral, and prints the SHA256 the modlinks manifest needs.
#
# This has to run on a machine with Hollow Knight installed: the mod assembly references the
# game's Managed folder, and those assemblies are Team Cherry's / Unity's / PlayMaker's to
# distribute, not ours. That is why CI never builds this project - see .github/workflows/ci.yml.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet build "$root\src\ToolAssistedSteelsoul" -c Release -p:InstallToGame=false
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$stage = "$root\dist\ToolAssistedSteelsoul"
if (Test-Path "$root\dist") { Remove-Item -Recurse -Force "$root\dist" }
New-Item -ItemType Directory -Force $stage | Out-Null

$bin = "$root\src\ToolAssistedSteelsoul\bin\Release\net472"
Copy-Item "$bin\ToolAssistedSteelsoul.dll", "$bin\ToolAssistedSteelsoul.Core.dll", "$root\README.md" $stage

$zip = "$root\dist\ToolAssistedSteelsoul.zip"

# "$stage\*", not "$stage": given a bare directory, Compress-Archive puts the folder itself in
# the archive, and an installer that extracts into Mods\ToolAssistedSteelsoul\ would then land
# the DLLs one level too deep. ModLoader.LoadModsInit only looks one level down
# (Directory.GetDirectories(Mods), then GetFiles(d, "*.dll")), so such a mod never loads.
Compress-Archive -Path "$stage\*" -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Set-Content -Path "$zip.sha256" -Value $hash -NoNewline

& "$PSScriptRoot\check-release-collateral.ps1" -Zip $zip
if ($LASTEXITCODE -ne 0) { throw "Release collateral checks failed" }

$version = ([xml](Get-Content "$root\Directory.Build.props")).SelectSingleNode("//ModVersion").InnerText.Trim()

Write-Host ""
Write-Host "Release zip: $zip"
Write-Host "SHA256:      $hash"
Write-Host ""
Write-Host "To put it through the release gate:"
Write-Host "  gh release create v$version --draft --title `"v$version`" `"$zip`""
Write-Host "  gh workflow run release.yml -f tag=v$version"
Write-Host "then approve the 'release' environment on the Actions run."
