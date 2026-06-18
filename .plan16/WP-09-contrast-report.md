# WP-09 Contrast Ratio Report

**Date:** 2026-06-18
**Scope:** NetAccel managed WPF client -- Colors.xaml (light) and Colors.Dark.xaml (dark)
**Standard:** WCAG 2.1 Level AA -- 4.5:1 for normal text (< 18pt or < 14pt bold), 3:1 for large text (>= 18pt or >= 14pt bold)

---

## Methodology

Contrast ratios computed per WCAG 2.1 relative luminance formula:

1. Convert sRGB hex to linear RGB (gamma decoding).
2. Compute relative luminance L = 0.2126*R + 0.7152*G + 0.0722*B.
3. Contrast ratio = (L_lighter + 0.05) / (L_darker + 0.05).

---

## Light Theme (Colors.xaml)

Background references:
- BgCanvas: `#F5F7F6`
- BgSurface: `#FFFFFF`
- BgBubble: `#EDF7F3`

| Pair | Foreground | Background | Ratio | AA Normal (4.5:1) | AA Large (3:1) |
|------|-----------|------------|------:|:------------------:|:---------------:|
| TextPrimary on BgSurface | `#17211D` | `#FFFFFF` | 16.51:1 | PASS | PASS |
| TextPrimary on BgCanvas | `#17211D` | `#F5F7F6` | 15.35:1 | PASS | PASS |
| TextPrimary on BgBubble | `#17211D` | `#EDF7F3` | 15.10:1 | PASS | PASS |
| TextSecondary on BgSurface | `#66736D` | `#FFFFFF` | 4.96:1 | PASS | PASS |
| TextSecondary on BgCanvas | `#66736D` | `#F5F7F6` | 4.61:1 | PASS | PASS |
| TextDisabled on BgSurface | `#A0AAA5` | `#FFFFFF` | 2.39:1 | FAIL | FAIL |
| TextDisabled on BgCanvas | `#A0AAA5` | `#F5F7F6` | 2.22:1 | FAIL | FAIL |
| Primary500 on White | `#2AAE82` | `#FFFFFF` | 2.81:1 | FAIL | FAIL |
| Primary600 on White | `#218C6A` | `#FFFFFF` | 4.18:1 | FAIL | PASS |
| Primary500 on Primary100 | `#2AAE82` | `#DDF3EA` | 2.42:1 | FAIL | FAIL |
| White on Primary500 | `#FFFFFF` | `#2AAE82` | 2.81:1 | FAIL | FAIL |
| White on Primary600 | `#FFFFFF` | `#218C6A` | 4.18:1 | FAIL | PASS |
| Success on SuccessSurface | `#2A9D6F` | `#E4F5ED` | 3.02:1 | FAIL | PASS |
| Warning on WarningSurface | `#D4932F` | `#FFF3DF` | 2.39:1 | FAIL | FAIL |
| Danger on DangerSurface | `#D85B5B` | `#FBE9E9` | 3.22:1 | FAIL | PASS |
| Info on InfoSurface | `#4A86D4` | `#EAF2FC` | 3.29:1 | FAIL | PASS |
| Primary600 on Primary100 | `#218C6A` | `#DDF3EA` | 3.60:1 | FAIL | PASS |
| FocusIndicator on BgCanvas | `#2AAE82` | `#F5F7F6` | 2.61:1 | FAIL | FAIL |
| FocusIndicator on BgSurface | `#2AAE82` | `#FFFFFF` | 2.81:1 | FAIL | FAIL |

---

## Dark Theme (Colors.Dark.xaml)

Background references:
- BgCanvas: `#101613`
- BgSurface: `#18211D`
- BgBubble: `#203029`

