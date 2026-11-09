param(
    [string]$ProfilePath = 'C:\Users\Casie\AppData\Roaming\com.kesomannen.gale\valheim\profiles\newProfile'
)

$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$outputPath = Join-Path $projectPath 'dist'
$sourcePlugins = Join-Path $ProfilePath 'BepInEx\plugins'
$artworkPath = Join-Path $PSScriptRoot 'artwork'
$nugetPath = Join-Path $env:USERPROFILE '.nuget\packages'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.IO.Compression.FileSystem

$dependencies = @(Get-ChildItem -LiteralPath (Join-Path $sourcePlugins 'MyEmotions') -Filter '*.dll' -File |
    Where-Object Name -ne 'MyEmotions.dll')
if ($dependencies.Count -ne 9) { throw 'Expected the nine installed JSON runtime libraries.' }

foreach ($name in @('MyEmotions', 'Weaponbreaker')) {
    $packagePath = Join-Path $PSScriptRoot ('Thunderstore\' + $name)
    $pluginPath = Join-Path $packagePath ('plugins\' + $name)
    New-Item -ItemType Directory -Path $pluginPath -Force | Out-Null
    $sourceDll = Join-Path $sourcePlugins ($name + '\' + $name + '.dll')
    Copy-Item -LiteralPath $sourceDll -Destination $pluginPath -Force
    foreach ($dependency in $dependencies) {
        Copy-Item -LiteralPath $dependency.FullName -Destination $pluginPath -Force
        $packageId = [IO.Path]::GetFileNameWithoutExtension($dependency.Name).ToLowerInvariant()
        $dependencyHash = (Get-FileHash -LiteralPath $dependency.FullName).Hash
        $matchingVersion = $null
        foreach ($version in Get-ChildItem -LiteralPath (Join-Path $nugetPath $packageId) -Directory) {
            $matchingFile = Get-ChildItem -LiteralPath $version.FullName -Filter $dependency.Name -Recurse -File |
                Where-Object { (Get-FileHash -LiteralPath $_.FullName).Hash -eq $dependencyHash } |
                Select-Object -First 1
            if ($matchingFile) { $matchingVersion = $version; break }
        }
        if (!$matchingVersion) { throw "Cannot locate matching NuGet package for $($dependency.Name)." }
        $licenses = @(Get-ChildItem -LiteralPath $matchingVersion.FullName -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD.PARTY.NOTICES)' })
        if (!$licenses.Count) { throw "No library license found for $packageId." }
        $licensePath = Join-Path $pluginPath ('licenses\' + $matchingVersion.Parent.Name + '-' + $matchingVersion.Name)
        New-Item -ItemType Directory -Path $licensePath -Force | Out-Null
        foreach ($license in $licenses) { Copy-Item -LiteralPath $license.FullName -Destination $licensePath -Force }
    }

    # Resample generated artwork to the exact package size without changing its content.
    $image = [Drawing.Image]::FromFile((Join-Path $artworkPath ($name + '-original.png')))
    $icon = [Drawing.Bitmap]::new(256, 256)
    $graphics = [Drawing.Graphics]::FromImage($icon)
    try {
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.DrawImage($image, 0, 0, 256, 256)
        $icon.Save((Join-Path $packagePath 'icon.png'), [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $icon.Dispose(); $image.Dispose() }

    $manifest = Get-Content -LiteralPath (Join-Path $packagePath 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.name -ne $name -or $manifest.name -notmatch '^[a-zA-Z0-9_]+$' -or
        $manifest.version_number -notmatch '^\d+\.\d+\.\d+$' -or $manifest.description.Length -gt 250 -or
        $manifest.dependencies -notcontains 'denikson-BepInExPack_Valheim-5.4.2360') {
        throw "Invalid manifest for $name."
    }
    $archivePath = Join-Path $outputPath ($name + '-' + $manifest.version_number + '.zip')
    $temporaryArchive = Join-Path $outputPath ([guid]::NewGuid().ToString('N') + '.zip')
    [IO.Compression.ZipFile]::CreateFromDirectory($packagePath, $temporaryArchive,
        [IO.Compression.CompressionLevel]::Optimal, $false)
    $archive = [IO.Compression.ZipFile]::OpenRead($temporaryArchive)
    try {
        foreach ($required in @('manifest.json', 'README.md', 'icon.png', ('plugins/' + $name + '/' + $name + '.dll'))) {
            if (!$archive.GetEntry($required)) { throw "Missing root or plugin entry: $required" }
        }
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match '(^|/)(config|bin|obj|verification)/|\.cfg$|\.pdb$|(^|/)(assembly_valheim|BepInEx|0Harmony|UnityEngine[^/]*)\.dll$') {
                throw "Unexpected private, development or game file: $($entry.FullName)"
            }
            $localFile = Join-Path $packagePath $entry.FullName
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $archiveHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '')
                if ($archiveHash -ne (Get-FileHash -LiteralPath $localFile).Hash) {
                    throw "Archive checksum mismatch: $($entry.FullName)"
                }
            } finally { $stream.Dispose(); $sha.Dispose() }
        }
        $iconStream = $archive.GetEntry('icon.png').Open()
        $archivedIcon = [Drawing.Image]::FromStream($iconStream)
        try {
            if ($archivedIcon.Width -ne 256 -or $archivedIcon.Height -ne 256) { throw 'Invalid icon dimensions.' }
        } finally { $archivedIcon.Dispose(); $iconStream.Dispose() }
        $expectedDlls = @($name + '.dll') + @($dependencies.Name)
        $actualDlls = @($archive.Entries | Where-Object FullName -like '*.dll' | ForEach-Object Name)
        if (@(Compare-Object $expectedDlls $actualDlls).Count) { throw "Incorrect runtime library set for $name." }
        $fileCount = $archive.Entries.Count
    } finally { $archive.Dispose() }
    Move-Item -LiteralPath $temporaryArchive -Destination $archivePath -Force
    [pscustomobject]@{
        Archive = $archivePath
        Version = $manifest.version_number
        Files = $fileCount
        Bytes = (Get-Item -LiteralPath $archivePath).Length
        Sha256 = (Get-FileHash -LiteralPath $archivePath).Hash
    } | Format-List
}
