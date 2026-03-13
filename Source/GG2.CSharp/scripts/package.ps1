param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$workspaceRoot = Split-Path -Parent (Split-Path -Parent $repoRoot)
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "dist"
}

$stagingRoot = Join-Path $OutputRoot "gg2-csharp"
$zipPath = Join-Path $OutputRoot "gg2-csharp.zip"

if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Path $stagingRoot | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingRoot "Assets") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingRoot "config") | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingRoot "Maps") | Out-Null

Push-Location $repoRoot
try {
    dotnet test GG2.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }

    dotnet publish .\src\GG2.Client\GG2.Client.csproj -c $Configuration -o $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish for GG2.Client failed." }
    dotnet publish .\src\GG2.ServerLauncher\GG2.ServerLauncher.csproj -c $Configuration -o $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish for GG2.ServerLauncher failed." }
    dotnet publish .\src\GG2.Server\GG2.Server.csproj -c $Configuration -o $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish for GG2.Server failed." }

    New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot "Assets") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot "config") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $stagingRoot "Maps") | Out-Null

    $contentOutput = Join-Path $repoRoot "src\GG2.Client\Content\bin\DesktopGL\Content"
    if (Test-Path $contentOutput) {
        Copy-Item $contentOutput (Join-Path $stagingRoot "Content") -Recurse -Force
    }

    $assetCopies = @(
        @{ Source = "Source\gg2"; Target = "Assets\Source\gg2" },
        @{ Source = "Source\GG2.CSharp\src\GG2.Backgrounds"; Target = "Assets\Source\GG2.CSharp\src\GG2.Backgrounds" },
        @{ Source = "EXEassets"; Target = "Assets\EXEassets" },
        @{ Source = "Music"; Target = "Assets\Music" }
    )

    foreach ($copy in $assetCopies) {
        $sourcePath = Join-Path $workspaceRoot $copy.Source
        $targetPath = Join-Path $stagingRoot $copy.Target
        $targetParent = Split-Path -Parent $targetPath
        if (-not (Test-Path $sourcePath)) {
            throw "Required packaging source is missing: $sourcePath"
        }

        New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
        Copy-Item $sourcePath $targetPath -Recurse -Force
    }

    Copy-Item (Join-Path $repoRoot "packaging\config\client.settings.json") (Join-Path $stagingRoot "config\client.settings.json") -Force
    Copy-Item (Join-Path $repoRoot "packaging\config\input.bindings.json") (Join-Path $stagingRoot "config\input.bindings.json") -Force
    Copy-Item (Join-Path $repoRoot "packaging\config\server.settings.json") (Join-Path $stagingRoot "config\server.settings.json") -Force
    Copy-Item (Join-Path $workspaceRoot "sampleMapRotation.txt") (Join-Path $stagingRoot "config\sampleMapRotation.txt") -Force
    Copy-Item (Join-Path $repoRoot "packaging\README.txt") (Join-Path $stagingRoot "README.txt") -Force
    Copy-Item (Join-Path $workspaceRoot "GPL.txt") (Join-Path $stagingRoot "LICENSE.txt") -Force

    Compress-Archive -Path $stagingRoot -DestinationPath $zipPath -Force
}
finally {
    Pop-Location
}

Write-Host "Package ready:"
Write-Host "  Folder: $stagingRoot"
Write-Host "  Zip:    $zipPath"
