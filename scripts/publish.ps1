# Publishes ScreenshotTool as a self-contained win-x64 Release build.
# Output: dist/ScreenshotTool/

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$project = Join-Path $repoRoot "src\ScreenshotTool\ScreenshotTool.csproj"
$outputDir = Join-Path $repoRoot "dist\ScreenshotTool"

$dotnet = "dotnet"
if (-not (Get-Command $dotnet -ErrorAction SilentlyContinue)) {
    $userDotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
    if (Test-Path $userDotnet) {
        $dotnet = $userDotnet
    }
    else {
        throw "dotnet CLI not found. Install .NET 8 SDK or add dotnet to PATH."
    }
}

Write-Host "Publishing $project -> $outputDir"
& $dotnet publish $project -c Release -r win-x64 --self-contained true -o $outputDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Publish complete: $outputDir"