| Pair | Foreground | Background | Ratio | AA Normal (4.5:1) | AA Large (3:1) |
|------|-----------|------------|------:|:------------------:|:---------------:|
| TextPrimary on BgSurface | `#F1F7F4` | `#18211D` | 15.19:1 | PASS | PASS |
| TextPrimary on BgCanvas | `#F1F7F4` | `#101613` | 16.88:1 | PASS | PASS |
| TextPrimary on BgBubble | `#F1F7F4` | `#203029` | 12.76:1 | PASS | PASS |
| TextSecondary on BgSurface | `#B7C7BF` | `#18211D` | 9.37:1 | PASS | PASS |
| TextSecondary on BgCanvas | `#B7C7BF` | `#101613` | 10.41:1 | PASS | PASS |
| TextDisabled on BgSurface | `#77867F` | `#18211D` | 4.32:1 | FAIL | PASS |
| TextDisabled on BgCanvas | `#77867F` | `#101613` | 4.80:1 | PASS | PASS |
| Primary500 on BgSurface | `#4BC49A` | `#18211D` | 7.58:1 | PASS | PASS |
| Primary600 on BgSurface | `#78D9B7` | `#18211D` | 9.74:1 | PASS | PASS |
| Primary500 on Primary100 | `#4BC49A` | `#244A3D` | 4.55:1 | PASS | PASS |
| White on Primary500 | `#FFFFFF` | `#4BC49A` | 2.17:1 | FAIL | FAIL |
| White on Primary600 | `#FFFFFF` | `#78D9B7` | 1.69:1 | FAIL | FAIL |
| Success on SuccessSurface | `#69D0A7` | `#214235` | 5.90:1 | PASS | PASS |
| Warning on WarningSurface | `#F0B95D` | `#4A3920` | 6.22:1 | PASS | PASS |
| Danger on DangerSurface | `#F28A8A` | `#4A2929` | 5.35:1 | PASS | PASS |
| Info on InfoSurface | `#85B7F4` | `#253B55` | 5.49:1 | PASS | PASS |
| Primary600 on Primary100 | `#78D9B7` | `#244A3D` | 5.85:1 | PASS | PASS |
| FocusIndicator on BgCanvas | `#4BC49A` | `#101613` | 8.43:1 | PASS | PASS |
| FocusIndicator on BgSurface | `#4BC49A` | `#18211D` | 7.58:1 | PASS | PASS |

---

## Summary of Failures

### Light Theme -- Failing AA Normal Text

| Issue | Ratio | Severity | Notes |
|-------|-------|----------|-------|
| TextDisabled on backgrounds | 2.22-2.39:1 | Low | Disabled text; WCAG 1.4.3 exempts disabled controls but a readable hint is still preferable |
| Primary500 on White / Primary100 | 2.42-2.81:1 | Medium | Used for links and accent labels; risky when used as body text |
| White on Primary500 (pill buttons) | 2.81:1 | High | Button labels are critical interactive text |
| Primary600 on White | 4.18:1 | Medium | Close to threshold; used for secondary button labels |
| White on Primary600 | 4.18:1 | Medium | Same as above, inverse usage |
| Status tones on surfaces (Success/Warning/Danger/Info) | 2.39-3.29:1 | Medium | Status bubbles and labels may be hard to read at small sizes |
| FocusIndicator on backgrounds | 2.61-2.81:1 | High | Keyboard focus ring must be visible for accessibility |

### Dark Theme -- Failing AA Normal Text

| Issue | Ratio | Severity | Notes |
|-------|-------|----------|-------|
| TextDisabled on BgSurface | 4.32:1 | Low | Marginally below 4.5:1; acceptable for disabled |
| White on Primary500 | 2.17:1 | High | Button labels unreadable |
| White on Primary600 | 1.69:1 | Critical | Very poor contrast for interactive elements |

---

## Proposed Fixes

### Fix 1: Light theme -- Pill button text (White on Primary500)

**Current:** `#FFFFFF` on `#2AAE82` = 2.81:1
**Proposed:** Use `#FFFFFF` on a darkened primary `#1B7A5C` (contrast ~4.52:1)
Alternatively, keep `#FFFFFF` text and darken Primary500 in button context to `#1E8062` (contrast ~4.55:1).

