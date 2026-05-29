$desktop = "C:\Users\ericr\OneDrive\Desktop"

# Read trade-bridge-api.js to get actual port mappings
$bridgePath = "C:\Users\ericr\OneDrive\Desktop\ericscyndaquil\trade-bridge-api.js"
$bridgeContent = Get-Content $bridgePath -Raw

# Parse all bot entries from trade-bridge: name, port, folder
$bridgeBots = @{}
$botMatches = [regex]::Matches($bridgeContent, "(\w+):\s*\{\s*name:\s*'([^']+)',\s*port:\s*(\d+),\s*folder:\s*'([^']+)'")
foreach ($m in $botMatches) {
    $bridgeBots[$m.Groups[1].Value] = @{
        Name = $m.Groups[2].Value
        Port = [int]$m.Groups[3].Value
        Folder = $m.Groups[1].Value
    }
}

# All 13 bot folders
$bots = @(
    @{ Name = "Celebi-SWSH-Bot";    Path = "$desktop\Celebi-SWSH-Bot";    BridgeKey = "celebi" },
    @{ Name = "Diance-PLZA-Bot";    Path = "$desktop\Diance-PLZA-Bot";    BridgeKey = "diancie" },
    @{ Name = "Flareon-LGPE-Bot";   Path = "$desktop\Flareon-LGPE-Bot";   BridgeKey = "flareon" },
    @{ Name = "Floette-PLZA-Bot";   Path = "$desktop\Floette-PLZA-Bot";   BridgeKey = "floette" },
    @{ Name = "Giratina-BDSP-Bot";  Path = "$desktop\Giratina-BDSP-Bot";  BridgeKey = "giratina" },
    @{ Name = "Glaceon-LGPE";       Path = "$desktop\Glaceon-LGPE";       BridgeKey = "glaceon" },
    @{ Name = "Hoopa-PLZA-Bot";     Path = "$desktop\Hoopa-PLZA-Bot";     BridgeKey = "hoopa" },
    @{ Name = "Jirachi-SWSH-Bot";   Path = "$desktop\Jirachi-SWSH-Bot";   BridgeKey = "jirachi" },
    @{ Name = "Landorus-PLA-Bot";   Path = "$desktop\Landorus-PLA-Bot";   BridgeKey = "landorus" },
    @{ Name = "Meloetta-SV-Bot";    Path = "$desktop\Meloetta-SV-Bot";    BridgeKey = "meloetta" },
    @{ Name = "Mew-SV-Bot";         Path = "$desktop\Mew-SV-Bot";         BridgeKey = "mew" },
    @{ Name = "Pikachu-LGPE-Bot";   Path = "$desktop\Pikachu-LGPE-Bot";   BridgeKey = "" },
    @{ Name = "Rayquaza-BDSP-Bot";  Path = "$desktop\Rayquaza-BDSP-Bot";  BridgeKey = "rayquaza" }
)

$publishedExe = "C:\Users\ericr\source\repos\ZE-FusionBot\publish-sc\PKM-Universe Bot.exe"
$publishedSize = (Get-Item $publishedExe).Length
$publishedHash = (Get-FileHash $publishedExe -Algorithm MD5).Hash

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PKM-Universe Bot Verification Report" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Published exe: $([math]::Round($publishedSize / 1MB, 1)) MB | MD5: $publishedHash"
Write-Host "Trade-bridge bots found: $($bridgeBots.Count)"
Write-Host ""

$issues = @()
$passCount = 0
$totalChecks = 0

