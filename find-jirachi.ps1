$procs = Get-Process -Name "PKM-Universe Bot" -ErrorAction SilentlyContinue
foreach ($proc in $procs) {
    $title = $proc.MainWindowTitle
    Write-Output "$($proc.Id) - $title"
}
