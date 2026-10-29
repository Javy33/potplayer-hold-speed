<#
Builds PotPlayerHoldSpeed.exe into %LOCALAPPDATA%\PotPlayerHoldSpeed and routes PotPlayer's
shortcuts and file associations through it, so the hold-to-speed helper starts with PotPlayer.

- Associations: per-user overrides under HKCU\Software\Classes (HKLM is left untouched).
- Shortcuts: desktop / Start menu / taskbar .lnk files that point at PotPlayer. The all-users
  ones need an elevated prompt.
- Original shortcut targets are saved in install.json; uninstall.ps1 reverts everything.
Safe to re-run (e.g. after editing PotPlayerHoldSpeed.cs).
#>
param([string]$PotPlayer)   # defaults to the path PotPlayer's installer recorded
$ErrorActionPreference = 'Stop'

if (-not $PotPlayer) {
    foreach ($k in 'HKLM:\SOFTWARE\DAUM\PotPlayer64', 'HKCU:\SOFTWARE\DAUM\PotPlayer64', 'HKLM:\SOFTWARE\DAUM\PotPlayer', 'HKCU:\SOFTWARE\DAUM\PotPlayer', 'HKLM:\SOFTWARE\WOW6432Node\DAUM\PotPlayer') {
        $p = (Get-ItemProperty $k -ErrorAction SilentlyContinue).ProgramPath
        if ($p -and (Test-Path $p)) { $PotPlayer = $p; break }
    }
    if (-not $PotPlayer) { throw 'PotPlayer not found; pass -PotPlayer <path to PotPlayerMini64.exe>' }
}

$dir = Join-Path $env:LOCALAPPDATA 'PotPlayerHoldSpeed'
$exe = Join-Path $dir 'PotPlayerHoldSpeed.exe'
$stateFile = Join-Path $dir 'install.json'
if (-not (Test-Path $PotPlayer)) { throw "PotPlayer not found: $PotPlayer" }
New-Item -ItemType Directory -Force $dir | Out-Null

$state = [ordered]@{ potPlayer = $PotPlayer; classKeys = @(); shortcuts = @() }
if (Test-Path $stateFile) {
    $old = Get-Content -Raw $stateFile | ConvertFrom-Json
    $state.classKeys = @($old.classKeys)
    $state.shortcuts = @($old.shortcuts)
}

# Build
Get-Process PotPlayerHoldSpeed -ErrorAction SilentlyContinue | Stop-Process -Force
$csc = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
& $csc /nologo /target:winexe /optimize "/out:$exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll (Join-Path $PSScriptRoot 'PotPlayerHoldSpeed.cs')
if ($LASTEXITCODE -ne 0) { throw 'csc failed' }
[IO.File]::WriteAllText((Join-Path $dir 'target.txt'), $PotPlayer)

# File associations: copy every PotPlayer verb into HKCU with the exe swapped for the launcher.
$exePattern = '"?' + [regex]::Escape($PotPlayer) + '"?'
$classCount = 0
foreach ($cls in Get-ChildItem 'HKLM:\SOFTWARE\Classes' | Where-Object { $_.PSChildName -like 'PotPlayer*' }) {
    $id = $cls.PSChildName
    $shell = Join-Path $cls.PSPath 'shell'
    if (-not (Test-Path $shell)) { continue }
    foreach ($verb in Get-ChildItem $shell) {
        $cmd = (Get-ItemProperty (Join-Path $verb.PSPath 'command') -ErrorAction SilentlyContinue).'(default)'
        if (-not $cmd -or $cmd -notmatch $exePattern) { continue }
        $userClass = "HKCU:\Software\Classes\$id"
        if (-not (Test-Path $userClass)) { $state.classKeys += $id }
        $key = "$userClass\shell\$($verb.PSChildName)\command"
        if (-not (Test-Path $key)) { New-Item -Force $key | Out-Null }
        Set-ItemProperty $key '(default)' ([regex]::Replace($cmd, $exePattern, { "`"$exe`"" }, 'IgnoreCase'))
        $classCount++
    }
}
$state.classKeys = @($state.classKeys | Select-Object -Unique)

# Shortcuts
$shell = New-Object -ComObject WScript.Shell
$roots = @(
    [Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonDesktopDirectory'),
    [Environment]::GetFolderPath('StartMenu'), [Environment]::GetFolderPath('CommonStartMenu'),
    "$env:APPDATA\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")
$changed = @(); $failed = @()
foreach ($lnk in Get-ChildItem $roots -Recurse -Filter *.lnk -ErrorAction SilentlyContinue) {
    $sc = $shell.CreateShortcut($lnk.FullName)
    if ($sc.TargetPath -ne $PotPlayer) { continue }
    if (-not ($state.shortcuts | Where-Object { $_.path -eq $lnk.FullName })) {
        $state.shortcuts += [pscustomobject]@{ path = $lnk.FullName; target = $sc.TargetPath; icon = $sc.IconLocation }
    }
    $sc.TargetPath = $exe
    $sc.IconLocation = "$PotPlayer,0"
    try { $sc.Save(); $changed += $lnk.FullName } catch { $failed += $lnk.FullName }
}

$state | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $stateFile

Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);'
[Win32.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero) # SHCNE_ASSOCCHANGED

"Launcher: $exe"
"Association verbs redirected: $classCount"
$changed | ForEach-Object { "Shortcut redirected: $_" }
$failed | ForEach-Object { "Shortcut NOT changed (run elevated): $_" }
