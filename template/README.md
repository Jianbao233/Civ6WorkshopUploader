# Steam Workshop Mod (Civ6)

## Directory layout

* `workshop.json` -- The config describing the Steam workshop mod.
* `content`       -- Where you should place the mod files to be uploaded to Steam Workshop.
                      For Civ6 this is the mod directory itself: `<ModName>.modinfo`,
                      `Binaries/`, `Data/`, `UI/`, `Script/`, `Text/` etc., exactly as the
                      game loads it from `My Games/Sid Meier's Civilization VI/Mods/<ModName>`.
* `image.png`     -- The image shown in the Steam Workshop. Replace with your own!
* `mod_id.txt`    -- Created automatically after the first upload. NEVER delete it.
* `README.md`     -- This readme document

## `workshop.json` Properties

This describes what gets uploaded to the workshop.

Most properties can be substituted with `null` or removed from the JSON if you wish for them to remain unchanged after the initial upload.

```
{
  "title": "",                -- The title of your mod.
  "description": "",          -- The description (BBCode, not Markdown).
  "visibility": "private",    -- The visibility status of the mod.
                                  Options include: "private", "friends_only", "unlisted", "public".
  "changeNote": "",           -- A note for describing the newest changes you've made to your users.
  "tags": [],                 -- A list of tags to search for your mod by.
  "dependencies": []          -- A list of mods that your mod depends on.
                                 These should be mod IDs (can be found in the workshop URL).

                              -- Set to `null` or remove them to say that you support all versions.
  "minBranch": "public-beta"  -- Minimum branch supported by this mod.
  "maxBranch": "public"       -- Maximum branch supported by this mod.

                              -- Optional per-language variants. Each entry only overwrites the
                                 fields you provide for that Steam language code. The primary
                                 upload always writes the "english" variant.
  "localizations": [
    {
      "language": "schinese",
      "title": "",
      "description": "",
      "changeNote": ""
    }
  ]
}
```