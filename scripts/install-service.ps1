<#
PowerShell helper to install/uninstall WriteCraftAI as a Windows service.
Run as Administrator.

Usage:
  .\install-service.ps1 -Install -ExePath "C:\Program Files\WriteCraftAI\WriteCraftAI.exe"
  .\install-service.ps1 -Uninstall -ServiceName "WriteCraftAI"

This script demonstrates two approaches:
 1) sc.exe native service wrapper (simple but limited)
 2) NSSM (recommended for non-service-aware exes) - commented out

Note: Installer should set the --service flag when starting the exe as a service.
#>
param(
    [switch]$Install,
    [switch]$Uninstall,
    [string]$ExePath = "C:\Program Files\WriteCraftAI\WriteCraftAI.exe",
    [string]$ServiceName = "WriteCraftAI",
    [string]$DisplayName = "WriteCraft.AI Local Server",
    [string]$Description = "Local WriteCraft.AI server (runs LLM inference locally).",
    [switch]$UseNssm
)

function Install-Sc {
    Write-Host "Installing service '$ServiceName' using sc.exe..." -ForegroundColor Cyan
    if (-not (Test-Path $ExePath)) { Write-Error "Exe not found: $ExePath"; return }

    $binpath = '"' + $ExePath + '" --service'
    sc.exe create $ServiceName binPath= $binpath start= auto DisplayName= "$DisplayName" | Out-Null
    sc.exe description $ServiceName "$Description" | Out-Null
    sc.exe start $ServiceName
    Write-Host "Service installed and started. Use 'sc query $ServiceName' to check status." -ForegroundColor Green
}

function Uninstall-Sc {
    Write-Host "Stopping and removing service '$ServiceName'..." -ForegroundColor Yellow
    sc.exe stop $ServiceName | Out-Null
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service removed." -ForegroundColor Green
}

function Install-Nssm {
    Write-Host "Installing service using NSSM (you must have nssm.exe in PATH)..." -ForegroundColor Cyan
    if (-not (Test-Path $ExePath)) { Write-Error "Exe not found: $ExePath"; return }
    $nssm = "nssm"
    $nssmArgs = @("install", $ServiceName, $ExePath, "--service")
    & $nssm @nssmArgs
    & $nssm set $ServiceName AppStdout "C:\ProgramData\WriteCraftAI\stdout.log"
    & $nssm set $ServiceName AppStderr "C:\ProgramData\WriteCraftAI\stderr.log"
    & $nssm start $ServiceName
    Write-Host "NSSM service installed and started." -ForegroundColor Green
}

if ($Install) {
    if ($UseNssm) { Install-Nssm } else { Install-Sc }
}

if ($Uninstall) {
    if ($UseNssm) { Write-Host "Uninstalling via NSSM is manual. Use nssm remove <service> confirm" } else { Uninstall-Sc }
}
