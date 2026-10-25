# Data Center Teleoperation Simulator (Unity 6 · Meta Quest · WebGL)

Drive robots around a data center and complete cabling work orders: pick up a cable, carry it to
the right rack and plug it into the right port. Runs **standalone on a Meta Quest** and **in the
browser**. The Unity project also contains the Da Vinci surgical robot simulation, which has its
own scene and is left untouched.

- **Try it in the browser:** [mzaidk.com/droyd](https://mzaidk.com/droyd)
- **Full documentation** (architecture, all controls, tests, performance): [README_DataCenter.md](README_DataCenter.md)

---

## Build it for a Meta Quest headset

### What you need

| | |
|---|---|
| Headset | Meta Quest 2, 3, 3S or Pro |
| Unity | **6000.4.0f1** with **Android Build Support** (and its OpenJDK and Android SDK & NDK Tools) |
| Cable | A USB-C data cable from the computer to the headset |
| Phone | The **Meta Horizon** app, signed in to the account the headset uses |

### 1. Get the project

```bash
git clone https://github.com/ZaidKamil1574/Robot-Teleoperation.git
```

In **Unity Hub**, click **Add > Add project from disk**, pick the `Robot-Teleoperation` folder and open it with
Unity **6000.4.0f1**. If Hub says Android support is missing: **Installs > 6000.4.0f1 > gear icon >
Add modules > Android Build Support** (tick OpenJDK and Android SDK & NDK Tools too).

The first open imports everything and takes a while.

### 2. Put the headset in developer mode (once)

1. In the **Meta Horizon** app: **Menu > Devices >** your headset **> Headset settings > Developer
   mode**, and switch it on. If it asks you to, create a free developer organization at
   [developers.meta.com](https://developers.meta.com/horizon/) first.
2. Restart the headset.
3. Plug the headset into the computer, put it on and accept **Allow USB debugging** (tick
   **Always allow from this computer**).

### 3. Build and install

1. In Unity: **File > Build Profiles > Android > Switch Platform**. Only needed once; it takes a
   few minutes the first time.
2. With the headset plugged in and awake: **Tools > Data Center > Platforms > Build And Run on Quest**.

That builds only the data center scene, installs it on the headset as **DC Teleop** and starts it.
The first build takes about 10 minutes; later ones are faster.

To start it again later, on the headset: **Library > Unknown Sources > DC Teleop**.

The XR settings (OpenXR with Meta Quest support) are already set up in the project; there is
nothing to change.

#### Or: build an APK to install later

**Tools > Data Center > Platforms > Build Quest APK** writes `Builds/Quest/DataCenterTeleop.apk`.
Install it with [Meta Quest Developer Hub](https://developers.meta.com/horizon/documentation/unity/ts-mqdh/)
(drag the APK onto the device), or from a terminal:

```bash
adb install -r Builds/Quest/DataCenterTeleop.apk
```

### 4. Play

Follow the floating marker and the **NEXT** instructions. Every movement control is hold-to-run:
let go and the robot stops.

| Quest controller | Action |
|---|---|
| Left stick | Drive the robot (also sideways) |
| A / B | Turn the robot clockwise / anticlockwise |
| Right stick | Move the gripper (press the stick in and push to reach forward / back) |
| R2 + right stick | Rotate the wrist |
| L2 (hold) | Close the gripper on a cable. Near the target port it plugs in by itself |
| Right grip + right stick | Teleport and turn |
| X | E-Stop. Press X again, hands off the controls, to reset |
| Y | Show / hide the dashboard |

The controllers buzz faster as the gripper gets close to a cable or a held cable gets close to
its port, a chime plays when you can grab (L2) and when the plug is lined up, and a thud plus a
buzz means the robot ran into a rack: it cannot drive or reach through equipment.

Keyboard controls for the browser version are in [README_DataCenter.md](README_DataCenter.md#controls).

To watch a job done for you first, press **AUTO DEMO** in the top bar (desktop or browser): each
robot in turn picks up its cable and plugs it in. Press any key to take over.

### If something goes wrong

| Problem | Fix |
|---|---|
| Unity asks you to switch to Android | Do step 3.1 (**File > Build Profiles > Android > Switch Platform**) first. |
| The headset is not found | Put the headset on and accept **Allow USB debugging**. Try another cable or port; some cables only charge. |
| Pressing **Play** shows nothing in the headset | Play in the Editor needs Quest Link, which runs on Windows only. On a Mac, use **Build And Run on Quest**. |
| The app closes right after starting | Build with the **Tools > Data Center** menu, not Unity's own Build And Run. The menu builds only the data center scene and clears Android build leftovers from other scenes. |
| Hub cannot find Unity 6000.4.0f1 | Install it from the [Unity download archive](https://unity.com/releases/editor/archive), with Android Build Support. |
