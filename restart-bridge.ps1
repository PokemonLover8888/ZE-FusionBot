# Find and stop current trade-bridge process on port 3456
$conn = Get-NetTCPConnection -LocalPort 3456 -State Listen -ErrorAction SilentlyContinue
if ($conn) {
    $procId = $conn.OwningProcess
    Write-Host "Stopping trade-bridge PID: $procId"
    Stop-Process -Id $procId -Force
    Start-Sleep -Seconds 2
} else {
    Write-Host "No process found on port 3456"
}

# Start new instance
Write-Host "Starting trade-bridge-api.js..."
$proc = Start-Process -FilePath 'node' -ArgumentList 'C:\Users\ericr\OneDrive\Desktop\ericscyndaquil\trade-bridge-api.js' -WindowStyle Hidden -PassThru
Write-Host "Started with PID: $($proc.Id)"

Start-Sleep -Seconds 2

# Verify
$newConn = Get-NetTCPConnection -LocalPort 3456 -State Listen -ErrorAction SilentlyContinue
if ($newConn) {
    Write-Host "[OK] Trade-bridge running on port 3456 (PID: $($newConn.OwningProcess))" -ForegroundColor Green
} else {
    Write-Host "[FAIL] Trade-bridge not listening on port 3456!" -ForegroundColor Red
}
