# MazdaLink

Windows 10/11 desktop tools for Mazda ECU backups, ROM inspection, and supported programming workflows.

[![C# checks](https://github.com/brxndon19/MazdaLink/actions/workflows/checks-dotnet.yml/badge.svg)](https://github.com/brxndon19/MazdaLink/actions/workflows/checks-dotnet.yml)

## Mazda 2 DE (2008, ZY-VE)

Use the manufacturer's 32-bit J2534 04.04 driver for your Mongoose-Plus ISO/CAN3 adapter. Adapter support is experimental; Windows 10/11 and your physical ECU still need acceptance testing.

- **Connection** identifies `ZY87EC000` / `ZY87EC000ZYV5050` and automatically selects the observed read session with a 512-byte limit. Ignition on, engine off.
- **Read & verify backup** captures the entire 1 MiB ROM twice. Matching captures, manifest, and journal are retained. **Save raw backup as .rom…** exports an exact copy without overwriting existing files.
- **MazdaLinkReader.exe** is an alternative interactive reader. Its `checksum` command checks/corrects all 19 established ZY87 checksum ranges in an externally edited `.bin` or `.rom`. Use `--stock` with the original backup to compare changes.
- **Mazda 2 Flash & Recovery** validates a modified ROM against its complete original hardware backup. It is an offline preflight only. **Mazda 2 erase, flashing, and recovery are not implemented or enabled.** A checksum pass is not proof that a tune is safe or that programming is supported.
- The built-in editor definitions and live Gen 1 programmer target other Mazda ECUs. Do not use the Gen 1 programmer for the Mazda 2.

Download the standalone desktop **SpeedLink.exe** and optional **MazdaLinkReader.exe** from [Releases](https://github.com/brxndon19/MazdaLink/releases). No .NET installation is required. Keep the executable in a writable folder and retain the entire backup folder.

[Build and test](dotnet/README.md) · [Adapter setup](docs/ADAPTERS.md) · [Mazda 2 programming status](docs/MAZDA2_FLASHING.md)
