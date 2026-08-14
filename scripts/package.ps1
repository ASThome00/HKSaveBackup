# Builds a release zip ready for a GitHub release + modlinks submission,
# and prints the SHA256 the modlinks manifest needs.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

dotnet build "$root\src\HKSaveBackup" -c Release -p:InstallToGame=false
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$stage = "$root\dist\HKSaveBackup"
if (Test-Path "$root\dist") { Remove-Item -Recurse -Force "$root\dist" }
New-Item -ItemType Directory -Force $stage | Out-Null

$bin = "$root\src\HKSaveBackup\bin\Release\net472"
Copy-Item "$bin\HKSaveBackup.dll", "$bin\HKSaveBackup.Core.dll", "$root\README.md" $stage

$zip = "$root\dist\HKSaveBackup.zip"
Compress-Archive -Path $stage -DestinationPath $zip

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Release zip: $zip"
Write-Host "SHA256:      $hash"
Write-Host "Paste the hash into modlinks-manifest.xml before submitting."
