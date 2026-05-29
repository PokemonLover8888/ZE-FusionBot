Get-ChildItem 'C:\Users\ericr\OneDrive\Desktop' -Directory | Where-Object { $_.Name -match 'LGPE' } | ForEach-Object { Write-Host $_.Name }
