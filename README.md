# MAUI BLE multiplayer

One Android phone hosts. Other phones join. The host is the GATT peripheral and the source of truth. Guests are centrals. The sample lobby exchanges hello, roster, and chat. It does not carry a game.

The transport moves JSON strings. Seat a player only after `hello`. A bare connection is not a player.

## Layout

| Path | What it is |
|------|------------|
| `src/MauiBle.Core` | Framing, advertise-name budget, lobby roster. No MAUI types. |
| `src/MauiBle.Sample` | Android GATT host, Plugin.BLE guest, lobby page. |
| `docs/pitfalls.md` | Failures this stack already hit, and the fix for each. |
| `docs/architecture.md` | Roles, GATT layout, and the advertise size budget. |

Service UUIDs live in `BleIds`. Generate new ones before you ship another app. Two apps that share a UUID will discover each other.

## Roles

```
Host phone (peripheral)  <-- notify TX / write RX -->  Guest phones (centrals)
        |
        source of truth for the roster
```

Android hosts and joins. The Windows build opens the lobby and reports that hosting is Android-only. iOS can join later with Plugin.BLE. This sample does not implement an iOS peripheral.

## Run the tests

```powershell
dotnet test tests/MauiBle.Core.Tests
```

The tests cover framing, the 18-character advertise name, and the rule that hello is what takes a seat.

## Build a sideload APK

```powershell
dotnet build src/MauiBle.Sample/MauiBle.Sample.csproj -f net9.0-android -c Release
```

`EmbedAssembliesIntoApk` is on. Install the full APK (`adb install --no-incremental`, or uninstall the old package first). A Fast Deployment build looks for `.__override__` and aborts on a phone that is not attached to the IDE.

Package id: `com.mauible.lobby`.

## Try it on two phones

1. Install the APK on the host and at least one guest.
2. Open the lobby on each phone before you host or scan.
3. On the host, enter a name and tap **Host**. The scan response is a short name such as `MBM Ada`.
4. On the guest, tap **Scan**, then tap the host. The guest sends `hello`. The host adds that phone to the roster and notifies every guest.
5. Send a line of chat. The host rebroadcasts it.

A third phone can join the same way. The cap is 8 players (1 host + 7 guests). Extra GATT connections are rejected.

## What the lobby sends

| type | who sends it | effect |
|------|----------------|--------|
| `hello` | guest after connect | host seats that transport id |
| `roster` | host | guests replace the name list |
| `chat` | either | host shows it and notifies every guest |
| `disconnect` | guest leaving | host frees the seat |

Messages at or under 160 UTF-8 bytes go out as one write. Larger messages are split into `type: part` frames and reassembled per peer. Do not use `part` as your own message type.

## License

MIT. See [LICENSE](LICENSE).
