# SpeedLink documentation

SpeedLink combines ROM reading, offline editing, stock-ECU logging, and Gen 1
flashing and recovery. Gen 2 flashing is planned for an upcoming release.

## Using SpeedLink

| Guide | Contents |
| --- | --- |
| [Dump a ROM / request support](DUMP_A_ROM.md) | Guided adapter setup, two-pass reading, verification and ROM contribution |
| [Gen 1 flashing and recovery](FLASHING.md) | Prepare a verified backup, write a ROM, and recover a failed application transfer |
| [ROM editor overview](editor/README.md) | Screenshots, features and a short editing workflow |
| [ROM editor guide](EDITOR.md) | First edit, single values, small tables, MAF curves, layout, map help, comparison and saving |
| [Map-help conventions](TUNING_HELP.md) | Units, sign conventions, multipliers, corrections and interpretation limits |
| [Logger overview](logger/README.md) | Live-data and recorded-log screenshots, features and a quick start |
| [Logger](LOGGER.md) | Adapter setup, ROM binding, parameters, units, charts, CSV recording and log viewing |
| [Logger performance](LOGGER_PERFORMANCE.md) | Max polling, measured adapter rates, reply completion and validation limits |
| [Report a problem](TROUBLESHOOTING.md) | Export a support ZIP, review its contents and open a guided issue |
| [General usage](USAGE.md) | Reading, backups, transport settings and file inspection |
| [Supported ROMs](SUPPORTED_ROMS.md) | Known inspection layouts and compatibility limits |
| [Adapter status and testing candidates](ADAPTERS.md) | Qualified adapter paths, planned hardware and the test checklist |
| [Definition import](DEFINITIONS.md) | Import editor/logger updates, persistence and bundled fallback |
| [Logger coverage](../definitions/logging/All_ROM_Coverage.md) | Included and excluded parameters across all ten profiles |

The app includes Getting started, Editor, Logger, Gen 1 flashing, and Report a problem guides under **Help**.
**Help → Editor guide** opens the editing guide; **Map info** / F1 explains
the selected map. Navigation shows workspace names by default; **☰ Navigation**
or **View → Navigation labels** toggles its compact form.

## Development and validation

- [Build the single Windows EXE](BUILD.md).
- [C# implementation and tests](../dotnet/README.md).
- [Validation results and pending ECU checks](../dotnet/docs/Validation.md).
- [Bundled tuning definitions](../definitions/tuning/README.md).
- [Contributing](../CONTRIBUTING.md) and [licenses/attributions](../ATTRIBUTIONS.md).

The reader can dump unknown compatible-family ROMs without a definition. Editing
and logging each have stricter profile requirements, described in their guides.

## Get help

Use the [problem-report workflow](TROUBLESHOOTING.md) to export a diagnostic ZIP
and open a guided GitHub report.

- [Experimental Gen 2 reading, editing, logging and validation limits](GEN2.md)
