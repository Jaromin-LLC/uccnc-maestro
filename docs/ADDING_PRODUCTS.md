# Adding Products and Steps



(uc)CNC Maestro is edited from the **Admin** tab in the plugin window. Projects are saved to `C:\UCCNC\Maestro\projects.json`; tools are saved to `C:\UCCNC\Maestro\tools.json`.



## Tool library



Tools are defined once in the **Admin → Tools** tab and referenced by steps via a numeric **toolId** (internal surrogate key; not shown in the UI).



1. Open **Admin → Tools**

2. Click **Add** (or duplicate an existing tool)

3. Set **Tool #** (T-number), **Type**, **Diameter**, **Description**, and optional **Tool Image**

4. Tool images are copied to `C:\UCCNC\Maestro\Media\Tools\`

5. Click **Save All**



When editing a project step, pick a tool from the **Tool** dropdown or click **New Tool...** to create one in the library and assign it to the step.



## Add a new project



1. Open UCCNC with the (uc)CNC Maestro plugin enabled

2. Switch to the **Admin** tab → **Projects**

3. Click **Add** in the project list

4. Set **Project ID** (unique, no spaces), **Project Name**, and **Description**

5. Add steps with **Add Step**

6. Click **Save All**



## Step types



| Type | Purpose |

|------|---------|

| `op` | Runs a G-code file with tool prompt, auto-zero, and cycle start |

| `gate` | Operator confirmation only (flip, register, inspect, etc.) |



## Operation step fields



- **G-code File**: full path to the `.nc` file (use **Browse**)

- **Instructions**: shown in the guided panel before the operator confirms

- **Tool**: select from the tool library (or **New Tool...**)

- **Step Photo / Video**: optional media copied into `C:\UCCNC\Maestro\Media\<projectId>\`



Runtime is recorded automatically after the first run (shown in the Operator grid).



## Auto-operations (pre/post)



Each operation step can configure **Pre Ops** and **Post Ops**:



| ID | Action |

|----|--------|

| `moveToolChange` | Move to tool-change position (safe Z first) |

| `toolPrompt` | Show guided install/confirm panel |

| `autoZero` | Two-pass fixed-plate probe (every tool change) |

| `spindleOff` | M5 |

| `gotoWorkZero` | G0 X0 Y0 |

| `parkG28` | Rapid to UCCNC G28 (homed machine origin; not adjustable in-plugin) |

| `parkG30` | Rapid to UCCNC G30 (second predefined position) |

| `parkCustom` | Rapid to the adjustable **Park X/Y/Z** machine coordinates (safe Z first) |

| `customMdi` | Run custom MDI from step (future field) |



Default for `op` steps: pre = moveToolChange, toolPrompt, autoZero; post = spindleOff, moveToolChange.



## Machine settings (Admin → Settings)



- **Media Root**: `C:\UCCNC\Maestro\Media`

- **Test mode**: skips probing checks (demo only — never use when cutting)



## Seed config in repo



[plugin/config/projects.json](../plugin/config/projects.json) and [plugin/config/tools.json](../plugin/config/tools.json) are version-controlled defaults used to seed `C:\UCCNC\Maestro` on a **first install only** — installers never overwrite existing `projects.json` / `tools.json` on the machine.

To change workflows on a machine that is already installed, edit live in Admin and **Save All** (no reinstall needed), or copy the files into `C:\UCCNC\Maestro` manually.