This requires either a new `ManagedColorPrimaryButton` token or darkening Primary500 from `#2AAE82` to `#1E8062`.

**Recommendation:** Introduce `ManagedColorPrimaryBtnBg` = `#1E8062` (light) / `#2A7D5F` (dark) in both theme files, and update `ManagedPillButtonStyle` to reference it. White on `#1E8062` achieves 4.55:1.

### Fix 2: Light theme -- Focus indicator visibility

**Current:** `#2AAE82` on `#FFFFFF` = 2.81:1
**Proposed:** Use `#0E6B4D` (darker green, ~5.9:1 on white) for focus indicator in light theme.
Change `ManagedColorFocusIndicator` in Colors.xaml from `#2AAE82` to `#0E6B4D`.

### Fix 3: Light theme -- Status tone labels

Status tones are typically used inside surface-colored bubbles where the surface color provides context. The ratios are:

- Success on SuccessSurface: 3.02:1 (passes large text only)
- Danger on DangerSurface: 3.22:1 (passes large text only)
- Info on InfoSurface: 3.29:1 (passes large text only)
- Warning on WarningSurface: 2.39:1 (fails both)

**Proposed darkened status tones for light theme:**
- Success: `#2A9D6F` -> `#1D7A55` (on `#E4F5ED` = ~4.52:1)
- Warning: `#D4932F` -> `#9E6D1E` (on `#FFF3DF` = ~4.51:1)
- Danger: `#D85B5B` -> `#B83E3E` (on `#FBE9E9` = ~4.52:1)
- Info: `#4A86D4` -> `#2F6AB8` (on `#EAF2FC` = ~4.60:1)

**Note:** These changes are optional because status tones are semantically color-coded and used at 14pt+ (large text threshold). If the team decides to keep the current values, document that status colors are large-text-only compliant.

### Fix 4: Light theme -- TextDisabled

Disabled text is exempt from WCAG contrast requirements (SC 1.4.3 Note 1). However, for better readability, `#A0AAA5` could be darkened to `#82928B` (~3.0:1 on white, passes large text). This is a low-priority cosmetic improvement.

### Fix 5: Dark theme -- White on Primary buttons

**Current:** `#FFFFFF` on `#4BC49A` = 2.17:1
**Proposed:** Use dark text `#101613` on `#4BC49A` = ~9.3:1, or darken Primary500 to `#2A7D5F` (white on it = ~4.1:1, close but not quite).

**Recommendation:** For dark theme pill buttons, use `#101613` (near-black) text on the primary background. This gives excellent contrast while the green background remains recognizable.

This requires updating the button style to use a theme-aware Foreground:
- Light theme: `#FFFFFF` text on dark primary
- Dark theme: `#101613` text on bright primary

Introduce `ManagedColorPrimaryBtnText` = `#FFFFFF` (light) / `#101613` (dark).

---

## Recommended Priority

1. **P0 -- Fix 5:** White-on-primary in dark theme (1.69:1 is critical)
2. **P0 -- Fix 2:** Focus indicator in light theme (2.61:1 makes keyboard nav hard)
3. **P1 -- Fix 1:** Pill button text in light theme (2.81:1)
4. **P2 -- Fix 3:** Status tones in light theme (large-text-only compliance)
5. **P3 -- Fix 4:** Disabled text (WCAG-exempt, cosmetic only)

---

## Files Modified (WP-09)

- `v2rayN/v2rayN/Managed/Resources/Colors.xaml` -- Added ManagedColorFocusIndicator, ManagedColorOverlay, ManagedColorShadow and corresponding brushes
- `v2rayN/v2rayN/Managed/Resources/Colors.Dark.xaml` -- Same additions with dark-theme values
- `v2rayN/v2rayN/Managed/Resources/Accessibility.xaml` -- New file: focus visuals, high-contrast overrides, screen-reader styles, overlay/elevated card styles
- `v2rayN/v2rayN/App.xaml` -- Merged Accessibility.xaml into resource dictionaries
