# Tuning guide

In **Editor**, open a map and click **Map info** or **ⓘ** in its pane. You can
also press **F1** or choose **View → Map layout and display → Map information…**.
The popup explains the selected control and shows its units and input axes.
When this guide covers the control, a link in the popup opens the relevant
section inside SpeedLink, including offline. You can also open **Help → Tuning
guide** from any workspace. Addresses and data types are under **Storage details**.

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

### Set up boost pressure mode

The bundled factory ROMs ship with **BCS Correction Mode** set to **Engine
load**. The walkthrough uses Gen 1 **L3M6EG000**, where the pressure mode has
been traced through the wastegate command in firmware. Vehicle behavior
remains unverified.

1. In the Editor, open **Boost Control → BCS Correction Mode** and select
   **Boost pressure**. Keep **BCS Throttle Input** at its existing setting.
   Check whether the RPM × throttle tables are using **Measured TPS** or
   **Target TPS** before choosing which cells to edit.
2. In **Boost Control / Targets → MAP Targets (Boost Targeting Mode)**, enter
   the manifold-pressure target for the RPM and throttle cells you want to
   tune. The values are in **psi absolute**. Add local barometric pressure to
   your gauge target. For example, 10 psi of gauge boost at 14.5 psia barometric
   pressure is a target of 24.5 psia. Start with pressures already validated on
   your setup. Check the surrounding cells too, because the ECU interpolates
   between them and the factory pressure table contains zero-valued cells.
3. Start with your proven **BCS DC Base** and the stock **BCS DC Adjustment Boost
   Target Correction (Boost Targeting Mode)** curve. Keep the load requests
   and overboost protections at their known-good settings. **Load Targets
   (Alternate Mode)** belongs to a separately enabled strategy.
4. Log RPM, the relevant throttle input, manifold absolute pressure, barometric
   pressure, and commanded wastegate duty where those channels are available.
   Compare actual MAP to the matching target cells in the same units. Use
   **BCS DC Base** to bring a steady pull closer to target. Use the correction
   curve to tune how quickly duty responds to pressure error. The ECU can look
   up a pressure correction and still clear the accumulated term or override
   the final duty. If duty stops changing while error persists, check whether
   the controller is active and whether a correction or duty limit is reached.
   Change one map at a time so you can see its effect in the log.

On stock **L3M6EG000**, about 1.015 psi under target adds 0.30 duty percentage
points per active update. About 1.015 psi over target subtracts 0.60 points.
The firmware schedules this controller every **25 ms nominally** (40 Hz).
Four active updates at that same over-target error remove 2.4 points from the
accumulated term in about 100 ms, subject to the limits. The period comes from
the ROM scheduler. Live timing and pressure response remain unmeasured.

The L3M6EG000 enable threshold is based on **gauge boost** and RPM. At
1,500 RPM the stock threshold to start accumulating correction is about
7.7 psi gauge. Once enabled, it can stay active down to about 4.8 psi gauge
at that same RPM, depending on other ECU conditions. A below-threshold test
can show an active table lookup with zero accumulated correction.

### How the ECU combines the corrections

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

The dropdown writes `0` or `1`. The ECU also interprets other nonzero stored
values as pressure mode. Load mode uses the ECU's airflow-based estimate of engine load.
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
AFR Target** uses lambda where labeled `lambda`. The three **Open Loop Part
Throttle AFR Target** maps in Gen 2 L3CGEE000 also use lambda. Enter 0.80 for
lambda 0.80, which is about 11.8:1 on a 14.7:1 gasoline-equivalent scale. A
target is a commanded value. Use a measured channel to see the sensor response.

For MAP sensor conversion, the Gen 1 map named **MAP Scale Additive** supplies
the amount subtracted after the multipliers. Gen 2 L3CGEE000 exposes separate
sets of **MAP Scale Base Multiplier**, **MAP Scale Second Multiplier** and
**MAP Scale Subtractive Offset** for control and diagnostics and for logging.
Both sets calculate `kPa = max(0, volts × base multiplier × second multiplier − offset)`.
If the sensor changes, use its known pressure-versus-voltage curve to calibrate
both sets and compare against a known pressure reference.

The examples use illustrative numbers to explain the calculations. See the
[validation scope](VALIDATION.md) for the available ECU test evidence.
