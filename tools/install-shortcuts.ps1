param([switch]$Remove)
$ErrorActionPreference = 'Stop'
$shortcutRoot = Split-Path -Parent $PSScriptRoot
$shortcutExe = Join-Path $shortcutRoot 'bin\VoxFix Desktop.exe'
$runPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'VoxFix Desktop'
$taskName = 'VoxFix Desktop Hotkey Host'
$loginUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$loginSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
function Test-LoginUser([string]$account) {
    if ($account -eq $loginSid) { return $true }
    try { return ([Security.Principal.NTAccount]$account).Translate([Security.Principal.SecurityIdentifier]).Value -eq $loginSid }
    catch { return $false }
}
$runValue = '"' + $shortcutExe + '" --hotkey-host'
$legacyRunValue = '"' + $shortcutExe + '" --background'
$runProperties = Get-ItemProperty -LiteralPath $runPath -ErrorAction SilentlyContinue
$previous = if ($runProperties) { $runProperties.PSObject.Properties[$runName].Value }
if ($previous -and $previous -ne $runValue -and $previous -ne $legacyRunValue) { throw 'An existing VoxFix login entry points elsewhere; preserved without changes.' }
$task = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction SilentlyContinue
if ($task -and (@($task.Actions).Count -ne 1 -or $task.Actions[0].Execute -ne $shortcutExe -or
    $task.Actions[0].Arguments -ne '--hotkey-host' -or -not (Test-LoginUser $task.Principal.UserId))) {
    throw 'An existing VoxFix scheduled task points elsewhere or belongs to another user; preserved without changes.'
}
if ($Remove) {
    if ($task) { Unregister-ScheduledTask -TaskName $taskName -TaskPath '\' -Confirm:$false }
    if ($previous) { Remove-ItemProperty -LiteralPath $runPath -Name $runName }
    if (Test-Path -LiteralPath $shortcutExe) { & $shortcutExe --stop-hotkey-host 2>&1 | Out-Null }
    Write-Output 'Login startup removed. Exit VoxFix to release Win+H and restore normal Escape/Ctrl+C.'
    return
}
if (-not (Test-Path -LiteralPath $shortcutExe)) { throw 'Build VoxFix before installing shortcuts.' }
$action = New-ScheduledTaskAction -Execute $shortcutExe -Argument '--hotkey-host' -WorkingDirectory $shortcutRoot
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $loginUser
$principal = New-ScheduledTaskPrincipal -UserId $loginUser -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
    -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)
Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'VoxFix Win+H login entry; warm the app without recording.' -Force | Out-Null
$installed = Get-ScheduledTask -TaskName $taskName -TaskPath '\'
if (@($installed.Actions).Count -ne 1 -or $installed.Actions[0].Execute -ne $shortcutExe -or
    $installed.Actions[0].Arguments -ne '--hotkey-host' -or $installed.Actions[0].WorkingDirectory -ne $shortcutRoot -or
    -not (Test-LoginUser $installed.Principal.UserId) -or $installed.Principal.LogonType -ne 'Interactive' -or
    $installed.Principal.RunLevel -ne 'Limited' -or @($installed.Triggers).Count -ne 1 -or
    $installed.Triggers[0].CimClass.CimClassName -ne 'MSFT_TaskLogonTrigger' -or
    -not (Test-LoginUser $installed.Triggers[0].UserId) -or -not $installed.Settings.Enabled -or
    $installed.Settings.RestartCount -ne 3 -or $installed.Settings.ExecutionTimeLimit -ne 'PT0S') {
    throw 'Scheduled login task verification failed; the old login entry has been preserved.'
}
# Remove the old entry only after the replacement is registered and verified.
if ($previous) { Remove-ItemProperty -LiteralPath $runPath -Name $runName }
Write-Output 'VoxFix hotkey host will start at login and warm the app without recording. Win+H starts/pauses VoxFix and recovers a closed app; Escape dismisses. Ctrl+C stays with the current app.'
