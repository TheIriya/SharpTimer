Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$versionSource = Join-Path $repositoryRoot "src/Plugin/Globals.cs"
$buildRoot = Join-Path $repositoryRoot "Build"
$packageRoot = Join-Path $repositoryRoot "package"
$releaseRoot = Join-Path $repositoryRoot "release-artifacts"

$versionMatch = Select-String -Path $versionSource -Pattern 'ModuleVersion\s*=>\s*\$?"([^"]+)"' |
    Select-Object -First 1
if ($null -eq $versionMatch -or $versionMatch.Matches[0].Groups[1].Value -notmatch '^\d+\.\d+\.\d+$') {
    throw "Could not read a valid plugin version from $versionSource"
}

$version = $versionMatch.Matches[0].Groups[1].Value
$packageName = "SharpTimer-v$version"
$stagingDirectory = Join-Path $packageRoot $packageName
$archivePath = Join-Path $releaseRoot "$packageName.zip"

function Remove-GeneratedPath([string]$path, [string]$parentDirectory) {
    $resolvedPath = [IO.Path]::GetFullPath($path)
    $resolvedParent = [IO.Path]::GetFullPath($parentDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar

    if (-not $resolvedPath.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the generated output directory: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
}

Push-Location $repositoryRoot
try {
    Write-Host "Building SharpTimerAPI in Release configuration..."
    dotnet build "SharpTimerAPI/SharpTimerAPI.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "SharpTimerAPI build failed with exit code $LASTEXITCODE" }

    Write-Host "Building SharpTimer in Release configuration..."
    dotnet build "src/SharpTimer.csproj" -c Release
    if ($LASTEXITCODE -ne 0) { throw "SharpTimer build failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}

$requiredBuildFiles = @(
    "addons/counterstrikesharp/plugins/SharpTimer/SharpTimer.dll",
    "addons/counterstrikesharp/gamedata/stgamedata.json",
    "addons/counterstrikesharp/shared/SharpTimerAPI/SharpTimerAPI.dll",
    "cfg/SharpTimer/example.config.cfg"
)
foreach ($relativePath in $requiredBuildFiles) {
    $buildFile = Join-Path $buildRoot $relativePath
    if (-not (Test-Path -LiteralPath $buildFile -PathType Leaf)) {
        throw "Required release file is missing after build: $buildFile"
    }
}

Remove-GeneratedPath $stagingDirectory $packageRoot
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

$sourceAddonRoot = Join-Path $buildRoot "addons/counterstrikesharp"
$targetAddonRoot = Join-Path $stagingDirectory "addons/counterstrikesharp"
New-Item -ItemType Directory -Path (Join-Path $targetAddonRoot "plugins") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $targetAddonRoot "shared") -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $sourceAddonRoot "gamedata") -Destination $targetAddonRoot -Recurse
Copy-Item -LiteralPath (Join-Path $sourceAddonRoot "plugins/SharpTimer") `
    -Destination (Join-Path $targetAddonRoot "plugins") -Recurse
Copy-Item -LiteralPath (Join-Path $sourceAddonRoot "shared/SharpTimerAPI") `
    -Destination (Join-Path $targetAddonRoot "shared") -Recurse
Copy-Item -LiteralPath (Join-Path $buildRoot "cfg") -Destination $stagingDirectory -Recurse

New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
Remove-GeneratedPath $archivePath $releaseRoot

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $stagingDirectory -File -Recurse) {
        $relativePath = [IO.Path]::GetRelativePath($stagingDirectory, $file.FullName).Replace('\', '/')
        $entryName = "$packageName/$relativePath"
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive,
            $file.FullName,
            $entryName,
            [IO.Compression.CompressionLevel]::Optimal
        ) | Out-Null
    }
}
finally {
    $archive.Dispose()
}

$requiredArchiveEntries = @(
    "$packageName/addons/counterstrikesharp/plugins/SharpTimer/SharpTimer.dll",
    "$packageName/addons/counterstrikesharp/gamedata/stgamedata.json",
    "$packageName/addons/counterstrikesharp/shared/SharpTimerAPI/SharpTimerAPI.dll",
    "$packageName/cfg/SharpTimer/example.config.cfg"
)
$zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    $entries = @($zip.Entries | ForEach-Object FullName)
    foreach ($entry in $requiredArchiveEntries) {
        if ($entry -notin $entries) { throw "Release archive is missing required file: $entry" }
    }
    if ($entries | Where-Object { $_ -match 'SharpTimer-Example' }) {
        throw "Release archive must not contain the SharpTimer-Example plugin"
    }
}
finally {
    $zip.Dispose()
}

$archiveInfo = Get-Item -LiteralPath $archivePath
Write-Host "Created $($archiveInfo.FullName) ($($archiveInfo.Length) bytes)"
