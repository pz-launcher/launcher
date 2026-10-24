# PZ dedicated server runtime behaviour (verification spike, 2026-10-23)

Answers parent `design.md` R1/R2 for the hosting design. Two sources:

- **Bytecode**: `javap -c` (JDK 21) on `java/projectzomboid.jar` of the local
  Windows dedicated server, Steam build `25485538` (B42). Classes:
  `zombie.network.GameServer`, `RCONServer` (+ `$ClientThread`,
  `$ExecCommand`), `ServerOptions`, `ServerWorldDatabase`,
  `zombie.core.logger.LimitSizeFileOutputStream`, `zombie.core.Core`.
- **Live run** on Windows 11 with that install: a small Python harness
  started `jre64\bin\java.exe` directly (`-Drconlo=1 -Xmx4g -nosteam`,
  `-cachedir=` in a scratch folder, ports 16361/16362, RCON 27115), stdin /
  stdout / stderr on `NUL`, `DETACHED_PROCESS | CREATE_NEW_PROCESS_GROUP`;
  the harness exited right after the start. Nothing under `~/Zomboid` or the
  install was touched.

Linux was **not** run (no Linux machine or WSL distro here); Linux-only items
are listed at the end.

## Findings

### Console input (stdin) — parent R1: resolved, harmless

- The `command handler` thread loops: `if (reader.ready()) readLine()` else
  `Thread.sleep(100)`. `readLine() == null` queues the internal command
  `process-status@eof` and the thread ends.
- With stdin on `NUL` the server ran normally; idle CPU 78 ms over 10 s. No
  busy loop, no exit.
- Exception: the **first-run admin prompt** (below) reads stdin with a
  blocking `Scanner.nextLine()`.

### First-run admin account

- `ServerWorldDatabase.create()`: if user `admin` doesn't exist and no
  `-adminpassword` was given, it prints `User 'admin' not found, creating it`,
  `Command line admin password: null`, `Enter new administrator password:`
  and reads stdin. On EOF: `java.util.NoSuchElementException: No line found`
  in `main`; the process **exits ~2 s after start** (verified, server
  `spike2`).
- That failed start **still leaves `db/<name>.db`** (verified: 73 KB, no admin
  row) plus `Server/<name>.ini` and spawn files. So "db file exists" does
  **not** mean the admin account exists.
- With `-adminpassword <pw>` and no admin: `Administrator account 'admin'
  created.` and `admin password changed via -adminpassword option`.
- With `-adminpassword` and an existing admin: the password is **reset**
  (`UPDATE whitelist SET password = ?`, same log line). Passing it on every
  start would overwrite in-game password changes.
- Second start of an existing server without `-adminpassword`: no prompt,
  starts normally (verified).

### Command line (`GameServer.main`)

Recognised: `-cachedir=<dir>`, `-servername <name>`, `-adminusername <u>`,
`-adminpassword <pw>`, `-ip <addr>`, `-port <n>`, `-udpport <n>`,
`-steamvac <bool>`, `-nosteam`, `-console_dot_txt_size_kb=<n>`,
`-disablelog=`, `-debuglog=`, `-debugLogFile=`, `-useTimeStampedLogFileNames=`,
`-statistic <n>`, `-coop`, `-gui`, `-seed=`, `-no-worldgen`, `-no-foraging`,
`-no-attachment`.

### Ini (`ServerOptions.init`)

- If `<cachedir>/Server/<name>.ini` exists: `loadServerTextFile` (unknown
  keys ignored, missing keys keep defaults), then **always**
  `saveServerTextFile` — the whole file is regenerated with every key and
  PZ's own comments. Otherwise it writes the defaults.
- Verified: a 4-line ini (`RCONPort`, `RCONPassword`, `DefaultPort`,
  `UDPPort`) became 421 lines with our values kept.
- Consequence for `10-23-server-config-core`: PZ drops unknown keys and custom
  comments on every server start; only PZ's own layout survives.
- `<name>_SandboxVars.lua` (45 KB) appeared only after a successful first
  start; the failed start wrote `.ini` and spawn files but no SandboxVars.

### RCON (`RCONServer`) — parent R2/R3: resolved

- Started only when `RCONPassword` is non-empty; port = `RCONPort`. Log line:
  `RCON: listening on port <n>`.
- Bind address: `127.0.0.1` when the JVM system property `rconlo` is set
  (any value, e.g. `-Drconlo=1`); else the `-ip` address; else all
  interfaces. Verified with `-Drconlo=1`: `netstat` shows
  `127.0.0.1:27115 LISTENING` only. → R3 (RCON exposed) is solved by always
  passing `-Drconlo=1`; no firewall rule needed for RCON.
- Protocol: Source RCON, little-endian `size, id, type, body\0\0`. Incoming
  packets must be 10..4096 bytes. At most 5 client connections.
  - type 3 AUTH → an empty RESPONSE_VALUE, then AUTH_RESPONSE with the request
    id, or `-1` on a wrong password (the server then closes the connection;
    logs `RCON: password doesn't match`). Verified.
  - type 2 EXECCOMMAND → queued to the main thread (`GameServer.rcon`), the
    client thread blocks until the response is ready, so responses arrive in
    request order.
  - Responses are UTF-8, split into packets of at most 4086 body bytes with
    the request id. Verified: `help` = 5866 bytes in 2 packets.
  - type 0 RESPONSE_VALUE is echoed back as an empty packet with the same id:
    the standard end-of-response sentinel works. Verified.
- `players` → `Players connected (0): \n`.
- RCON commands are **not** written to `server-console.txt` (only auth
  failures are), so the UI must show the command and its response itself.

### Stop

- RCON `quit` → response `Quit` in 0.2 s; the process exited 8.5 s later
  (twice, same server); log ends with `Shutdown handling finished`; the save
  folder `Saves/Multiplayer/<name>/` was written.

### `server-console.txt`

- Path `<cachedir>/server-console.txt`; `System.out` is replaced by a
  `ProxyPrintStream` writing to the original stdout **and** this file.
- Opened with `FileOutputStream(file, append=false)`: **truncated on every
  start** (verified).
- `LimitSizeFileOutputStream`: when the bytes written would pass the limit, it
  closes and **re-opens the file truncated** and continues — so it is also
  truncated **during a run**. Limit = `-console_dot_txt_size_kb=` clamped to
  512..102400 KB, default 5120 KB.
- One file per cache dir, shared by all server names (fine: one runs at a
  time, D4).
- First lines are SLF4J warnings; startup to `RCON: listening` took 25 s
  (first start incl. world generation) and 27 s (second start).

### Process

- Started detached with `NUL` std handles, the java process kept running after
  its launcher exited (parent PID pointed at the exited harness). Name
  `java`, start time readable via `Process.StartTime`.
- Working set ~4.4 GB with `-Xmx4g`.

## Still unverified (Linux)

- Linux server folder layout: `ProjectZomboid64.json` location (root vs. a
  nested folder), `start-server.sh` contents, native library paths
  (`linux64`, `natives`) and whether `LinuxLaunchEnvironment`'s
  `LD_LIBRARY_PATH` / `LD_PRELOAD` (`libjsig.so`) apply to the server.
- Detached start on Linux (`setsid`, stdin `/dev/null`) and survival across
  `systemctl restart` with `KillMode=process`.
- Same RCON / console behaviour on the Linux build (same jar expected).

These become the manual Linux acceptance check of this child and are designed
so a wrong guess only changes constants in `PzServerFiles.cs`.
