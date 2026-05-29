# Stop all PKM Universe bot processes
Write-Host "Stopping all PKM Universe bot processes..."

Get-Process | Where-Object { $_.ProcessName -like "*PKM-Universe*" } | ForEach-Object {
    Write-Host "Stopping: $($_.ProcessName) (PID: $($_.Id))"
    Stop-Process -Id $_.Id -Force
}

Write-Host "All bots stopped."
