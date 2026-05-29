$exes = Get-ChildItem 'C:/Users/ericr/source/repos/ZE-FusionBot' -Recurse -Filter '*.exe' | Where-Object { $_.Name -match 'PKM|ZE_Fusion' }
foreach ($exe in $exes) {
    try {
        $bytes = [IO.File]::ReadAllBytes($exe.FullName)
        $str = [Text.Encoding]::UTF8.GetString($bytes)
        if ($str -match 'Trade Contents') {
            Write-Host "FOUND Trade Contents in: $($exe.FullName)"
        }
        if ($str -match 'PKM Universe Trading') {
            Write-Host "FOUND PKM Universe Trading in: $($exe.FullName)"
        }
        if ($str -match 'queued for trade') {
            Write-Host "FOUND queued for trade in: $($exe.FullName)"
        }
    } catch { }
}
Write-Host 'Search complete'
