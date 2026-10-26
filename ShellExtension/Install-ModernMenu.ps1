param([switch]$Remove, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
$log = Join-Path $PSScriptRoot 'modern-menu.log'
try {
    if ($Remove) {
        Get-AppxPackage -Name 'SoundSwitchQuick.DisplayMenu.Preview' | Remove-AppxPackage
    } else {
        $package = Join-Path $PSScriptRoot 'SoundSwitchQuick.Menu.msix'
        if (-not (Test-Path $package)) { throw 'SoundSwitchQuick.Menu.msix is missing. Extract the entire ZIP.' }
        Add-AppxPackage -Path $package -ExternalLocation $PSScriptRoot -AllowUnsigned
        if (-not (Get-AppxPackage -Name 'SoundSwitchQuick.DisplayMenu.Preview')) { throw 'Registration was not confirmed.' }
    }
    "$(Get-Date -Format o) Success. Remove=$Remove" | Out-File $log -Encoding utf8
    exit 0
} catch {
    $_ | Out-String | Out-File $log -Encoding utf8
    # Only request elevation for access-denied / elevation-required failures, not arbitrary errors.
    $accessDenied = $_.Exception.HResult -in @(-2147024891, -2147024156) -or $_.ToString() -match '0x80070005|0x800702E4|administrator|elevation'
    if (-not $Elevated -and $accessDenied) {
        try {
            $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Elevated'
            if ($Remove) { $arguments += ' -Remove' }
            $child = Start-Process powershell.exe -ArgumentList $arguments -Verb RunAs -Wait -PassThru
            exit $child.ExitCode
        } catch { $_ | Out-String | Add-Content $log }
    }
    exit 1
}
