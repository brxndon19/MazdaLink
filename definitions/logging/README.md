# Stock-ROM logger definitions

The catalog now covers **ten ROMs with tuning definitions**: nine Gen 1 profiles with
33–38 parameters, plus L3CGEE000 Gen 2 with 20. See [per-ROM coverage and exclusions](All_ROM_Coverage.md),
[channel coverage](All_ROM_Coverage.csv), and the
[live ECU checklist](All_ROM_ECU_Checklist.csv). `Catalog.json` inventories the
profiles. Every profile has its own requests, MAP bindings, code guards, native
vectors and evidence. All 346 included ROM/channel combinations need ECU testing.

`LoggerDefinition.Known` exposes the embedded catalog;
`LoggerDefinition.ForCalibration` selects a unique live calibration ID;
`LoggerDefinition.BindKnownRom` selects and validates a matching complete image.
Ambiguous identities and unreviewed code changes are rejected. Unknown ROMs can
still be dumped by the reader. No ROM files are bundled.

The application can overlay imported `*.logger.json` updates through **Definitions**.
See [definition import](../../docs/DEFINITIONS.md) for file selection and compatibility limits.

The following is the original L38KEH000 research record; its counts and addresses
apply to that profile only.

## Original Speed6 definition — L38KEH000, v0.1.0

This SpeedLink logger definition covers the 38 parameters listed in the [Speed6 coverage catalog](Coverage.md).
It includes 36 queried channels using 34 distinct identifiers, plus calculated boost and injector duty. Shared MAP/voltage and clutch/neutral requests can be polled once and decoded into both channels.

## Files and status

- `L38KEH000.logger.json`: versioned requests, field decoding, units, calibration bindings, code fingerprints, tuner help and verification status.
- `logger-definition.schema.json`: JSON Schema 2020-12 for the data format. The C# reader also enforces semantic bounds, read-only services and dependency rules.
- `L38KEH000.vectors.json`: 298 controlled decoder/calculation vectors produced from native instruction tests.
- `L38KEH000.evidence.json`: validation scope and counts.
- [Coverage](Coverage.md) / `Coverage.csv`: a row for every channel.
- `ECU_Test_Checklist.csv`: live checks and evidence fields for every channel.

The reference image is L38KEH000 with SHA-256 `1b9a8b78ef9fe1a346a32865ffa0e5a2bd971d7383c5b39db35392f959349a85`. Its factory provenance and vehicle association are unverified. No ROM image is included.

All 38 channels were reviewed. Native descriptors, selected encoders and payload copies were executed for the queried data; both calculated channels were checked against their inputs. There are 238 independent scalar conversion checks, 16 switch-flag combinations, 4 unsupported-identifier cases, 298 decoder vectors and 108 passing C# tests, including all 52 original parity cases.

The instruction tests use synthetic RAM and a bounded SH-2E model. The standard diagnostic bitmap refresh is held out, and unrelated extended capture/status paths are excluded. SR is modeled synchronously. These are not peripheral, scheduler, physical-adapter logger or real-ECU tests.

**All 38 channels still need ECU testing.** Normal logging-session acceptance, physical values/units, sensor calibration, update rates, freshness and dropouts remain pending. The first desktop logger draft is integrated; see the [logger guide](../../docs/LOGGER.md). Integration tests do not change the per-channel ECU verification status.

## Important decoding choices

- Actual lambda uses identifier `0034`, with a four-byte data payload. The first two bytes encode lambda; the second word is not substituted for lambda. Desired lambda uses `0044`. Both use the exact native 1/32768 scale. AFR display requires an explicit fuel stoichiometry value.
- MAP voltage and high-resolution absolute pressure share `0900`. Pressure uses the ROM's logging gain and offset. Boost is absolute manifold pressure minus barometric pressure; vacuum remains negative.
- Cam timing `16CD` is signed 16-bit at 1/16 degree per count. Confirm crank/cam degree basis on an ECU.
- Secondary trim `163E` is signed Q13 after the native encoder multiplies internal percentage by 0.01. The definition displays `raw *100 /8192` percent and also offers `raw /8192` as correction fraction. Native consumption as `100/(100+trim)-1` establishes the internal percentage basis. The inspected reference metadata labels its fraction formula as Percentage; compare both presentations during ECU validation.
- Injection amount `1752` uses 1/32.768 mg per count, correcting the inherited 32.728 denominator. The fuel model's mass-per-injection basis still needs vehicle correlation.
- Pulsewidth `1410` is a calculated logging value using fuel quantity, specific gravity and a calibration coefficient. Its 0.008 ms/count scale is verified. It is not a direct electrical pulse measurement. Calculated injector duty follows the reference 360-degree convention: `pulsewidth_ms * RPM /600`, with no 100-percent clamp.
- Clutch is `A211` word bit 0; neutral is bit 10. Other bits are ignored. Both are switch states; physical polarity is pending confirmation.
- Narrowband voltage `0015` has a two-byte data payload and uses only its first byte for voltage. Catalyst temperature `003C` is an ECU model output.
- Some encoded values saturate. For example, vehicle speed is limited to 255 km/h and boost-air temperature to 119.375 C by their wire formats. An encoding boundary does not establish the actual physical value. Primary MAP pressure uses high-resolution sensor voltage with the ROM calibration.

## Calibration and profile matching

`LoggerDefinition.BindRom` checks the software marker and five code fingerprints before reading the MAP logging calibration. The source floats are big-endian at `0xB1644` (base multiplier), `0xB1648` (second multiplier), and `0xB164C` (subtracted offset). Gain is the product of the two multipliers.

This image gives 54.117647260427475 kPa/V and an offset of 1.6469999551773071 kPa. Those are reference defaults, not confirmation of a vehicle's installed sensor. Bind a matching current ECU backup, or explicitly supply a sensor calibration. Calibration-slot edits are allowed by the code fingerprints. Unreviewed code changes and other software IDs are rejected. Matching a file does not prove it is installed in the vehicle.

## C# decoder use

The offline decoder lives at `dotnet/src/SpeedLink.Core/LoggerDefinition.cs`. Input is an assembled diagnostic PDU, including positive service `62` and the echoed two-byte identifier. CAN headers and ISO-TP framing have already been removed.

```csharp
var profile = LoggerDefinition.Load("L38KEH000.logger.json");
var calibration = profile.BindRom(File.ReadAllBytes("matching-ecu-backup.bin"));
var response = Convert.FromHexString("6209000A00"); // synthetic example: 2.5 V
double kpa = profile.Decode("manifold_absolute_pressure", response, calibration);
```

`RequestFor` only builds read-identifier requests. The decoder opens no adapter, changes no ECU session and sends no traffic. Use normal application logging when implementing the live worker; do not reuse the ROM read/programming session sequence. Session/security acceptance is recorded as pending ECU validation.

Calculated channels require valid, fresh dependency samples. The caller supplies maximum age and skew limits using a consistent clock; these limits still require real-ECU rate testing. Missing, malformed, negative, stale or mismatched data must remain unavailable rather than becoming a zero reading.

Run the C# checks from `dotnet/` with `dotnet test SpeedLink.slnx -c Release`. No manufacturer driver, commercial assembly or ROM is required for those offline tests.
