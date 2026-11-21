# Pitfalls and the fix that held

These are failures from real phones, mostly Android, including Samsung, and from sideloaded APKs.

| What you see | What is wrong | Fix in this repo |
|--------------|---------------|------------------|
| App dies as soon as it opens, before anyone taps Host | `CrossBluetoothLE.Current` ran in a DI constructor or at startup | `BleTransport` builds Plugin.BLE on the first scan or connect |
| Sideload dies with `No assemblies found in ... .__override__` | The APK was a Fast Deployment build | `EmbedAssembliesIntoApk` is true. Install the full APK and uninstall the old one first |
| `Advertising failed: DataTooLarge` | Service UUID plus a long local name exceeded the 31-byte legacy advertisement | Primary advertisement is the UUID only. The name is scan-response only, capped at 18 characters. On `DataTooLarge`, advertise again with no name |
| Log says a device was found, and the list stays empty | The scan finished before the page subscribed, or the timeout cleared the list the UI was holding | Open the lobby first, subscribe, then scan. Republish `GetDiscoveredHosts()` on timeout |
| Only the first guest receives notifications | The host stored a single `BluetoothDevice` | `AndroidBlePeripheral` keeps a dictionary by address and notifies every entry |
| A connected phone never answers, and the host waits forever | The roster took a seat on connect, before `hello` | `LobbyRoster` seats a guest only from `hello` |
| API 33 host sends an empty notification | `SetValue` is obsolete and ignored | API 33+ passes the bytes into `NotifyCharacteristicChanged` |
| Guest cannot see crash logs on Samsung | `Android/data` is not browsable | Write a copy under Downloads with MediaStore if you add file logging. This sample logs through `ILogger` only |
| iOS phone cannot host | `CBPeripheralManager` is a separate implementation | This sample hosts on Android. An iOS build can be a guest |

## Also refuse these

- Classic RFCOMM as the only transport. iOS and Android do not meet cleanly there for a casual lobby.
- A large JSON blob inside the advertisement.
- Assuming eight centrals will always stay connected. The cap is 8. Many phones are comfortable at 3 to 5.
- Blocking the UI thread on connect or scan.
- Shipping a Debug Fast Deployment APK as the file people install.

## Phone checklist

- The app opens while Bluetooth is off.
- Host advertise succeeds.
- The guest list shows the host, not only a log line.
- Two guests connect. The host roster shows both. The host can send.
- A chat line from each phone shows on the others.
- Disconnecting in the lobby removes that seat.
- A sideloaded APK runs with no IDE attached.
