[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release", "Beta", "CI")]
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$projectPath = Join-Path $projectRoot "PCL.Nex.ExperimentalFeatures.csproj"
$assemblyPath = Join-Path $projectRoot "bin\$Configuration\net10.0-windows\PCL.Nex.ExperimentalFeatures.dll"
$stagePath = Join-Path $projectRoot "obj\pclx-package"
$outputDirectory = Join-Path $projectRoot "dist"
$packageName = [string]::Concat([char]0x5B9E, [char]0x9A8C, [char]0x6027, [char]0x529F, [char]0x80FD) + ".pclx"
$outputPath = Join-Path $outputDirectory $packageName

& dotnet build $projectPath --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "Plugin assembly build failed." }
if (-not (Test-Path -LiteralPath $assemblyPath)) { throw "Plugin assembly was not found: $assemblyPath" }

if (Test-Path -LiteralPath $stagePath) { Remove-Item -LiteralPath $stagePath -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $stagePath "lib") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagePath "mixins") -Force | Out-Null
Copy-Item -LiteralPath $assemblyPath -Destination (Join-Path $stagePath "lib\PCL.Nex.ExperimentalFeatures.dll")
Copy-Item -LiteralPath (Join-Path $projectRoot "plugin.json") -Destination (Join-Path $stagePath "plugin.json")
Copy-Item -LiteralPath (Join-Path $projectRoot "mixins\slider-keyboard-precision.json") -Destination (Join-Path $stagePath "mixins\slider-keyboard-precision.json")
Copy-Item -LiteralPath (Join-Path $projectRoot "mixins\open-website-https.json") -Destination (Join-Path $stagePath "mixins\open-website-https.json")

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stagePath, $outputPath)
Write-Host "Generated: $outputPath"
