# Builds both projects and creates dist\ShelfBoxModsFix-<version>.zip in the BepInEx folder layout.
# Usage: .\package.ps1 -GameDir "D:\SteamLibrary\steamapps\common\TCG Card Shop Simulator"
param([Parameter(Mandatory=$true)][string]$GameDir, [string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
dotnet build src/Patcher -c Release -p:GameDir="$GameDir"
dotnet build src/Plugin  -c Release -p:GameDir="$GameDir"
$stage = "dist/stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory "$stage/BepInEx/patchers/ShelfBoxModsFix", "$stage/BepInEx/plugins/ShelfBoxModsFix" | Out-Null
Copy-Item src/Patcher/bin/Release/netstandard2.1/ShelfBoxModsFix.Patcher.dll "$stage/BepInEx/patchers/ShelfBoxModsFix/"
Copy-Item src/Plugin/bin/Release/netstandard2.1/ShelfBoxModsFix.dll "$stage/BepInEx/plugins/ShelfBoxModsFix/"
Copy-Item README.md, CHANGELOG.md $stage
Compress-Archive -Path "$stage/*" -DestinationPath "dist/ShelfBoxModsFix-$Version.zip" -Force
Write-Host "Created dist/ShelfBoxModsFix-$Version.zip"
