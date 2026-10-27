$ErrorActionPreference = 'Stop'
Import-Module ScheduledTasks
$installer = Join-Path $PSScriptRoot 'install-shortcuts.ps1'
$fixtureExe = Join-Path (Split-Path $PSScriptRoot -Parent) 'bin\VoxFix Desktop.exe'
$global:VoxFixInstallerTestState = @{ checks=0 }
try {
function Assert($ok, $message) { if (-not $ok) { throw $message }; $global:VoxFixInstallerTestState.checks++ }
# Keep all mutations in memory; task construction still uses the platform cmdlets.
function Get-ItemProperty { param($LiteralPath, $ErrorAction) [pscustomobject]@{ 'VoxFix Desktop' = $global:VoxFixInstallerTestState.oldRun } }
function Remove-ItemProperty { param($LiteralPath, $Name) $global:VoxFixInstallerTestState.oldRun = $null }
function Get-ScheduledTask { param($TaskName, $TaskPath, $ErrorAction) $global:VoxFixInstallerTestState.storedTask }
function Register-ScheduledTask {
    param($TaskName, $TaskPath, $Action, $Trigger, $Principal, $Settings, $Description, [switch]$Force)
    if ($global:VoxFixInstallerTestState.failRegister) { throw 'Synthetic registration failure' }
    # Windows may return the short account name rather than the supplied qualified name.
    $Principal.UserId = [Environment]::UserName
    if ($global:VoxFixInstallerTestState.badReadback) { $Settings.Enabled = $false }
    $global:VoxFixInstallerTestState.storedTask = [pscustomobject]@{ Actions=@($Action); Triggers=@($Trigger); Principal=$Principal; Settings=$Settings }
}
function Unregister-ScheduledTask { param($TaskName, $TaskPath, $Confirm) $global:VoxFixInstallerTestState.storedTask = $null }
function Test-Path { param($LiteralPath) -not $global:VoxFixInstallerTestState.missingExe }
function Reset-Fixture {
    $global:VoxFixInstallerTestState.storedTask=$null; $global:VoxFixInstallerTestState.oldRun=$null
    $global:VoxFixInstallerTestState.failRegister=$false; $global:VoxFixInstallerTestState.badReadback=$false; $global:VoxFixInstallerTestState.missingExe=$false
}
function Expect-Failure($pattern) {
    $caught=$false
    try { & $installer | Out-Null } catch { $caught=$_.Exception.Message -like $pattern }
    Assert $caught "Expected failure: $pattern"
}
Reset-Fixture
& $installer | Out-Null
Assert ($global:VoxFixInstallerTestState.storedTask.Principal.LogonType -eq 'Interactive' -and $global:VoxFixInstallerTestState.storedTask.Principal.RunLevel -eq 'Limited') 'Must use the logged-in ordinary user'
Assert ($global:VoxFixInstallerTestState.storedTask.Settings.RestartCount -eq 3 -and $global:VoxFixInstallerTestState.storedTask.Settings.RestartInterval -eq 'PT1M') 'Must retry failures'
Assert ($global:VoxFixInstallerTestState.storedTask.Settings.ExecutionTimeLimit -eq 'PT0S' -and -not $global:VoxFixInstallerTestState.storedTask.Settings.DisallowStartIfOnBatteries -and -not $global:VoxFixInstallerTestState.storedTask.Settings.StopIfGoingOnBatteries) 'Must stay running on battery and beyond three days'
Assert ($global:VoxFixInstallerTestState.storedTask.Settings.MultipleInstances -eq 'IgnoreNew') 'Must not start duplicate tasks'
Assert ($global:VoxFixInstallerTestState.storedTask.Triggers[0].CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger') 'Must trigger at login'
& $installer | Out-Null
Assert ($null -ne $global:VoxFixInstallerTestState.storedTask) 'Reinstall accepts the same user returned as a short name'
foreach ($legacyArgument in @('--background','--hotkey-host')) {
    Reset-Fixture
    $global:VoxFixInstallerTestState.oldRun='"' + $fixtureExe + '" ' + $legacyArgument
    & $installer | Out-Null
    Assert ($null -eq $global:VoxFixInstallerTestState.oldRun -and $null -ne $global:VoxFixInstallerTestState.storedTask) 'Migrate old same-path startup entries'
}
Reset-Fixture
$global:VoxFixInstallerTestState.oldRun='"C:\Other\VoxFix Desktop.exe" --background'
Expect-Failure '*points elsewhere*'
Assert ($null -ne $global:VoxFixInstallerTestState.oldRun -and $null -eq $global:VoxFixInstallerTestState.storedTask) 'Preserve conflicting startup entries'
Reset-Fixture
& $installer | Out-Null
$global:VoxFixInstallerTestState.storedTask.Actions[0].Execute='C:\Other\VoxFix.exe'
Expect-Failure '*points elsewhere*'
Assert ($global:VoxFixInstallerTestState.storedTask.Actions[0].Execute -eq 'C:\Other\VoxFix.exe') 'Preserve unrelated scheduled tasks'
foreach ($failure in @('failRegister','badReadback')) {
    Reset-Fixture
    $global:VoxFixInstallerTestState.oldRun='"' + $fixtureExe + '" --background'
    $global:VoxFixInstallerTestState[$failure] = $true
    Expect-Failure $(if ($failure -eq 'failRegister') { '*registration failure*' } else { '*verification failed*' })
    Assert ($null -ne $global:VoxFixInstallerTestState.oldRun) 'Keep old startup until replacement is verified'
}
Reset-Fixture
$global:VoxFixInstallerTestState.missingExe=$true
Expect-Failure '*Build VoxFix*'
Assert ($null -eq $global:VoxFixInstallerTestState.storedTask) 'Missing executable must not create a task'
Reset-Fixture
& $installer | Out-Null
$global:VoxFixInstallerTestState.oldRun='"' + $fixtureExe + '" --hotkey-host'
$global:VoxFixInstallerTestState.missingExe=$true
& $installer -Remove | Out-Null
Assert ($null -eq $global:VoxFixInstallerTestState.storedTask -and $null -eq $global:VoxFixInstallerTestState.oldRun) 'Removal clears both owned startup mechanisms'
"PASS: $($global:VoxFixInstallerTestState.checks) login installer checks; no registry or scheduled task mutations"

} finally { Remove-Variable -Scope Global -Name VoxFixInstallerTestState }
