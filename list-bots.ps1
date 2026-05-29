Get-Process | Where-Object {$_.ProcessName -like '*PKM*'} | Select-Object Id, ProcessName | Format-Table -AutoSize
