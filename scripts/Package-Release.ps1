param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Rid,

    [string] $OutputRoot = (Join-Path $PSScriptRoot '..\release')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\AppLauncher.csproj'
$output = [IO.Path]::GetFullPath($OutputRoot)
$publish = Join-Path $output "publish-$Rid"
$stage = Join-Path $output "AppLauncher-v$Version-$Rid"
$zip = Join-Path $output "AppLauncher-v$Version-$Rid.zip"

New-Item -ItemType Directory -Force -Path $output | Out-Null
foreach ($path in @($publish, $stage, $zip)) {
    if (Test-Path -LiteralPath $path) {
        Remove-Item -LiteralPath $path -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $publish, $stage | Out-Null

[xml] $projectXml = Get-Content -Raw -LiteralPath $project
$projectVersion = ($projectXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($projectVersion -ne $Version) {
    throw "Requested version $Version does not match AppLauncher.csproj Version $projectVersion"
}

dotnet restore $project -r $Rid
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed for $Rid"
}
dotnet publish $project `
    --no-restore -c Release -r $Rid --self-contained true `
    -o $publish `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:ContinuousIntegrationBuild=true
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed for $Rid"
}

Copy-Item (Join-Path $PSScriptRoot '..\LICENSE'), (Join-Path $PSScriptRoot '..\README.md'), (Join-Path $PSScriptRoot '..\README.zh-CN.md') -Destination $publish
New-Item -ItemType Directory -Force -Path (Join-Path $publish 'docs') | Out-Null
Copy-Item (Join-Path $PSScriptRoot '..\docs\*.png') (Join-Path $publish 'docs')

$dotnetRoot = Join-Path ${env:ProgramFiles} 'dotnet'
foreach ($notice in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
    $noticePath = Join-Path $dotnetRoot $notice
    if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
        throw "The .NET runtime notice file was not found: $noticePath"
    }
    Copy-Item -LiteralPath $noticePath -Destination (Join-Path $publish "DOTNET-$notice")
}

$requiredFiles = @(
    'AppLauncher.exe',
    'AppLauncher.dll',
    'AppLauncher.deps.json',
    'AppLauncher.runtimeconfig.json',
    'app.ico',
    'LICENSE',
    'README.md',
    'README.zh-CN.md',
    'DOTNET-LICENSE.txt',
    'DOTNET-ThirdPartyNotices.txt',
    'docs/launcher.png',
    'docs/search.png'
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $publish ($relativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required package file is missing: $relativePath"
    }
}

$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $publish 'AppLauncher.runtimeconfig.json') | ConvertFrom-Json
if ($runtimeConfig.runtimeOptions.tfm -ne 'net10.0') {
    throw "Unexpected target framework: $($runtimeConfig.runtimeOptions.tfm)"
}
$deps = Get-Content -Raw -LiteralPath (Join-Path $publish 'AppLauncher.deps.json') | ConvertFrom-Json
if ($deps.runtimeTarget.name -ne ".NETCoreApp,Version=v10.0/$Rid") {
    throw "Unexpected runtime target: $($deps.runtimeTarget.name)"
}

$pngs = @(Get-ChildItem -LiteralPath (Join-Path $publish 'docs') -Filter '*.png' -File)
if ($pngs.Count -ne 2 -or (@($pngs.Name) -notcontains 'launcher.png') -or (@($pngs.Name) -notcontains 'search.png')) {
    throw 'The package must contain exactly launcher.png and search.png.'
}

$forbidden = @(Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object {
    $_.Extension -in @('.pdb', '.log', '.dmp') -or
    $_.Name -in @('config.json', 'app_cache.json') -or
    $_.FullName -match '(?i)icons_clean'
})
if ($forbidden.Count -gt 0) {
    throw "Forbidden local or diagnostic files found: $($forbidden.FullName -join ', ')"
}

$textFiles = Get-ChildItem -LiteralPath $publish -File -Recurse | Where-Object {
    $_.Extension -in @('.json', '.md', '.txt', '.xml', '.config')
}
foreach ($file in $textFiles) {
    $contents = Get-Content -Raw -LiteralPath $file.FullName
    if ($contents -match '(?i)[A-Z]:\\Users\\|/Users/|/home/') {
        throw "Personal absolute path found in package file: $($file.FullName)"
    }
}

Copy-Item (Join-Path $publish '*') $stage -Recurse -Force
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
if (-not (Test-Path -LiteralPath $zip -PathType Leaf)) {
    throw "ZIP was not created: $zip"
}

Remove-Item -LiteralPath $publish, $stage -Recurse -Force
Write-Output $zip
