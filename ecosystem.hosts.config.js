// ============================================================================
//  PM2 supervisor for the 5 multi-tenant bot hosts.
//  PM2 restarts a host the instant it exits (~15s back up) instead of waiting on
//  a polling watchdog. exp_backoff_restart_delay grows the delay on repeat crashes
//  so a crash-loop can NEVER hammer Discord into an IP ban.
//  Rule: one bot per game per host (SysCord<T>.Runner is static per game type).
//  Reboot-persisted via `pm2 save` -> pm2-resurrect.cmd (same as the governor).
// ============================================================================
const STD = 'C:\\Users\\ericr\\source\\repos\\ZE-FusionBot\\publish-multitenant\\SysBot.Pokemon.ConsoleApp.exe';
const SV  = 'C:\\Users\\ericr\\source\\repos\\ZE-FusionBot\\publish-multitenant-sv\\SysBot.Pokemon.ConsoleApp.exe';
const STD_CWD = 'C:\\Users\\ericr\\source\\repos\\ZE-FusionBot\\publish-multitenant';
const SV_CWD  = 'C:\\Users\\ericr\\source\\repos\\ZE-FusionBot\\publish-multitenant-sv';
const D = 'C:\\Users\\ericr\\OneDrive\\Desktop';
const cfg = (b) => `${D}\\${b}\\config.json`;

const common = {
  interpreter: 'none',                 // run the .exe directly, not via node
  autorestart: true,
  exp_backoff_restart_delay: 15000,    // 15s, grows on repeat crashes = ban safety
  min_uptime: 30000,                   // must stay up 30s to be considered stable
  max_memory_restart: '2600M',         // recycle a host if it balloons past 2.6GB
  kill_timeout: 8000,
  env: { DISCORD_REST_PROXY: 'http://127.0.0.1:3460/api/v10/' },
};

module.exports = {
  apps: [
    { name: 'host-A', script: STD, cwd: STD_CWD, args: [cfg('Celebi-SWSH-Bot'), cfg('Dialga-BDSP-Bot'), cfg('Diance-PLZA-Bot'), cfg('Flareon-LGPE-Bot')], ...common },
    { name: 'host-B', script: STD, cwd: STD_CWD, args: [cfg('Giratina-BDSP-Bot'), cfg('Floette-PLZA-Bot')], ...common },
    { name: 'host-C', script: STD, cwd: STD_CWD, args: [cfg('Rayquaza-BDSP-Bot'), cfg('Hoopa-PLZA-Bot')], ...common },
    { name: 'host-D', script: SV,  cwd: SV_CWD,  args: [cfg('Mew-SV-Bot')], ...common },
    { name: 'host-E', script: SV,  cwd: SV_CWD,  args: [cfg('Meloetta-SV-Bot')], ...common },
    // Shaymin (Legends Arceus / PA8) runs in its OWN process: sharing a host with a Legends Z-A
    // (PA9) bot like Floette cross-wires their Switch connections (Floette grabbed 10.0.0.159).
    { name: 'host-F', script: STD, cwd: STD_CWD, args: [cfg('Shaymin-PLA-Bot')], ...common },
  ],
};