foreach ($bot in $bots) {
    Write-Host "--- $($bot.Name) ---" -ForegroundColor Yellow

    # Check folder exists
    if (-not (Test-Path $bot.Path)) {
        Write-Host "  [FAIL] Folder not found: $($bot.Path)" -ForegroundColor Red
        $issues += "$($bot.Name): Folder not found"
        Write-Host ""
        continue
    }

    # Check exe exists and matches
    $totalChecks++
    $exePath = Join-Path $bot.Path "PKM-Universe Bot.exe"
    if (Test-Path $exePath) {
        $exeSize = (Get-Item $exePath).Length
        $exeHash = (Get-FileHash $exePath -Algorithm MD5).Hash
        if ($exeHash -eq $publishedHash) {
            Write-Host "  [OK] Exe deployed ($([math]::Round($exeSize / 1MB, 1)) MB, hash matches)" -ForegroundColor Green
            $passCount++
        } else {
            Write-Host "  [FAIL] Exe hash MISMATCH!" -ForegroundColor Red
            $issues += "$($bot.Name): Exe hash mismatch"
        }
    } else {
        Write-Host "  [FAIL] Exe not found!" -ForegroundColor Red
        $issues += "$($bot.Name): Exe not found"
    }

    # Check config.json port
    $totalChecks++
    $configPath = Join-Path $bot.Path "config.json"
    if (Test-Path $configPath) {
        $rawConfig = Get-Content $configPath -Raw
        $configPort = $null
        if ($rawConfig -match '"ControlPanelPort"\s*:\s*(\d+)') {
            $configPort = [int]$Matches[1]
        }

        if ($null -ne $configPort) {
            if ($bot.BridgeKey -eq "") {
                Write-Host "  [INFO] Config port: $configPort (not in trade-bridge)" -ForegroundColor Cyan
                $passCount++
            } elseif ($bridgeBots.ContainsKey($bot.BridgeKey)) {
                $expectedPort = $bridgeBots[$bot.BridgeKey].Port
                if ($configPort -eq $expectedPort) {
                    Write-Host "  [OK] Port: $configPort (config matches trade-bridge)" -ForegroundColor Green
                    $passCount++
                } else {
                    Write-Host "  [FAIL] Port mismatch! Config=$configPort, Trade-bridge=$expectedPort" -ForegroundColor Red
                    $issues += "$($bot.Name): Port mismatch - config=$configPort, bridge=$expectedPort"
                }
            } else {
                Write-Host "  [WARN] Config port: $configPort (bridge key '$($bot.BridgeKey)' not found)" -ForegroundColor Yellow
                $issues += "$($bot.Name): Bridge key not found"
            }
        } else {
            Write-Host "  [WARN] Could not find ControlPanelPort in config" -ForegroundColor Yellow
            $issues += "$($bot.Name): No ControlPanelPort found"
        }
    } else {
        Write-Host "  [FAIL] config.json not found!" -ForegroundColor Red
        $issues += "$($bot.Name): No config.json"
    }

    # Check Discord token exists
    $totalChecks++
    if (Test-Path $configPath) {
        $rawConfig = Get-Content $configPath -Raw
        if ($rawConfig -match '"Token"\s*:\s*"[^"]{20,}"') {
            Write-Host "  [OK] Discord token present" -ForegroundColor Green
            $passCount++
        } else {
            Write-Host "  [WARN] Discord token may be missing or empty" -ForegroundColor Yellow
            $issues += "$($bot.Name): Discord token missing"
        }
    }

    Write-Host ""
}

# Trade-bridge folder path check
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Trade-Bridge Folder Path Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$folderMatches = [regex]::Matches($bridgeContent, "folder:\s*'([^']+)'")
foreach ($match in $folderMatches) {
    $totalChecks++
    $folder = $match.Groups[1].Value -replace '/', '\'
    if (Test-Path $folder) {
        Write-Host "  [OK] $folder" -ForegroundColor Green
        $passCount++
    } else {
        Write-Host "  [FAIL] $folder (NOT FOUND)" -ForegroundColor Red
        $issues += "Trade-bridge folder missing: $folder"
    }
}

# Trade-bridge API check
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Trade-Bridge API Status" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
$totalChecks++
$conn = Get-NetTCPConnection -LocalPort 3456 -State Listen -ErrorAction SilentlyContinue
if ($conn) {
    Write-Host "  [OK] Trade-bridge API running on port 3456 (PID: $($conn.OwningProcess))" -ForegroundColor Green
    $passCount++
} else {
    Write-Host "  [FAIL] Trade-bridge API not running!" -ForegroundColor Red
    $issues += "Trade-bridge API not running on port 3456"
}

# Docker containers
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Docker Containers" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
$docker = docker ps --format "{{.Names}} | {{.Status}}" 2>&1
if ($LASTEXITCODE -eq 0) {
    foreach ($line in $docker) {
        $totalChecks++
        if ($line -match "Up") {
            Write-Host "  [OK] $line" -ForegroundColor Green
            $passCount++
        } else {
            Write-Host "  [WARN] $line" -ForegroundColor Yellow
        }
    }
    if (-not $docker) {
        Write-Host "  [WARN] No containers running" -ForegroundColor Yellow
    }
} else {
    Write-Host "  [WARN] Docker not available" -ForegroundColor Yellow
}

# Summary
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Summary: $passCount/$totalChecks checks passed" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
if ($issues.Count -eq 0) {
    Write-Host "  ALL CHECKS PASSED!" -ForegroundColor Green
} else {
    Write-Host "  $($issues.Count) issue(s) found:" -ForegroundColor Red
    foreach ($issue in $issues) {
        Write-Host "    - $issue" -ForegroundColor Red
    }
}
