# Importing definitions

SpeedLink includes editor and logger definitions for all ten supported ROMs.
You can install definition updates without replacing the EXE.

1. Disconnect the adapter and finish any active operation.
2. Choose **Tools → Manage definitions…**, then **Import…**.
3. Choose one editor `.xml` file or logger `.json` file from a source you trust.
   Review its ROM ID, version and number of controls, then confirm the import.
4. Reopen the ROM for editor updates. For logger updates, load the current ECU
   ROM again before connecting and logging.

Editor XML files are available under `definitions/tuning/`. Logger files are
named `*.logger.json` under `definitions/logging/`. Import the actual logger
profile; its catalog, test vectors and evidence files are not definitions.

Imported files live in `data/definitions` beside SpeedLink.exe. Replacing the
EXE preserves them when you keep that folder. **Open folder** shows the location.
The Definitions window shows whether each active copy is imported or bundled.

An update applies the next time a ROM is opened or loaded for logging. It does
not change an already-open ROM, its edits or its undo history. Save that work
before reopening. The bundled copies remain embedded in the application.

Select a row and choose **Use bundled copy** to remove its override. An earlier
import is retained as a `.previous` backup. If an imported file becomes invalid,
SpeedLink uses its bundled copy and reports the problem in Definitions.

## Supported updates

The importer accepts the standalone, one-ROM editor XML format used by this
project and the SpeedLink logger JSON format. ROM identity must match a supported
layout. It checks storage bounds, protected ROM metadata, supported scaling and
static axes. Every editor export still applies its mandatory ROM-specific integrity policy.

Program compatibility checks and logger request limits remain controlled by the
application. Imported editor maps can use calibration storage and the program-area
bits already supported by the bundled layout. Logger updates must retain the
supported ROM fingerprints and use existing allowed read identifiers.

New ROM layouts, new program-area editing ranges or new logger identifiers need
application support. A definition import does not install checksum plugins,
execute code, add flashing support or validate a tune on a vehicle. XML files
that require other definition files, inherited layouts or dynamic axes are not
supported by this first importer.
