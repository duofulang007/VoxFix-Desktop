param([string]$Events, [switch]$ReplaceSelection)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $projectRoot 'work\webview-text-test'
$sdk = Join-Path $directory 'sdk'
$core = Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$forms = Join-Path $sdk 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $sdk 'runtimes\win-x64\native\WebView2Loader.dll'
foreach ($file in @($core,$forms,$loader)) {
    if (-not (Test-Path -LiteralPath $file)) { throw 'Missing isolated WebView2 test SDK. This script does not download or install dependencies.' }
    $signature = Get-AuthenticodeSignature -LiteralPath $file
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*CN=Microsoft Corporation,*') { throw "Invalid Microsoft test component signature: $file" }
    Copy-Item -LiteralPath $file -Destination $directory
}
$application = Join-Path $projectRoot 'bin\VoxFix Desktop.exe'
$version = [Reflection.AssemblyName]::GetAssemblyName($application).Version.ToString()
$bridge = Join-Path $projectRoot "bin\VoxFix.TextBridge.$version.dll"
$testBridge = Join-Path $directory "VoxFix.TextBridge.$version.dll"
if (-not (Test-Path -LiteralPath $testBridge) -or (Get-FileHash -LiteralPath $bridge).Hash -ne (Get-FileHash -LiteralPath $testBridge).Hash) {
    Copy-Item -LiteralPath $bridge -Destination $testBridge
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $directory 'VoxFix Desktop.exe'
& $compiler /nologo /target:exe /platform:x64 /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$core" "/r:$forms" "/out:$executable" (Join-Path $PSScriptRoot 'test_text_bridge_browser.cs')
if ($LASTEXITCODE -ne 0) { throw 'Browser test build failed.' }
if (-not $Events) { $Events = Join-Path $PSScriptRoot 'fixtures\text-bridge-updates.json' }
foreach ($mode in @('textarea','editable')) {
    $started = Get-Date
    $output = & $executable $mode $Events $(if ($ReplaceSelection) { 'selection' } else { 'append' }) 2>&1 | Out-String
    $testExit = $LASTEXITCODE
    $output | Set-Content -LiteralPath (Join-Path $directory "$mode-run.log")
    if ($testExit -ne 0) { throw "$mode browser test failed: $output" }
    $resultFile = Get-Item -LiteralPath (Join-Path $directory "$mode-result.json")
    $result = Get-Content -LiteralPath $resultFile.FullName -Raw | ConvertFrom-Json
    if ($resultFile.LastWriteTime -lt $started -or -not $result.pass) { throw 'Stale or unsuccessful browser test result.' }
    Write-Output $output
}
