<#
Reverts install.ps1: restores PotPlayer shortcuts, removes the HKCU association overrides it
created, and deletes %LOCALAPPDATA%\PotPlayerHoldSpeed. All-users shortcuts need an elevated prompt.
#>
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:LOCALAPPDATA 'PotPlayerHoldSpeed'
$stateFile = Join-Path $dir 'install.json'
if (-not (Test-Path $stateFile)) { throw "Not installed ($stateFile missing)" }
$state = Get-Content -Raw $stateFile | ConvertFrom-Json

Get-Process PotPlayerHoldSpeed -ErrorAction SilentlyContinue | Stop-Process -Force

$shell = New-Object -ComObject WScript.Shell
$failed = @()
foreach ($s in @($state.shortcuts)) {
    if (-not (Test-Path $s.path)) { continue }
    $sc = $shell.CreateShortcut($s.path)
    $sc.TargetPath = $s.target
    $sc.IconLocation = $s.icon
    try { $sc.Save(); "Shortcut restored: $($s.path)" } catch { $failed += $s.path }
}
if ($failed) {
    $failed | ForEach-Object { "Shortcut NOT restored (run elevated): $_" }
    throw 'Some shortcuts could not be restored; nothing else was removed.'
}

foreach ($id in @($state.classKeys)) {
    $key = "HKCU:\Software\Classes\$id"
    if (Test-Path $key) { Remove-Item -Recurse $key; "Association override removed: $id" }
}
Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);'
[Win32.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Remove-Item -Recurse -Force $dir
"Uninstalled."
