# Understanding map values

In **Editor**, select a map and click **Map info** beside its tab, or press
**F1**. **View → Map layout and display → Map information…** opens the same window.
Value units and input-axis names
appear above the tuning explanation. Storage addresses and data types remain
under **Storage details**. The window follows the selected map.

The bundled profiles have been reviewed for unit and arithmetic clarity across
all ten ROM layouts. An imported definition supplies its own units and help;
SpeedLink displays that definition's metadata. Missing imported units are shown
as unspecified, rather than guessed.

## Common conventions

| Value type | How to read it |
| --- | --- |
| Multiplier | 1 leaves its contribution unchanged; 1.1 applies 110% and 0.9 applies 90%. Activation and limits still matter. |
| Percent of base | 100 leaves the base unchanged; 90 applies 90% of it. |
| Duty percentage points | +5 adds five points: a 40% request becomes 45% before later limits. |
| Fuel enrichment percent | 20 contributes 20% additional calculated fuel before other corrections and limits. |
| Degrees of retard | A positive retard request removes ignition advance when that path is active. A retard limit only bounds the request. |
| Degrees of advance correction | A positive correction adds ignition advance; a negative one removes it. The final timing limits still apply. |
| Injection degrees BTDC | Angle before compression top dead center. Larger positive angles occur earlier. These are injection phases. |
| Lambda | 1 is stoichiometric; lower is richer and higher is leaner. These are not gasoline AFR numbers. |
| Absolute pressure | Includes barometric pressure. Gauge boost is absolute pressure minus current barometric pressure. |
| Load | Relative engine-load scale, not boost pressure. |
| Airflow in lb/min | Pounds per minute, not grams per second. |
| Seconds / milliseconds | 0.5 s is half a second; 1 ms is one thousandth of a second. |
| Engine events | Counts of 180-degree crank events. Their elapsed duration changes with RPM. |
| ECU coefficients or counts | Unscaled calibration values. Read the map's formula; do not assume milliseconds, degrees or a physical flow unit. |

Thresholds qualify a condition. Limits bound a request. Hysteresis is a
difference from the activation threshold. A map may be bypassed in the factory
configuration or scaled by other maps; its description retains those qualifications.

## Knock reference and injector examples

**Knock Retard Additive** adds an offset to the knock-detection reference:

`reference = base reference × Knock Retard Multiplier + offset`

Its values use ECU knock-signal units. A value of 0.8 adds 0.8 signal units;
one internal operating state halves that offset. It does not directly subtract
0.8 degrees from ignition timing. The physical sensor-unit calibration is not
established. **Knock Retard Multiplier** scales this reference, rather than
accumulated spark retard. The map names remain stable for existing projects.

**Injector Base Pulsewidth** values are encoded coefficients, not milliseconds
or injector flow rates. Increasing a coefficient by 10% lengthens the calculated
base pulse by about 10% at the same fuel quantity and pressure, before later
corrections and limits. **Injector Pulsewidth Offset** is an addition in
milliseconds; **Injector Short Pulsewidth Correction** is a multiplier.

For MAP sensor conversion, the map named **MAP Scale Additive** is actually
subtracted after the multipliers. Its help states the formula and the remaining
physical-unit qualification. The name alone is not sufficient to infer a sign.

These explanations describe the calibration calculation. They do not establish
real-ECU acceptance or prescribe tuning values.
