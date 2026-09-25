# Self-contained Windows build. The output folder is what Inno Setup packs.
# Run from the repository root:
#   powershell -ExecutionPolicy Bypass -File windows-app\publish-release.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "windows-app\ClipboardSync.App\ClipboardSync.App.csproj"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false

Write-Host ""
Write-Host "Published:"
Write-Host "  windows-app\ClipboardSync.App\bin\Release\net10.0-windows\win-x64\publish\ClipBoard.exe"
Write-Host "That folder does not need the .NET runtime installed on the target PC."
