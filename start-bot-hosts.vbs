' ============================================================================
'  Launch all 5 multi-tenant bot hosts HIDDEN (no console windows to babysit).
'  Grouping = one bot per game per process so PKHeX loads once per host.
'  Requires the local Discord governor (127.0.0.1:3460) already up — the Startup
'  folder also runs pkm-governor-startup.vbs, which sorts before this file.
' ============================================================================
Option Explicit
Dim sh, stdExe, svExe, d, wmi, procs
Set sh = CreateObject("WScript.Shell")

' Guard: if any host is already running (e.g. logon without a reboot), do nothing —
' relaunching would double-connect each bot's token and get it kicked by Discord.
Set wmi = GetObject("winmgmts:\\.\root\cimv2")
Set procs = wmi.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name='SysBot.Pokemon.ConsoleApp.exe'")
If procs.Count > 0 Then WScript.Quit

sh.Environment("PROCESS")("DISCORD_REST_PROXY") = "http://127.0.0.1:3460/api/v10/"

' give the governor a moment on a cold boot
WScript.Sleep 8000

stdExe = "C:\Users\ericr\source\repos\ZE-FusionBot\publish-multitenant\SysBot.Pokemon.ConsoleApp.exe"
svExe  = "C:\Users\ericr\source\repos\ZE-FusionBot\publish-multitenant-sv\SysBot.Pokemon.ConsoleApp.exe"
d      = "C:\Users\ericr\OneDrive\Desktop\"

Function Q(s) : Q = """" & s & """" : End Function

' Host A: Celebi(SwSh) + Dialga(BDSP) + Diance(PLZA) + Flareon(LGPE)
sh.Run Q(stdExe) & " " & Q(d & "Celebi-SWSH-Bot\config.json") & " " & Q(d & "Dialga-BDSP-Bot\config.json") & " " & Q(d & "Diance-PLZA-Bot\config.json") & " " & Q(d & "Flareon-LGPE-Bot\config.json"), 0, False
WScript.Sleep 5000
' Host B: Giratina(BDSP) + Floette(PLZA)
sh.Run Q(stdExe) & " " & Q(d & "Giratina-BDSP-Bot\config.json") & " " & Q(d & "Floette-PLZA-Bot\config.json"), 0, False
WScript.Sleep 5000
' Host C: Rayquaza(BDSP) + Hoopa(PLZA)
sh.Run Q(stdExe) & " " & Q(d & "Rayquaza-BDSP-Bot\config.json") & " " & Q(d & "Hoopa-PLZA-Bot\config.json"), 0, False
WScript.Sleep 5000
' Host D: Mew-SV  (SV deps build)
sh.Run Q(svExe) & " " & Q(d & "Mew-SV-Bot\config.json"), 0, False
WScript.Sleep 5000
' Host E: Meloetta-SV  (SV deps build)
sh.Run Q(svExe) & " " & Q(d & "Meloetta-SV-Bot\config.json"), 0, False
