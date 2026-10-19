# How fpsacha/zomboid-control-panel handles Workshop collections (2026-10-19)

Source: https://github.com/fpsacha/zomboid-control-panel (MIT; Node.js web panel for dedicated servers).

- It does not list the user's favorited collections (code search for favorite / myworkshopfiles / GetUserFiles finds
  nothing Workshop-related). The admin gives one collection id; the panel mirrors its tracked mods into it.
- Reads: keyless `ISteamRemoteStorage/GetCollectionDetails` and `GetPublishedFileDetails` (same as this launcher).
- Writes (add/remove items of the user's own collection): POST `https://steamcommunity.com/sharedfiles/addchild` /
  `removechild` with the user's web session cookies `sessionid` + `steamLoginSecure`
  (`server/services/workshopCollectionSync.js`). The cookies come from a bundled browser extension
  (`browser-extension/`) that reads them from steamcommunity.com and posts them to the panel; stored as a secret.
- Workshop text search: `IPublishedFileService/QueryFiles` with a Steam Web API key the admin supplies
  (`server/services/steamApiKey.js`, `server/routes/mods.js`).
- No Steamworks, so no "playing PZ" status; the cost is handling a login-equivalent cookie (their own code calls
  `steamLoginSecure` "effectively a Steam login token"), which expires and must be re-sent.
