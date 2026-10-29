$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $projectRoot 'bin\VoxFix Chaos Motion Test.exe'
& $compiler /nologo /target:winexe /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$executable" (Join-Path $PSScriptRoot 'test_chaos_motion.cs')
if ($LASTEXITCODE -ne 0) { throw 'Motion fixture build failed' }
$out = & $executable 2>&1 | Out-String
$code = $LASTEXITCODE
Write-Output $out
Get-Content -LiteralPath (Join-Path $projectRoot 'work\chaos-motion-test.txt')
if ($code -ne 0) { throw 'Motion fixture failed' }
