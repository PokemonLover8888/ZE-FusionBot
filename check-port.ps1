Start-Sleep -Seconds 2
$conns = Get-NetTCPConnection -LocalPort 3456 -ErrorAction SilentlyContinue
foreach ($c in $conns) {
    if ($c.OwningProcess -ne 0) {
        Write-Output "Port 3456 owned by PID: $($c.OwningProcess)"
    }
}
