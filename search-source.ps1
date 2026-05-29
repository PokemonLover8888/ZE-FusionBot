Get-ChildItem 'C:/Users/ericr/source/repos/ZE-FusionBot' -Recurse -Filter '*.cs' | ForEach-Object {
    $content = Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue
    if ($content -match 'BATCH TRADE REQUEST') {
        Write-Host "FOUND in: $($_.FullName)"
    }
}
Write-Host 'Search complete'
