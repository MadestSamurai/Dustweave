# Dustweave · 织尘

Your daily routine, woven into one timeline.

**Windows x64 · 简体中文 / 繁體中文 / English**

| Package | Requirements |
| --- | --- |
| Portable | Includes the .NET desktop runtime; extract and run |
| Lite | Smaller download; requires .NET Desktop Runtime 8 x64 |

## Get started

Extract the complete package into a new folder and run `Dustweave.exe`. Keep the adjacent `app` folder: it contains data, documentation and licenses. Do not copy only the EXE or mix files from different releases.

Exit the previous Dustweave from the system tray before upgrading. Accounts, preferences and history reuse the current Windows user's data folder. Built-in tools follow the host language and theme.

## Moving from 1.4 to 1.5

Version 1.5 bundles the main application and its dependencies. Download its full package once and extract it into a new folder. The 1.4 and 1.5 update feeds are separate; older versions do not upgrade across this boundary automatically.

- [Latest GitHub release](https://github.com/MadestSamurai/Dustweave/releases/latest)
- [Quark download folder](https://pan.quark.cn/s/0ee553c60c0d?pwd=5AgC), access code: `5AgC`

Launch `Dustweave.exe` from the new folder and update any shortcuts you created. If Windows startup or scheduled runs are enabled, save those settings again in the new application so they use the new location.

## Updates and repair

The update page in software settings offers update checks, differential downloads and manual download links. Updates request a restart while idle and do not interrupt a running queue. From 1.5 onward, the application downloads and verifies a delta, then uses a temporary copy to replace files with rollback and interruption recovery. A local test build is not necessarily available through OTA.

If a 1.5 delta is unavailable or program files are damaged, download the full package from GitHub or Quark and extract into a new directory. Accounts and settings are retained. Version 1.4.18 retains its old independent repair entry.

## Accounts and schedules

Manage multiple accounts on the Accounts page. Isolated execution requires official Sandboxie-Plus; ordinary single-account use does not. Stop isolated instances before updating the game on the host, then launch them through Dustweave.

Scheduled tasks require a powered-on, awake computer and a signed-in Windows session. They are disabled by default.

## Attribution and license

Provided free by **MadSamurai** on Bilibili / **MadestSamurai** on GitHub. Source is MIT licensed: [Dustweave](https://github.com/MadestSamurai/Dustweave). Built-in tool licenses and sources are in `app/licenses`, `app/docs/tools` and `app/THIRD_PARTY_NOTICES.md`.
