# Tuning guide

In **Editor**, open a map and click **Map info** or **ⓘ** in its pane. You can
also press **F1** or choose **View → Map layout and display → Map information…**.
The popup explains the selected control and shows its units and input axes.
When this guide covers the control, a link in the popup opens the relevant
section. Addresses and data types are under **Storage details**.

The sections below explain the bundled calibrations. Check the active ROM's map
help for its axes, units and conditions. Imported definitions supply their own
units and descriptions. SpeedLink shows unspecified units explicitly.

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

## Boost and wastegate control

The boost-control solenoid (**BCS**) controls the wastegate through a duty-cycle
command. The ECU combines several terms to produce that command.

1. **BCS DC Base** supplies the starting duty from RPM and throttle input.
   **BCS Throttle Input** selects the measured or requested throttle input for
   that lookup. Temperature, barometric pressure and battery voltage can also
   affect the duty request.
2. **Load Target Correction - Initial** looks up a duty correction from
   `actual load − target load`. This term uses load error in either mode.
3. **BCS Correction Mode** chooses what adds to the accumulated correction on
   each update. Choose **Engine load** or **Boost pressure**.

   | Mode | Input to the accumulated correction | Map that supplies the step |
   | --- | --- | --- |
   | Engine load (`0`) | Actual load minus target load | **BCS DC Adjustment - Load Target Correction - Incremental** |
   | Engine load (`0`) while overboost is latched | Manifold pressure above the overboost limit | **BCS DC Adjustment - Boost Limit Exceeded** |
   | Boost pressure (`1`) | Actual manifold pressure minus target manifold pressure | **BCS DC Adjustment Boost Target Correction (Boost Targeting Mode)** |

4. The accumulated term has its own increase and decrease limits. The ECU then
   limits the initial and accumulated terms together before applying them to
   the duty request. The final duty has its own bounds.

The factory images for the bundled definitions use load mode. The dropdown
writes `0` or `1`. The ECU also interprets other nonzero stored values as
pressure mode. Load mode uses the ECU's airflow-based estimate of engine load.
The same manifold pressure can correspond to different air charge as intake
temperature changes.

The correction tables return **duty percentage points**. A `+5` combined
correction would take a 40% duty request to 45% before the final limit. The sign
of the error selects a position on the map axis. The sign of the map value
determines which way duty changes. For example, actual pressure of 26 psia and a
target of 25 psia give a `+1 psi` error. A map value of `−2` at that input asks
for a two-point reduction in accumulated duty on that update. The error lookup
uses the nearest endpoint when the input exceeds the axis range.

**MAP Targets (Boost Targeting Mode)** supplies a pressure target in absolute
units. For example, at 14 psia barometric pressure, 26 psia manifold pressure
is 12 psi of gauge boost. **Load Targets (Alternate Mode)**, where present,
belongs to a separate target-selection strategy. Its activation is separate
from **BCS Correction Mode**.

## Load limits and fuel cut

- **Requested Load** maps and their per-gear limits shape how much load the ECU
  requests. **Overload Protection Fuel Cut** uses relative engine load as a
  separate protective threshold. Check the RPM and temperature axes on your ROM.
- **Overboost Pressure Limit - Base**, **Boost Limit Barometer Adjustment**, and
  **Overboost Pressure Limit Hysteresis** set the threshold and release point
  for the wastegate correction described above. **Overboost Protection Fuel Cut**
  is a separate protective fuel-cut threshold in absolute manifold pressure.
  Check Map info for any delay or RPM conditions on that ROM.
- **Rev Limiter Normal** is the ordinary fuel-cut RPM threshold. The
  **Cold Coolant Rev Limiter** entries can take priority when their conditions
  apply. **Absolute Rev Limiter - 1 / 2 / 3**, where present, belong to engine
  speed plausibility checks.

## Knock reference and spark timing

**Knock Retard Additive** adds an offset to the knock-detection reference:

`reference = base reference × Knock Retard Multiplier + offset`

Its values use ECU knock-signal units. A value of 0.8 adds 0.8 signal units;
one internal operating state halves that offset. It does not directly subtract
0.8 degrees from ignition timing. The physical sensor-unit calibration is not
established. **Knock Retard Multiplier** scales this reference, rather than
accumulated spark retard. The map names remain stable for existing projects.

## Injector pulsewidth

**Injector Base Pulsewidth** uses a pressure-dependent encoded coefficient to
convert requested fuel quantity to a base pulse. Increasing the coefficient by
10% lengthens that calculated base pulse by about 10% at the same fuel quantity
and pressure. **Injector Pulsewidth Offset** adds time in milliseconds.
**Injector Short Pulsewidth Correction** multiplies its input. Each contribution
has its own activation conditions and the final pulse is subject to later limits.

## Sensor scaling and feedback

**MAF Calibration** maps sensor voltage to airflow. Where a ROM exposes multiple
curves, check the map description for when each one is selected. **Closed Loop
AFR Target** uses lambda where labeled `lambda`. A target is a commanded value.
Use a measured channel when you want to see the sensor response.

For MAP sensor conversion, the map named **MAP Scale Additive** is actually
subtracted after the multipliers. Its help states the formula and the remaining
physical-unit qualification. The name alone is not sufficient to infer a sign.

The examples use illustrative numbers to explain the calculations. See the
[validation scope](VALIDATION.md) for the available ECU test evidence.
