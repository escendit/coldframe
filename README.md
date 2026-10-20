# Coldframe

Coldframe watches the soil in a garden and says when to water. Battery-powered Nodes measure soil
moisture, a Hub relays their Readings over the home network, and a self-hosted Server evaluates
them and notifies the people who share the garden on the web, iOS and Android.

The project is open source and built to be reproduced: firmware, Server, apps, hardware designs
and deployment all live in this repository.

## Start here

- [Developer quickstart](docs/quickstart.md): run the local stack and every test suite.
- [Build guide](docs/build/README.md): build, flash and add your own Node and Hub.
- [Deploy guide](docs/deploy/README.md): run Coldframe on your home server, then
  [build your own apps](docs/build/apps.md).
- [Firmware reference](docs/firmware/README.md): learn ESP-NOW, BLE provisioning, eFuse keys and
  deep sleep from the Node and Hub firmware.
- [Hardware design](hardware/README.md): schematics, BOM, wiring, enclosure and probe sealing.
- [Hub radio coexistence spike](docs/spikes/hub-radio-coexistence.md): what the Hub's radio can do at once.

## Repository layout

| Folder | Holds |
| --- | --- |
| [`apps/`](apps) | Runtimes: Node and Hub firmware, the Server, the web app, the mobile shells |
| [`packages/`](packages) | Contracts, shared libraries and generated clients the runtimes reference |
| [`tests/`](tests) | All tests, split by language |
| [`aspire/`](aspire) | The local development stack, which is also the integration-test host |
| [`deploy/`](deploy) | Helm charts, Fleet bundles and a Compose reference example ([`deploy/compose`](deploy/compose/README.md)); deploy at home with [`docs/deploy`](docs/deploy/README.md) |
| [`hardware/`](hardware) | The reference Node and Hub design: schematics, BOM, wiring, enclosure |
| [`docs/`](docs) | Guides and references |

## License

[Apache License 2.0](LICENSE)
