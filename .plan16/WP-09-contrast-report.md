# WP-09 Contrast Report

**Date:** 2026-06-19

**Scope:** Managed WPF light and dark themes

**Standard:** WCAG 2.1 AA, 4.5:1 normal text and 3:1 large text/non-text focus indicators

Ratios use the WCAG relative-luminance formula. This report records the final tokens after the
WP-09 acceptance fixes, not the earlier failing proposal values.

## Light theme

| Usage | Foreground | Background | Ratio | Result |
|-------|------------|------------|------:|--------|
| Primary text on surface | `#17211D` | `#FFFFFF` | 16.51:1 | PASS |
| Secondary text on canvas | `#66736D` | `#F5F7F6` | 4.61:1 | PASS |
| Primary button text | `#FFFFFF` | `#1B7A5C` | 5.27:1 | PASS |
| Primary button hover text | `#FFFFFF` | `#145C45` | 7.93:1 | PASS |
| Secondary action text | `#0E6B4D` | `#DDF3EA` | 5.60:1 | PASS |
| Focus indicator on surface | `#0E6B4D` | `#FFFFFF` | 6.50:1 | PASS |
| Success on success surface | `#1D7A55` | `#E4F5ED` | 4.68:1 | PASS |
| Warning on warning surface | `#8A5A12` | `#FFF3DF` | 5.39:1 | PASS |
| Danger on danger surface | `#B83E3E` | `#FBE9E9` | 4.72:1 | PASS |
| Info on info surface | `#2F6AB8` | `#EAF2FC` | 4.80:1 | PASS |

`ManagedColorTextDisabled` is retained for disabled controls. Disabled controls are exempt from
WCAG 2.1 SC 1.4.3 and are not used for active instructions or status information.

## Dark theme

| Usage | Foreground | Background | Ratio | Result |
|-------|------------|------------|------:|--------|
| Primary text on surface | `#F1F7F4` | `#18211D` | 15.19:1 | PASS |
| Secondary text on canvas | `#B7C7BF` | `#101613` | 10.41:1 | PASS |
| Primary button text | `#101613` | `#4BC49A` | 8.43:1 | PASS |
| Primary button hover text | `#101613` | `#78D9B7` | 10.82:1 | PASS |
| Secondary action text | `#78D9B7` | `#244A3D` | 5.85:1 | PASS |
| Focus indicator on canvas | `#4BC49A` | `#101613` | 8.43:1 | PASS |
| Success on success surface | `#69D0A7` | `#214235` | 5.90:1 | PASS |
| Warning on warning surface | `#F0B95D` | `#4A3920` | 6.22:1 | PASS |
| Danger on danger surface | `#F28A8A` | `#4A2929` | 5.35:1 | PASS |
| Info on info surface | `#85B7F4` | `#253B55` | 5.49:1 | PASS |

## Applied corrections

- Introduced theme-specific primary button background, hover, text, and secondary action tokens.
- Changed the light focus indicator to `#0E6B4D`.
- Darkened the four light-theme semantic status colors.
- Applied the tokens to production buttons, inputs, navigation, check boxes, and combo boxes.
- Verified both production theme dictionaries through the real WPF smoke test and visual renders.
