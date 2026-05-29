Get-ChildItem 'C:/Users/ericr/source/repos/ZE-FusionBot' -Recurse -Filter '*.cs' | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -match 'Trade Contents') {
        Write-Host "FOUND Trade Contents in: $($_.FullName)"
    }
    if ($content -match 'PKM Universe Trading') {
        Write-Host "FOUND PKM Universe Trading in: $($_.FullName)"
    }
    if ($content -match 'queued for trade') {
        Write-Host "FOUND queued for trade in: $($_.FullName)"
    }
    if ($content -match 'Est\. Wait') {
        Write-Host "FOUND Est. Wait in: $($_.FullName)"
    }
}
Write-Host 'Search complete'
