# ============================================================================
#  restart-fleet.ps1  —  the SAFE way to restart the 5 PM2 bot hosts.
#  Plain `pm2 restart host-*` can leave ORPHAN ConsoleApp processes on Windows
#  (the self-contained single-file exe doesn't always die on a graceful stop),
#  and each orphan keeps its Discord token connected = token war = bots flapping
#  / posting twice. This stops the PM2 apps, HARD-kills every ConsoleApp process
#  to a clean slate, then starts exactly 5 fresh. Use this after an internet
#  outage or any time you need to bounce the fleet.
#  Run:  powershell -ExecutionPolicy Bypass -File restart-fleet.ps1
# ============================================================================
$ErrorActionPreference = 'SilentlyContinue'
$hosts = 'host-A','host-B','host-C','host-D','host-E'

Write-Host "1) Stopping PM2 host apps (disables autorestart so kills stick)..."
# Loop one name per call — passing the $hosts array to `pm2 stop $hosts` gets mangled into a
# single "host-A host-B ..." arg on Windows and PM2 reports "not found".
foreach ($h in $hosts) { pm2 stop $h | Out-Null }

Write-Host "2) Hard-killing ALL ConsoleApp processes (clears orphans)..."
Get-Process 'SysBot.Pokemon.ConsoleApp' -EA SilentlyContinue | Stop-Process -Force
Start-Sleep 4
$left = (Get-Process 'SysBot.Pokemon.ConsoleApp' -EA SilentlyContinue | Measure-Object).Count
Write-Host "   ConsoleApp processes remaining: $left (want 0)"

Write-Host "3) Also killing any stray WinForms bots (must never run with the hosts)..."
Get-Process 'PKM-Universe Bot' -EA SilentlyContinue | Stop-Process -Force

Write-Host "4) Starting 5 fresh via PM2..."
foreach ($h in $hosts) { pm2 restart $h | Out-Null }
Start-Sleep 35

$h = Get-Process 'SysBot.Pokemon.ConsoleApp' -EA SilentlyContinue
$gw = 0; foreach($p in $h){ $gw += (Get-NetTCPConnection -OwningProcess $p.Id -State Established -EA SilentlyContinue | Where-Object { $_.RemotePort -eq 443 } | Measure-Object).Count }
$wf = (Get-Process 'PKM-Universe Bot' -EA SilentlyContinue | Measure-Object).Count
Write-Host ("DONE -> hosts: {0}/5   bots: {1}/10   WinForms: {2}  (want 5 / 10 / 0)" -f ($h|Measure-Object).Count, $gw, $wf)
if ((($h|Measure-Object).Count -eq 5) -and ($gw -eq 10) -and ($wf -eq 0)) { Write-Host "All good." } else { Write-Host "!! Not clean - re-run this script." }
