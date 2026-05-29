@echo off
set SRC=C:\Users\ericr\source\repos\ZE-FusionBot\SysBot.Pokemon.Discord\bin\Release\net10.0\SysBot.Pokemon.Discord.dll

echo Copying Discord DLL with clean embeds to all bots...
echo.

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Mew-SV-Bot\"
if %errorlevel%==0 (echo [OK] Mew-SV-Bot) else (echo [FAIL] Mew-SV-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Meloetta-SV-Bot\"
if %errorlevel%==0 (echo [OK] Meloetta-SV-Bot) else (echo [FAIL] Meloetta-SV-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Hoopa-PLZA-Bot\"
if %errorlevel%==0 (echo [OK] Hoopa-PLZA-Bot) else (echo [FAIL] Hoopa-PLZA-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Floette-PLZA-Bot\"
if %errorlevel%==0 (echo [OK] Floette-PLZA-Bot) else (echo [FAIL] Floette-PLZA-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Diance-PLZA-Bot\"
if %errorlevel%==0 (echo [OK] Diance-PLZA-Bot) else (echo [FAIL] Diance-PLZA-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Landorus-PLA-Bot\"
if %errorlevel%==0 (echo [OK] Landorus-PLA-Bot) else (echo [FAIL] Landorus-PLA-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Flareon-LGPE-Bot\"
if %errorlevel%==0 (echo [OK] Flareon-LGPE-Bot) else (echo [FAIL] Flareon-LGPE-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Glaceon-LGPE\"
if %errorlevel%==0 (echo [OK] Glaceon-LGPE) else (echo [FAIL] Glaceon-LGPE)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Pikachu-LGPE-Bot\"
if %errorlevel%==0 (echo [OK] Pikachu-LGPE-Bot) else (echo [FAIL] Pikachu-LGPE-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Giratina-BDSP-Bot\"
if %errorlevel%==0 (echo [OK] Giratina-BDSP-Bot) else (echo [FAIL] Giratina-BDSP-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Rayquaza-BDSP-Bot\"
if %errorlevel%==0 (echo [OK] Rayquaza-BDSP-Bot) else (echo [FAIL] Rayquaza-BDSP-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Jirachi-SWSH-Bot\"
if %errorlevel%==0 (echo [OK] Jirachi-SWSH-Bot) else (echo [FAIL] Jirachi-SWSH-Bot)

copy /Y "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Celebi-SWSH-Bot\"
if %errorlevel%==0 (echo [OK] Celebi-SWSH-Bot) else (echo [FAIL] Celebi-SWSH-Bot)

echo.
echo Done!
