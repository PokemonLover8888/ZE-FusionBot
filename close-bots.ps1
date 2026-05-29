Get-Process | Where-Object {$_.ProcessName -like '*PKM-Universe*'} | Stop-Process -Force
Write-Host "All bots closed"
