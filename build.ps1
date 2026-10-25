$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
New-Item -ItemType Directory -Path (Join-Path $projectRoot 'bin') -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$executable = Join-Path $projectRoot 'bin\VoxFix Desktop.exe'
$icon = Join-Path $projectRoot 'app\assets\VoxFix Desktop.ico'
$compileOptions = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:Accessibility.dll', "/out:$executable")
$compileOptions += @("/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationClient.dll", "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\UIAutomationTypes.dll", "/r:$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll")
$compileOptions += "/resource:$projectRoot\app\assets\graphite-material.png,VoxFix.GraphiteMaterial"
if (-not (Test-Path -LiteralPath $icon)) {
    & $compiler @compileOptions "$projectRoot\app\Microphone.cs" "$projectRoot\app\HotkeyHost.cs"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Start-Process -FilePath $executable -ArgumentList '--export-icon' -WindowStyle Hidden -Wait
}
& $compiler @compileOptions "/win32icon:$icon" "$projectRoot\app\Microphone.cs" "$projectRoot\app\HotkeyHost.cs"
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $projectRoot 'VoxFix Desktop.lnk'))
$shortcut.TargetPath = $executable
$shortcut.WorkingDirectory = $projectRoot
$shortcut.IconLocation = "$executable,0"
$shortcut.Save()
