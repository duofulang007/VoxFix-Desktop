$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build_text_bridge.ps1') -BuildTests
$directory = Join-Path $projectRoot 'work\native-text-bridge-test'
$executable = Join-Path $directory 'VoxFix Desktop.exe'
$prior = [Environment]::GetEnvironmentVariable('VOXFIX_TEST_STANDARD_EDIT','Process')
try {
    foreach ($mode in @('plain','rich')) {
        [Environment]::SetEnvironmentVariable('VOXFIX_TEST_STANDARD_EDIT',$(if ($mode -eq 'plain') {'1'} else {$null}),'Process')
        $output = & $executable 2>&1 | Out-String
        $testExit = $LASTEXITCODE
        $output | Set-Content -LiteralPath (Join-Path $directory "$mode.txt")
        if ($testExit -ne 0 -or $output -notmatch '(?m)^PASS\s*$') { throw "$mode text bridge test failed: $output" }
        Write-Output "$mode text bridge PASS"
    }
} finally { [Environment]::SetEnvironmentVariable('VOXFIX_TEST_STANDARD_EDIT',$prior,'Process') }
