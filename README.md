# Civ6WorkshopUploader

CLI tool for creating, updating and removing **Sid Meier's Civilization VI** Steam Workshop items, designed so an AI agent (or a human) can drive the whole publish/maintenance flow from the command line.

Structure reference: [megacrit/sts2-mod-uploader](https://github.com/megacrit/sts2-mod-uploader). This project is an independent implementation for Civ6, not a copy of their code.

## Commands

```text
Civ6WorkshopUploader.exe new -w <dir>              Create a new workspace from the template
Civ6WorkshopUploader.exe upload -w <dir> [-i <id>] Upload a new item or update an existing one
Civ6WorkshopUploader.exe validate -w <dir>         Advisory pre-upload check (modinfo + referenced files)
Civ6WorkshopUploader.exe remove -w <dir> [-i <id>] Delete an item from the workshop
Civ6WorkshopUploader.exe comments -i <id>|-w <dir> [--since YYYY-MM-DD] [--until YYYY-MM-DD] [-o out.json] [--cookie "..."] [--proxy url]
                                                  Fetch workshop comments (pure HTTP, no Steam login for public items)
```

A bare directory path also works as a shortcut for `upload -w <dir>`.

## Workspace layout

```text
<workspace>/
├── workshop.json   # metadata: title, description, visibility, changeNote, tags, dependencies, localizations
├── image.png       # workshop preview image — REQUIRED, provide a real image (no placeholder shipped)
├── content/        # the Civ6 mod directory itself (.modinfo + Binaries/ + Data/ + UI/ ...)
└── mod_id.txt      # written after the first upload; NEVER delete (losing it loses the item ID)
```

Key facts:

- Items are created under the Civ6 app id `289070`; the tool itself registers as the Civ6 SDK tool depot (`404350`, see `steam/steam_appid.txt`).
- The primary update always writes the `english` language variant, independent of your Steam client language. Additional languages go through the `localizations` array in `workshop.json` and are applied as cheap metadata-only updates.
- `SteamAPI.Init` requires the Steam client to be running and logged in, with an account that owns Civ6.
- `comments` fetches workshop comments over plain HTTP (no Steam init). Use `--since`/`--until` to restrict to a date range and `-o` to export JSON. Steam may block anonymous/datacenter egress on this endpoint ("This profile is private.") — pass `--cookie` with a logged-in steamcommunity session to work around it. See `AGENTS.md` for the full agent-facing contract.

## Workshop feedback triage

`comments` is designed for daily batch triage: keep a ledger of your items, then run e.g.

```powershell
Civ6WorkshopUploader.exe comments -w <workspace> --since 2026-08-01 -o comments.json
```

Each comment carries `comment_id`, `author`, `author_steamid`, `timestamp` (unix, UTC) and `body`, so an agent can deduplicate, bucket by date, and prioritize fixes.

## Build

```powershell
dotnet publish -c Release -r win-x64 -p:PublishTrimmed=true --artifacts-path artifacts
.\artifacts\publish\Civ6WorkshopUploader\release_win-x64\Civ6WorkshopUploader.exe --help
```

Linux/macOS are supported by the project (conditional steam library copying); only win-x64 is tested locally.

## Using with an AI agent

The tool is deterministic and non-interactive: one command per action, exit code 0 on success, logs to `civ6-uploader.log` beside the executable. An agent can therefore fully own the publish workflow — building `content/` from staging, running `validate`, then `upload -w <workspace>` — and update the workshop ledger afterwards.

## License

MIT