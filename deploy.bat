@echo off
set SRC=C:\Users\ericr\source\repos\ZE-FusionBot\SysBot.Pokemon.Discord\bin\Release\net10.0
echo Deploying SysBot.Pokemon.Discord.dll to all bots...
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Mew-SV-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Meloetta-SV-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Hoopa-PLZA-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Floette-PLZA-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Diance-PLZA-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Landorus-PLA-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Flareon-LGPE-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Glaceon-LGPE" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Pikachu-LGPE-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Giratina-BDSP-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Rayquaza-BDSP-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Jirachi-SWSH-Bot" SysBot.Pokemon.Discord.dll
robocopy "%SRC%" "C:\Users\ericr\OneDrive\Desktop\Celebi-SWSH-Bot" SysBot.Pokemon.Discord.dll
echo Done!
