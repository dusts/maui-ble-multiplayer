# Architecture

## Roles

The host owns the roster. Guests apply what the host notifies. Do not let every phone advertise at once.

| Role | Bluetooth | Stack in this repo |
|------|-----------|--------------------|
| Host | GATT peripheral, advertises | `AndroidBlePeripheral` |
| Guest | Central, scans and connects | Plugin.BLE inside `BleTransport` |

Plugin.BLE is not the host. Its Android peripheral support is not what this sample relies on.

## GATT

| UUID | Properties | Direction |
|------|------------|-----------|
| Service `BleIds.Service` | Primary | advertised |
| RX | Write, WriteWithoutResponse | guest to host |
| TX | Notify and Read | host to guests |
| CCCD `00002902-0000-1000-8000-00805f9b34fb` | Write | guest turns notifications on |

`SendAsync` on the host loops every connected device and notifies each one. Keeping only the last connected device drops every guest but one.

The transport id is the Android Bluetooth address. Map that address to a player id when `hello` arrives. The advertised name is a label, not an identity.

## Advertise budget

A legacy primary advertisement is about 31 bytes.

- Flags, often added for you: about 3 bytes
- A 128-bit service UUID: about 18 bytes
- Room left for a name: often under 10 bytes

Put the service UUID alone in the primary advertisement. Put a short local name in the scan response, or omit it. `BleAdvertiseName` keeps that name to 18 characters and prefixes it with `MBM`. The full player name travels in `hello` after the connection exists.

If advertise returns `DataTooLarge`, the host retries with no name.

## Scan list

The lobby page is open before the scan starts, and it is already subscribed to `NearbyHostsUpdated`.

1. Subscribe.
2. Start the scan, filtered by the service UUID, with duplicate advertisements allowed.
3. If the filtered scan throws, scan again with no filter.
4. When the page appears, paint `GetDiscoveredHosts()` immediately.
5. When the scan times out, publish that list again.

A log line that says one device was seen, while the list on screen stays empty, is this race.

## Framing

`BleFraming` splits a UTF-8 string into 160-byte slices, base64-encodes each slice, and wraps it as JSON. The guest requests MTU 512 after connect. A single notify is still hard-capped at 500 bytes so one oversized write cannot blow the link. Reassembly is per transport id, because parts from two guests must not mix.

## Session vs transport

`IBleTransport.SendAsync` takes a string. `LobbySession` decides what the string means. A GATT connect raises `PeerJoined` and does not create a roster seat. `LobbyRoster.TrySeatGuest` runs only for `hello`.

That split is the point of the library. A different app keeps this transport and replaces the lobby messages.
