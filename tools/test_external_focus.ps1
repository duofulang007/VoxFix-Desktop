$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $projectRoot 'bin\VoxFix External Focus Test.exe'
& $compiler /nologo /target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$executable" (Join-Path $PSScriptRoot 'test_external_focus.cs')
if ($LASTEXITCODE -ne 0) { throw 'External focus fixture build failed' }
$out = & $executable 2>&1 | Out-String
$code = $LASTEXITCODE
Write-Output $out
Get-Content -LiteralPath (Join-Path $projectRoot 'work\external-focus-test.txt')
if ($code -ne 0) { throw 'External focus fixture failed' }
