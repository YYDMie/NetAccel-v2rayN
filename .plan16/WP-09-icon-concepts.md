# WP-09 Icon Concepts — NetAccel Application Icon Design

Date: 2026-06-18

## Context

The WPF client project inherited v2rayN branding assets (v2rayN.ico, NotifyIcon1-4.ico).
These need replacement with NetAccel-specific icons that convey "network protection/acceleration"
in a personal learning tool context, not a commercial VPN/proxy service.

Requirements:
- Works at 16px (tray), 32px (taskbar), 48px, 256px (installer/exe)
- Matches the green/teal color scheme: primary #2AAE82, surface #EDF7F3
- Modern, minimal, clean style
- Must be deliverable as XAML vector resources (no raster generation from code)

## Three Concept Directions Considered

### Concept A: "Globe + Signal Waves"

A simplified globe with radiating signal arcs, suggesting global connectivity and
network reach. The arcs would use the primary green, the globe a neutral fill.

- Pros: Universally understood "network" metaphor; scales well as a circle
- Cons: Common in VPN/telecom branding; lacks differentiation; signal waves
  look busy at 16px tray size; does not convey "protection"
- Verdict: Rejected — too generic, no protection/acceleration semantics

### Concept B: "Shield + Lightning Bolt" (CHOSEN)

A rounded shield shape (protection) with a small lightning bolt or speed arrow
inside (acceleration). Shield filled in primary green (#2AAE82) with white
lightning bolt. For the idle tray state, the shield is outline-only in muted
color. For connected, the shield is solid green with a white checkmark overlay.

- Pros: Shield = protection/security (clear user mental model); lightning =
  speed/acceleration (matches the "一键加速" feature); works at 16px because
  the silhouette is simple; distinct from v2rayN's letter-V icon
- Cons: Shield is a common security icon shape (but the lightning differentiates)
- Verdict: Chosen — best balance of meaning, scalability, and brand distinction

### Concept C: "Rocket + Arc Path"

A small rocket or arrow traveling along a curved arc path, suggesting
acceleration and route optimization. The arc would use a gradient from
primary green to a lighter teal.

- Pros: Directly conveys "acceleration"; dynamic/fun feel
- Cons: Does not convey "protection"; rocket silhouette is hard to read at
  16px; gradient rendering in tray icons (which are often 1-bit or low-color)
  degrades badly; may look toy-like
- Verdict: Rejected — poor 16px legibility, no protection semantics

## Chosen Direction: "Shield + Lightning" Details

### Vector Geometry

The shield uses a 32x32 viewbox coordinate space:
- Outer shield path: top-center at (16,2), curves down through (6,4) and (3,12)
  to pointed bottom at (16,29), mirrored on right side
- Inner highlight: slightly smaller shield in Primary600 (#218C6A) for depth
- Lightning bolt: white polygon starting upper-right, zigzagging to lower-left
  through the shield center

### Tray Icon States

| State       | Shield fill       | Overlay element           | DynamicResource key      |
|-------------|-------------------|---------------------------|--------------------------|
| Idle        | TextSecondary     | Horizontal dash           | NetAccelTrayIdle         |
| Connected   | Primary500        | White checkmark           | NetAccelTrayConnected    |
| Faulted     | Danger red        | Red exclamation mark      | NetAccelTrayFaulted      |
| Starting    | Primary500        | White lightning + pulse    | NetAccelTrayStartingBase |

### File Deliverables

1. `Managed/Resources/NetAccelIcon.xaml` — DrawingImage resources for general use
   (header logo, about dialog, etc.). Includes `NetAccelIcon` (detailed) and
   `NetAccelIconSmall` (simplified for 16px).

2. `Managed/Resources/NetAccelTrayIcons.xaml` — Four DrawingImage resources for
   system tray notification icon states. Uses DynamicResource brush references
   so dark/light theme switching works automatically.

3. `ManagedShellWindow.xaml` update — Header logo area now uses `NetAccelIcon`
   DrawingImage via `<Image Source="{DynamicResource NetAccelIcon}" />` instead
   of the MaterialDesign PackIcon.

### .ico File Notes

The existing .ico files (v2rayN.ico, NotifyIcon1-4.ico) are NOT modified by this
task. Replacing them requires exporting the XAML vectors to PNG at 16/32/48/256px
and packaging into .ico format using an image editor (e.g., Inkscape + GIMP,
or a tool like Greenfish Icon Editor). This is a manual step outside the scope
of code-based asset generation.

Recommended .ico replacement mapping:
- v2rayN.ico -> NetAccel.ico (from NetAccelIcon, 256px multi-size)
- NotifyIcon1.ico -> idle state (from NetAccelTrayIdle)
- NotifyIcon2.ico -> connected state (from NetAccelTrayConnected)
- NotifyIcon3.ico -> faulted state (from NetAccelTrayFaulted)
- NotifyIcon4.ico -> starting state (from NetAccelTrayStartingBase)
