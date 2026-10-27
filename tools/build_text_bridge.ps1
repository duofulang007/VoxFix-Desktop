param([switch]$BuildTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $locator)) { throw 'Native text bridge needs Visual Studio C++ Build Tools and Windows SDK.' }
$installation = & $locator -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'C++ Build Tools are missing.' }
$vc = Get-ChildItem -LiteralPath (Join-Path $installation 'VC\Tools\MSVC') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\msctf.h') } | Sort-Object Name -Descending | Select-Object -First 1
if (-not $vc -or -not $sdk) { throw 'C++ compiler or Windows SDK headers are missing.' }
$compiler = Join-Path $vc.FullName 'bin\Hostx64\x64\cl.exe'
$scratch = Join-Path $projectRoot 'work\text-bridge-build'
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'bin') -Force | Out-Null
$source = Get-Content -LiteralPath (Join-Path $projectRoot 'app\Microphone.cs') -Raw
if ($source -notmatch 'AssemblyVersion\("([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)"\)') { throw 'Product version is missing.' }
$version = $Matches[1]
$versionHeader = Join-Path $scratch 'TextBridgeVersion.h'
Set-Content -LiteralPath $versionHeader -Value ('#define VOXFIX_BRIDGE_VERSION L"' + $version + '"') -Encoding ASCII
$outputDirectory = if ($BuildTests) { Join-Path $projectRoot 'work\native-text-bridge-test' } else { Join-Path $projectRoot 'bin' }
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$output = Join-Path $outputDirectory "VoxFix.TextBridge.$version.dll"
$options = @('/nologo', '/EHsc', '/std:c++17', '/W4', '/WX', '/MT', '/utf-8', '/LD', "/FI$versionHeader", "/I$($vc.FullName)\include")
foreach ($header in @('ucrt','um','shared','winrt')) { $options += "/I$($sdk.FullName)\$header" }
$link = @('/link', "/LIBPATH:$($vc.FullName)\lib\x64")
foreach ($library in @('ucrt\x64','um\x64')) { $link += "/LIBPATH:$sdkRoot\Lib\$($sdk.Name)\$library" }
$link += @('ole32.lib','uuid.lib','user32.lib')
$build = @($options) + @("/Fe$output", "/Fo$scratch\TextBridge.obj", (Join-Path $projectRoot 'app\native\TextBridge.cpp')) + $link + "/IMPLIB:$scratch\TextBridge.lib"
& $compiler @build
if ($LASTEXITCODE -ne 0) { throw 'Native text bridge build failed.' }
if ($BuildTests) {
    $testDirectory = Join-Path $projectRoot 'work\native-text-bridge-test'
    New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
    $testOptions = @($options | Where-Object { $_ -ne '/LD' }) + @("/Fe$testDirectory\VoxFix Desktop.exe", "/Fo$testDirectory\test.obj", (Join-Path $projectRoot 'tools\test_text_bridge.cpp')) + $link
    & $compiler @testOptions
    if ($LASTEXITCODE -ne 0) { throw 'Native text bridge test build failed.' }
}
