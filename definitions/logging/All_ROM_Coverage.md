# Logger ROM coverage

All ten ROMs with local tuning definitions have a logger profile. Each profile uses its own identifier records, MAP calibration locations and code guards. The existing 38-channel baseline was checked separately in every image.

| ROM | Parameters | Requests per full scan | Excluded baseline parameters | Native vectors |
| --- | ---: | ---: | ---: | ---: |
| L38KEH000 | 38 | 34 | 0 | 298 |
| L39SED000 | 38 | 34 | 0 | 298 |
| L39SEE000 | 38 | 34 | 0 | 298 |
| L39SEF000 | 38 | 34 | 0 | 298 |
| L3M6EG000 | 33 | 30 | 5 | 247 |
| L3M7EF000 | 35 | 31 | 3 | 277 |
| L35HEH000 | 36 | 32 | 2 | 284 |
| L33XEH000 | 35 | 31 | 3 | 277 |
| L3P4EM000 | 35 | 31 | 3 | 277 |
| L3CGEE000 | 20 | 19 | 22 | 77 |

The checks execute bounded original ROM routines with synthetic RAM. They do not establish real-ECU session acceptance, physical values or rates. All 346 included ROM/channel combinations still need ECU testing. No ROM image is included.

## Parameters excluded from the current profiles

An excluded channel is not offered or polled by this draft. A similar signal may exist through another interface; that has not been established.

L3M6EG000: Desired fuel rail pressure, Fuel injection amount, Ambient air temperature, Catalyst temperature, Neutral switch. The checked native status encoder does not populate the neutral-switch bit; SpeedLink does not present a permanent zero as a neutral reading.

L3M7EF000: Desired fuel rail pressure, Fuel injection amount, Ambient air temperature.

L35HEH000: Desired fuel rail pressure, Ambient air temperature.

L33XEH000: Desired fuel rail pressure, Ambient air temperature, Catalyst temperature.

L3P4EM000: Desired fuel rail pressure, Fuel injection amount, Ambient air temperature.

L3M6EG000, L3M7EF000 and L3P4EM000 export pulsewidth directly from a different internal producer. Its wire scale remains 0.008 ms/count; its profile does not substitute pulsewidth for fuel injection amount. Older revisions also use a direct percentage source for primary fuel trim. Those paths were checked independently.

The selected ROM must match the live ECU calibration ID. Load the vehicle's current full backup so MAP/boost readings use the correct calibration. Unknown ROMs remain readable; their logger layout is not guessed.

See `All_ROM_Coverage.csv` for every included/excluded channel and `All_ROM_ECU_Checklist.csv` for the remaining live checks. The earlier `Coverage.md` and `ECU_Test_Checklist.csv` retain the original L38KEH000 research view.

L3CGEE000 uses independently checked Gen 2 identifiers and conversions. It also
offers calculated load and absolute throttle position beyond the older baseline.
The 22 unoffered baseline channels have no established active interface in this
release; this is not a claim that all comparable ECU signals are absent. Ambient
temperature is disabled in the checked lookup. See [Gen 2 limits](../../docs/GEN2.md).
