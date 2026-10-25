param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$shortcutRoot = Split-Path -Parent $PSScriptRoot
$shortcutExe = Join-Path $shortcutRoot 'bin\VoxFix Desktop.exe'
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'VoxFix Desktop'
$runValue = '"' + $shortcutExe + '" --hotkey-host'
$legacyRunValue = '"' + $shortcutExe + '" --background'
$runProperties = Get-ItemProperty -LiteralPath $runPath -ErrorAction SilentlyContinue
$previous = if ($runProperties) { $runProperties.PSObject.Properties[$runName].Value }
if ($previous -and $previous -ne $runValue -and $previous -ne $legacyRunValue) { throw 'An existing VoxFix login entry points elsewhere; preserved without changes.' }
if ($Remove) {
    if ($previous) { Remove-ItemProperty -LiteralPath $runPath -Name $runName }
    if (Test-Path -LiteralPath $shortcutExe) { & $shortcutExe --stop-hotkey-host 2>&1 | Out-Null }
    Write-Output 'Login startup removed. Exit VoxFix to release Win+H and restore normal Escape/Ctrl+C.'
    return
}
if (-not (Test-Path -LiteralPath $shortcutExe)) { throw 'Build VoxFix before installing shortcuts.' }
New-Item -Path $runPath -Force | Out-Null
New-ItemProperty -LiteralPath $runPath -Name $runName -PropertyType String -Value $runValue -Force | Out-Null
if ((Get-ItemPropertyValue -LiteralPath $runPath -Name $runName) -ne $runValue) { throw 'Login entry verification failed.' }
Write-Output 'VoxFix hotkey host will start at login and warm the app without recording. Win+H starts/pauses VoxFix and recovers a closed app; Escape dismisses. Ctrl+C stays with the current app.'
