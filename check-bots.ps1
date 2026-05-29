Get-Process | Where-Object { $_.ProcessName -like '*PKM*' -or $_.ProcessName -like '*Universe*' } | Select-Object Id, ProcessName | Format-Table -AutoSize
