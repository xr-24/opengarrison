param(
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [string[]]$Platforms = @("win-x64", "linux-x64", "osx-x64", "osx-arm64")
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot
$workspaceRoot = Split-Path -Parent (Split-Path -Parent $repoRoot)
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repoRoot "dist"
}

function Get-ArchiveExtension {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Platform
    )

    switch -Wildcard ($Platform) {
        "win-*" { return ".zip" }
        default { return ".tar.gz" }
    }
}

function New-Archive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Platform,
        [Parameter(Mandatory = $true)]
        [string]$SourceDirectory,
        [Parameter(Mandatory = $true)]
        [string]$DestinationPath
    )

    if (Test-Path $DestinationPath) {
        Remove-Item $DestinationPath -Force
    }

    if ($Platform -like "win-*") {
        Compress-Archive -Path $SourceDirectory -DestinationPath $DestinationPath -Force
        return
    }

    $parent = Split-Path -Parent $SourceDirectory
    $leaf = Split-Path -Leaf $SourceDirectory
    & tar -czf $DestinationPath -C $parent $leaf
    if ($LASTEXITCODE -ne 0) {
        throw "tar packaging failed for $Platform."
    }
}

function New-UnixLaunchScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$ExecutableName
    )

    $scriptContents = @"
#!/bin/sh
set -eu
SCRIPT_DIR=`$(CDPATH= cd -- "`$(dirname -- "`$0")" && pwd)
chmod +x "`$SCRIPT_DIR/$ExecutableName"
exec "`$SCRIPT_DIR/$ExecutableName" "`$@"
"@

    Set-Content -Path $Path -Value $scriptContents -NoNewline
}

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$packageArtifacts = @()

Push-Location $repoRoot
try {
    dotnet test GG2.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed." }

    foreach ($platform in $Platforms) {
        $packageName = "opengarrison-$platform"
        $stagingRoot = Join-Path $OutputRoot $packageName
        $archivePath = Join-Path $OutputRoot ($packageName + (Get-ArchiveExtension -Platform $platform))

        if (Test-Path $stagingRoot) {
            Remove-Item $stagingRoot -Recurse -Force
        }

        New-Item -ItemType Directory -Path $stagingRoot | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $stagingRoot "Assets") | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $stagingRoot "config") | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $stagingRoot "Maps") | Out-Null

        dotnet publish .\src\GG2.Client\GG2.Client.csproj -c $Configuration -r $platform --self-contained true -o $stagingRoot
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish for GG2.Client failed on $platform." }
        dotnet publish .\src\GG2.ServerLauncher\GG2.ServerLauncher.csproj -c $Configuration -r $platform --self-contained true -o $stagingRoot
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish for GG2.ServerLauncher failed on $platform." }
        dotnet publish .\src\GG2.Server\GG2.Server.csproj -c $Configuration -r $platform --self-contained true -o $stagingRoot
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish for GG2.Server failed on $platform." }

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

        if ($platform -notlike "win-*") {
            New-UnixLaunchScript -Path (Join-Path $stagingRoot "run-client.sh") -ExecutableName "GG2.Client"
            New-UnixLaunchScript -Path (Join-Path $stagingRoot "run-server.sh") -ExecutableName "GG2.Server"
            New-UnixLaunchScript -Path (Join-Path $stagingRoot "run-server-launcher.sh") -ExecutableName "GG2.ServerLauncher"
        }

        New-Archive -Platform $platform -SourceDirectory $stagingRoot -DestinationPath $archivePath
        $packageArtifacts += [pscustomobject]@{
            Platform = $platform
            Folder = $stagingRoot
            Archive = $archivePath
        }
    }
}
finally {
    Pop-Location
}

Write-Host "Packages ready:"
foreach ($artifact in $packageArtifacts) {
    Write-Host "  $($artifact.Platform)"
    Write-Host "    Folder:  $($artifact.Folder)"
    Write-Host "    Archive: $($artifact.Archive)"
}
