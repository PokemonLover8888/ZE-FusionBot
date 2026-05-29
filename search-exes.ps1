$exes = Get-ChildItem 'C:/Users/ericr/source/repos/ZE-FusionBot' -Recurse -Filter '*.exe' | Where-Object { $_.Name -match 'PKM|ZE_Fusion' }
foreach ($exe in $exes) {
    try {
        $bytes = [IO.File]::ReadAllBytes($exe.FullName)
        $str = [Text.Encoding]::UTF8.GetString($bytes)
        if ($str -match 'BATCH TRADE REQUEST') {
            Write-Host "FOUND in: $($exe.FullName)"
        }
    } catch { }
}
Write-Host 'Search complete'
