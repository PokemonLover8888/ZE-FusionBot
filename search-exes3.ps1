$exes = Get-ChildItem 'C:/Users/ericr/source/repos/ZE-FusionBot' -Recurse -Filter '*.exe' | Where-Object { $_.Name -match 'PKM|ZE_Fusion' }
foreach ($exe in $exes) {
    try {
        $bytes = [IO.File]::ReadAllBytes($exe.FullName)
        $str = [Text.Encoding]::Unicode.GetString($bytes)
        if ($str -match 'BATCH TRADE REQUEST') {
            Write-Host "FOUND (Unicode) BATCH TRADE REQUEST in: $($exe.FullName)"
        }
        if ($str -match 'Trade Contents') {
            Write-Host "FOUND (Unicode) Trade Contents in: $($exe.FullName)"
        }
        if ($str -match 'queued for trade') {
            Write-Host "FOUND (Unicode) queued for trade in: $($exe.FullName)"
        }
    } catch { }
}
Write-Host 'Search complete'
