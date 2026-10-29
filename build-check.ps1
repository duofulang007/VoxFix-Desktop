$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $projectRoot 'bin\VoxFix Desktop-check.exe'
$icon = Join-Path $projectRoot 'app\assets\VoxFix Desktop.ico'
$compileOptions = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:Accessibility.dll', "/out:$executable")
$compileOptions += @("/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll", "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll", "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll")
$compileOptions += "/resource:$projectRoot\app\assets\graphite-material.png,VoxFix.GraphiteMaterial"
$compileOptions += "/resource:$projectRoot\app\assets\chaos.vert,VoxFix.ChaosVertex"
$compileOptions += "/resource:$projectRoot\app\assets\chaos.frag,VoxFix.ChaosFragment"
$compileOptions += "/resource:$projectRoot\app\assets\ChatAni-LICENSE.txt,VoxFix.ChatAniLicense"
$compileOptions += "/resource:$projectRoot\app\assets\Ashima-Noise-LICENSE.txt,VoxFix.AshimaNoiseLicense"
& $compiler @compileOptions "/win32icon:$icon" "$projectRoot\app\Microphone.cs" "$projectRoot\app\HotkeyHost.cs" "$projectRoot\app\InPlaceText.cs" "$projectRoot\app\Theme.cs" "$projectRoot\app\ChaosGpu.cs"
if ($LASTEXITCODE -ne 0) { throw 'Check build failed' }
Write-Output 'CHECK BUILD OK'
