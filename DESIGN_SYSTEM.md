# Marathon Design System Reference

> Deep space. Neon signals. Holographic data.  
> Inspired by Bungie's Marathon aesthetic — angular HUD, neon accents, frosted glass.

---

## Table of Contents

1. [Typography](#typography)
2. [Spacing Scale](#spacing-scale)
3. [Border Radii](#border-radii)
4. [Motion / Transitions](#motion--transitions)
5. [Color Tokens — Light Mode](#color-tokens--light-mode)
6. [Color Tokens — Dark Mode](#color-tokens--dark-mode)
7. [Color Palettes (Themes)](#color-palettes-themes)
8. [Shadows](#shadows)
9. [Keyframe Animations](#keyframe-animations)
10. [Icons (SVG)](#icons-svg)
11. [Components](#components)
12. [Layout](#layout)
13. [Responsive Breakpoints](#responsive-breakpoints)
14. [Utilities](#utilities)
15. [Dark Mode Enhancements](#dark-mode-enhancements)

---

## Typography

```css
--font-main: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
--font-mono: 'JetBrains Mono', 'SF Mono', 'Fira Code', 'Cascadia Code', monospace;
--font-display: var(--font-mono);
```

**Conventions:**
- Headings, labels, badges, KPIs → `font-family: var(--font-mono)`
- Body text → `font-family: var(--font-main)`
- Labels use `text-transform: uppercase; letter-spacing: 0.04em–0.12em`
- Numeric displays use `font-variant-numeric: tabular-nums`

**Font Sizes Used:**
| Context | Size |
|---------|------|
| KPI values (large) | `1.4rem – 1.8rem` |
| Title (detail view) | `1.25rem` |
| Section headers / H2 | `0.85rem – 1rem` |
| Body / inputs | `0.85rem` |
| Card titles | `0.8rem` |
| Labels / pills | `0.68rem – 0.78rem` |
| Micro labels / badges | `0.6rem – 0.65rem` |

---

## Spacing Scale

```css
--sp-2xs: 2px;
--sp-xs:  4px;
--sp-sm:  8px;
--sp-md:  16px;
--sp-lg:  24px;
--sp-xl:  32px;
--sp-2xl: 48px;
--sp-3xl: 64px;
```

---

## Border Radii

Angular, aggressive — minimal rounding.

```css
--radius-xs:    2px;
--radius-sm:    3px;
--radius-md:    4px;
--radius-lg:    6px;
--radius-round: 50%;
```

Border width is always `--border-w: 1px`.

---

## Motion / Transitions

```css
--t-fast: 0.1s ease;       /* hover, active states */
--t-base: 0.18s ease;      /* standard transitions */
--t-slow: 0.3s cubic-bezier(0.16, 1, 0.3, 1);  /* slide-in, theme changes */
```

---

## Color Tokens — Light Mode

### Backgrounds
```css
--bg:               #f5f5f7;
--bg-secondary:     #ffffff;
--bg-tertiary:      #edeef1;
--surface-elevated: #f8f8fa;
--surface-sunken:   #e8e9ed;
--bg-gradient:      linear-gradient(180deg, #f5f5f7 0%, #edeef1 100%);
```

### Text
```css
--text:           #1a1a2e;
--text-secondary: #44495c;
--text-muted:     #7c819a;
--text-dim:       #b0b3c4;
```

### Primary / Accent (default = Runner)
```css
--primary:         #ff2d55;
--primary-hover:   #e02248;
--primary-light:   rgba(255, 45, 85, 0.07);
--primary-dark:    #cc2244;
--primary-glow:    rgba(255, 45, 85, 0.12);
--primary-surface: rgba(255, 45, 85, 0.03);

--accent:        #ff2d55;
--accent-hover:  #e02248;
--accent-bg:     rgba(255, 45, 85, 0.04);
--accent-yellow: #e8920a;
--accent-cyan:   #0097a7;
--accent-pink:   #e8305a;
--accent-green:  #1db954;
```

### Borders
```css
--border-color:  #dcdee4;
--border-light:  #ecedf1;
--border-accent: rgba(255, 45, 85, 0.14);
```

### Header / Tab
```css
--header-bg:     rgba(245, 245, 247, 0.82);
--header-text:   #1a1a2e;
--tab-bg:        rgba(245, 245, 247, 0.88);
--tab-active-bg: rgba(255, 45, 85, 0.06);
```

### Status Colors
```css
--success: #1db954;
--warning: #e8920a;
--error:   #e53935;
--info:    #2979ff;

--status-reading:   #ff2d55;
--status-completed: #1db954;
--status-wishlist:  #2979ff;
--status-dnf:       #8e8e93;
--status-error:     #e53935;
```

### Overlays
```css
--overlay-bg:    rgba(26, 26, 46, 0.45);
--overlay-heavy: rgba(26, 26, 46, 0.6);
```

---

## Color Tokens — Dark Mode

Set via `[data-theme='dark']` on root element.

### Backgrounds
```css
--bg:               #06080e;
--bg-secondary:     #0c0e18;
--bg-tertiary:      #121522;
--surface-elevated: #1a1e30;
--surface-sunken:   #04060a;
--bg-gradient:      linear-gradient(180deg, #06080e 0%, #080a12 40%, #0e1018 100%);
```

### Text
```css
--text:           #e4e8f4;
--text-secondary: #8892ad;
--text-muted:     #505a74;
--text-dim:       #2a3040;
```

### Primary / Accent (dark)
```css
--primary:         #ff2d55;
--primary-hover:   #ff4d6f;
--primary-light:   rgba(255, 45, 85, 0.1);
--primary-dark:    #ff6b8a;
--primary-glow:    rgba(255, 45, 85, 0.3);
--primary-surface: rgba(255, 45, 85, 0.05);

--accent:        #ff2d55;
--accent-hover:  #ff4d6f;
--accent-bg:     rgba(255, 45, 85, 0.05);
--accent-yellow: #ffd60a;
--accent-cyan:   #00e5ff;
--accent-pink:   #ff375f;
--accent-green:  #30d158;
```

### Borders
```css
--border-color:  #1e2438;
--border-light:  #181d2e;
--border-accent: rgba(255, 45, 85, 0.18);
```

### Header / Tab
```css
--header-bg:     rgba(10, 12, 20, 0.78);  /* actual render: rgba(6, 8, 14, 0.88) */
--tab-bg:        rgba(10, 12, 20, 0.82);  /* actual render: rgba(6, 8, 14, 0.92) */
--tab-active-bg: rgba(255, 45, 85, 0.08);
```

### Status Colors (dark)
```css
--success: #30d158;
--warning: #ffd60a;
--error:   #ff453a;
--info:    #64d2ff;

--status-reading:   #ff2d55;
--status-completed: #30d158;
--status-wishlist:  #64d2ff;
--status-dnf:       #636366;
--status-error:     #ff453a;
```

### Overlays
```css
--overlay-bg:    rgba(6, 8, 15, 0.6);
--overlay-heavy: rgba(6, 8, 15, 0.75);
```

### Scanline / CRT
```css
--scanline-opacity: 0.025;  /* light mode = 0 */
```

---

## Color Palettes (Themes)

Switchable via `data-color` attribute on root element. Each overrides `--primary`, `--primary-*`, `--accent*`, `--border-accent`, `--shadow-focus`, `--shadow-glow`, `--shadow-neon`, `--tab-active-bg`, and dark-mode background tints.

| Name | Light Primary | Dark Primary | Dark BG Tint |
|------|--------------|-------------|--------------|
| **Runner** (default) | `#ff2d55` | `#ff2d55` | standard dark |
| **Violet** | `#7c3aed` | `#a78bfa` | `#0c0a14` / `#120f1c` / `#1a1628` |
| **Coral** | `#f97316` | `#fb923c` | `#110c0a` / `#18120f` / `#241c16` |
| **Ocean** | `#0284c7` | `#38bdf8` | `#080c14` / `#0e121c` / `#141a28` |
| **Mint** | `#059669` | `#34d399` | `#080e0c` / `#0e1614` / `#14201c` |
| **Sunset** | `#dc2626` | `#f87171` | `#110a0a` / `#1a1010` / `#241616` |
| **Bubblegum** | `#db2777` | `#f472b6` | `#110a0e` / `#1a0f16` / `#24141e` |
| **Slate** | `#475569` | `#94a3b8` | `#0c0d10` / `#131418` / `#1a1c22` |
| **Gold** | `#b45309` | `#fbbf24` | `#100e08` / `#18150e` / `#221e14` |

---

## Shadows

### Light Mode
```css
--shadow-xs:         0 1px 2px rgba(0,0,0,0.04);
--shadow-sm:         0 1px 3px rgba(0,0,0,0.06), 0 1px 2px rgba(0,0,0,0.04);
--shadow-md:         0 4px 12px rgba(0,0,0,0.07), 0 1px 3px rgba(0,0,0,0.04);
--shadow-lg:         0 12px 40px rgba(0,0,0,0.1), 0 4px 12px rgba(0,0,0,0.05);
--shadow-focus:      0 0 0 3px rgba(255,45,85,0.16);
--shadow-glow:       none;
--shadow-neon:       none;
--shadow-card:       0 1px 3px rgba(0,0,0,0.04);
--shadow-card-hover: 0 8px 24px rgba(0,0,0,0.08), 0 2px 6px rgba(0,0,0,0.04);
```

### Dark Mode
```css
--shadow-xs:         0 1px 2px rgba(0,0,0,0.5);
--shadow-sm:         0 1px 4px rgba(0,0,0,0.5);
--shadow-md:         0 4px 16px rgba(0,0,0,0.6);
--shadow-lg:         0 12px 40px rgba(0,0,0,0.6);
--shadow-focus:      0 0 0 2px rgba(255,45,85,0.4), 0 0 16px rgba(255,45,85,0.15);
--shadow-glow:       0 0 20px rgba(255,45,85,0.25), 0 0 60px rgba(255,45,85,0.08);
--shadow-neon:       0 0 6px rgba(255,45,85,0.45), 0 0 20px rgba(255,45,85,0.15), 0 0 40px rgba(255,45,85,0.05);
--shadow-card:       0 2px 8px rgba(0,0,0,0.4), 0 0 1px rgba(255,45,85,0.08);
--shadow-card-hover: 0 8px 32px rgba(0,0,0,0.45), 0 0 12px rgba(255,45,85,0.1);
```

---

## Keyframe Animations

```css
@keyframes fadeIn       { 0% { opacity: 0 } 100% { opacity: 1 } }
@keyframes slideUp      { 0% { opacity: 0; transform: translateY(12px) } 100% { opacity: 1; transform: translateY(0) } }
@keyframes slideDown    { 0% { opacity: 0; transform: translateY(-12px) } 100% { opacity: 1; transform: translateY(0) } }
@keyframes scaleIn      { 0% { opacity: 0; transform: scale(0.95) } 100% { opacity: 1; transform: scale(1) } }
@keyframes slideIn      { 0% { transform: translateX(100%) } 100% { transform: translateX(0) } }
@keyframes neonPulse    { 0%,100% { opacity: 1 } 50% { opacity: 0.7 } }
@keyframes glitchFlicker{ 0%{opacity:1} 3%{opacity:0.4} 6%{opacity:1} 92%{opacity:1} 95%{opacity:0.6} 97%{opacity:1} }
@keyframes scanSweep    { 0% { transform: translateY(-100%) } 100% { transform: translateY(100vh) } }
@keyframes progressGlow { 0%,100% { box-shadow: 0 0 4px var(--primary-glow) } 50% { box-shadow: 0 0 12px var(--primary-glow), 0 0 24px var(--primary-glow) } }
@keyframes borderGlow   { 0%,100% { border-color: var(--border-accent) } 50% { border-color: var(--primary) } }
@keyframes toastIn      { 0% { opacity: 0; transform: translateX(40px) } 100% { opacity: 1; transform: translateX(0) } }
@keyframes toastOut     { 0% { opacity: 1; transform: translateX(0) } 100% { opacity: 0; transform: translateX(40px) } }
@keyframes shimmer      { 0% { transform: translateX(-100%) } 100% { transform: translateX(100%) } }
```

---

## Icons (SVG)

All icons are Feather-style inline SVGs with `viewBox="0 0 24 24"`, `fill="none"`, `stroke="currentColor"`, `stroke-width="1.5"`.

**Available icons:**
`book`, `plus`, `search`, `x`, `settings`, `sun`, `moon`, `monitor`, `grid`, `sort`, `check`, `trash`, `download`, `upload`, `edit`, `star`, `folder`, `zap`, `command`, `headphones`, `tablet`, `chevronDown`, `image`, `flame`, `starFilled`, `starHalf`, `clock`, `tag`, `barChart`, `play`, `pause`, `stop`, `skipBack`, `skipForward`, `rewind`, `fastForward`, `bookOpen`, `chevronRight`, `chevronLeft`, `target`, `trendingUp`, `calendar`, `volume2`, `gauge`, `arrowRight`, `maximize`, `bookmark`

**SVG sizing by context:**

| Context | Size |
|---------|------|
| Default `svg` | `1em × 1em` |
| `.empty-state > svg` | `64px`, `stroke-width: 1`, `color: var(--text-dim)` |
| `.tab-button svg` | `16px` |
| `.btn svg`, `.btn-icon svg` | `16px` |
| `.btn-primary-icon svg` | `18px` |
| `.insight-card-icon svg` | `24px` |
| `.now-playing-btn svg` | `16px` |
| `.now-playing-btn-main svg` | `20px` |
| `.audio-btn svg` | `18px` |
| `.audio-btn-play svg` | `24px` |
| `.command-palette-item-icon svg` | `16px` |

---

## Components

### Reset / Base
- `box-sizing: border-box` on everything
- `font-size: 16px` on `<html>`
- `-webkit-font-smoothing: antialiased`
- `line-height: 1.5` on `<body>`
- All `button`, `input`, `select`, `textarea` inherit font, have no border/outline/background
- `svg` default: `1em × 1em`, `flex-shrink: 0`, `vertical-align: middle`

### Buttons

**`.btn`** — Default button
- `font-family: var(--font-mono); font-size: 0.78rem; font-weight: 600`
- `letter-spacing: 0.04em; text-transform: uppercase`
- `padding: var(--sp-sm) var(--sp-md)`
- `background: var(--bg-tertiary); border: 1px solid var(--border-color); border-radius: var(--radius-sm)`
- Hover: primary border + primary-light bg
- Dark hover: adds `box-shadow: 0 0 8px var(--primary-glow)`

**`.btn-primary`** — Primary CTA
- `color: #fff; background: var(--primary); border-color: var(--primary)`
- Dark: neon glow with `--shadow-neon`

**`.btn-icon`** — Icon-only button
- `44px × 44px min`, transparent bg, no border
- Hover: primary color + primary-light bg + accent border

**`.btn-danger`** — Destructive action
- `color: var(--error); border: rgba(255,59,48,0.2); bg: rgba(255,59,48,0.04)`
- Hover: white text on `var(--error)` bg

### Cards (`.book-card`)
- `flex-direction: column; background: var(--bg-secondary); border: 1px solid var(--border-color); border-radius: var(--radius-md)`
- Cover: `aspect-ratio: 2/3`, image `object-fit: cover`
- Hover: `translateY(-3px)`, primary border, card-hover shadow
- Dark hover: neon box-shadow
- Selected: `box-shadow: 0 0 0 1px var(--primary)`
- Placeholder: monospace 2rem bold centered, optional `.shimmer` animation
- Badge: absolute top-left, mono 0.65rem, white on primary
- Info section: title (0.8rem mono bold, 2-line clamp), author (0.72rem), stars, progress bar
- Tags: 0.6rem mono pills in bg-tertiary

### Filter Chips (`.filter-chip`)
- Mono 0.72rem, uppercase, border pill
- Active: primary color + primary-light bg + primary border
- Dark active: 6px glow

### Editor Panel (`.editor-panel`)
- Slides in from right (`translateX(100%)` → `translateX(0)`)
- Max-width 520px, full height
- Overlay: `var(--overlay-bg)` with `blur(8px)`
- Form inputs: `bg: var(--bg-tertiary); border: 1px solid var(--border-color); border-radius: var(--radius-sm)`
- Focus: primary border + focus shadow (dark adds glow)
- Labels: mono 0.68rem, uppercase, 0.08em spacing
- Section dividers: primary-colored mono title with top border

### Tag Chips (`.tag-chip`)
- `font-family: var(--font-mono); font-size: 0.68rem`
- `color: var(--primary); bg: var(--primary-light); border: 1px solid var(--border-accent)`
- Dark: 4px primary glow

### Detail View
- Overlay: `var(--overlay-heavy)` with `blur(12px)`
- Max-width 720px centered
- Hero: flex row with 140px cover + info
- Section cards: `bg: var(--bg-secondary); border: 1px; border-radius: var(--radius-md)`
- Section headers: mono 0.72rem, bold, uppercase, primary color (dark: text-shadow glow)
- Progress ring: SVG with `stroke: var(--primary)`, dark has `drop-shadow` glow
- Status badge: inline-flex, mono 0.65rem, white on primary, rounded

### Audio Player
- Panel: `bg: var(--bg-secondary); border: 1px; border-radius: var(--radius-md)`
- Dark: inset shadow + accent border
- Waveform: flex bars 2px wide, active bars = primary (dark: glow)
- Play button: 48px, white on primary, square-ish (radius-sm)
- Speed pills: connected button group, active = primary
- Times: mono 0.72rem, tabular-nums

### Now Playing Bar
- Fixed bottom, full width
- `backdrop-filter: blur(20px)`
- Progress bar: 3px height, primary fill (dark: glow spread)
- Cover thumb: 40px square
- Controls: 32px icon buttons, 36px main play button

### Command Palette
- Centered overlay, max-width 560px
- `scaleIn` animation
- Input: mono 0.9rem, transparent bg, bottom border
- Results scrollable 360px max
- Items: hover shows primary-light bg (dark: 8px glow)

### Modal
- Centered overlay with `blur(8px)`
- Max-width 440px
- `scaleIn` animation
- Dark: accent border + glow + 1px primary outline
- Header/footer with border separators

### Toast
- Fixed top-right (mobile: bottom-center)
- `toastIn` / `toastOut` animations
- `min-width: 280px; max-width: 400px`
- Left 3px accent stripe per type (success/warning/error/info)
- Dark: type-specific glow on left stripe

### Stats / KPIs
- KPI row: 4-column grid
- KPI card: centered column, mono values
- Dark: 2px primary top accent with glow, value = primary color with text-shadow
- Stats panels: `bg: var(--bg-secondary); border: 1px` with primary H3 headers
- Dark panels: top 1px gradient accent line
- Genre bars: label (100px) + track + count
- Heatmap: 5 levels (0–4) using `color-mix()` with primary
- Achievement badges: earned = primary-light bg, locked = 0.4 opacity + grayscale

### Insight Cards
- Flex row with 3px left accent stripe (primary)
- Value: mono 1.4rem bold (dark: primary color + glow)
- Label: mono 0.68rem uppercase

### Settings
- Max-width 640px centered
- Cards: `bg: var(--bg-secondary); border: 1px; border-radius: var(--radius-md); padding: var(--sp-lg)`
- Theme toggle: connected button group
- Color swatches: 36px squares with inner 18px dot

### Reading Timer
- Mono 1.8rem bold primary display with tabular-nums
- Dark: text-shadow glow

### Scanner
- Full-screen overlay with video viewport
- `border: 2px solid var(--primary)` on viewport
- Dark: 16px primary glow around viewport

---

## Layout

### App Shell
```
.app-shell        → flex column, 100dvh, overflow hidden
  .app-header     → sticky top, z-100, frosted glass (blur 20px, saturate 1.5)
  .app-main       → flex 1, overflow-y auto
  .tab-bar        → bottom nav, frosted glass (blur 16px)
```

### Header
- `min-height: 56px`
- Safe area padding on all edges
- Dark: accent bottom border with glow shadow

### Tab Bar
- Horizontal scroll, hidden scrollbar
- Tab buttons: mono 0.75rem, uppercase
- Active: primary color, accent bg, neon underline (`::after` pseudo)
- Dark active: 12px glow + neon pulse animation on underline

### Book Grid
```css
grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));  /* default */
grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));  /* ≥768px */
grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));  /* ≥1200px */
```

### Shelf Pills
- Flex wrap row of pills
- Same styling as filter chips

---

## Responsive Breakpoints

| Breakpoint | Target |
|-----------|--------|
| `≤ 380px` | Extra small — 120px min cards, 48px header |
| `≤ 640px` | Mobile — stacked layouts, full-width editor, bottom-center toasts |
| `≥ 768px` | Tablet — larger grid cards, more padding |
| `≥ 1200px` | Desktop — 220px min grid cards |

### Key Mobile Adaptations (≤ 640px)
- Header: reduced padding, full-width search
- Tab bar: `justify-content: space-around`, hide labels (icon only), 44px min-height
- Book grid: 140px min columns
- Detail hero: vertical stack, centered
- Stats grid: 1 column
- KPI row: 2 columns
- Editor: full-screen width
- Settings rows: stack vertically
- Toast: bottom-center
- Command palette: full width

---

## Utilities

```css
.sr-only   { /* screen-reader only — visually hidden */ }
.truncate  { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.hidden    { display: none !important; }
```

---

## Dark Mode Enhancements

Applied when `[data-theme='dark']` is set:

1. **Scanline CRT overlay** — repeating 2px horizontal lines at low opacity on `.app-shell::before`
2. **Vignette effect** — radial gradient darkening edges on `.app-main::after`
3. **Cyberpunk grid** — subtle 40px grid lines on `.app-main` background
4. **Neon accents** — `box-shadow` glows on primary elements (buttons, active tabs, cards on hover, badges)
5. **Text glow** — `text-shadow` on primary-colored headings and values
6. **Neon pulse** — animated opacity on active tab underlines
7. **Glitch flicker** — subtle opacity flicker on page titles
8. **Progress glow** — animated box-shadow on progress bars

### How to Apply Theme
```html
<html data-theme="dark" data-color="runner">
```

Supported `data-theme` values: `light` (default), `dark`  
Supported `data-color` values: `runner`, `violet`, `coral`, `ocean`, `mint`, `sunset`, `bubblegum`, `slate`, `gold`

---

## Scrollbar

```css
::-webkit-scrollbar        { width: 6px; height: 6px }
::-webkit-scrollbar-track  { background: transparent }
::-webkit-scrollbar-thumb  { background: var(--border-color); border-radius: 0 }
::-webkit-scrollbar-thumb:hover { background: var(--primary) }
/* dark: hover adds 6px primary glow */
```

## Focus / Selection

```css
:focus-visible { outline: none; box-shadow: var(--shadow-focus) }
::selection    { background: var(--primary); color: #fff }
```

---

## Quick Start — Copying to a New App

1. **Copy `variables.css`** — contains all tokens for both themes and all color palettes
2. **Copy `reset.css`** — base reset, SVG sizing, scanline/vignette effects, keyframes
3. **Set the theme** on your HTML root:
   ```html
   <html data-theme="dark" data-color="ocean">
   ```
4. **Use the CSS variables** in your components:
   ```css
   .my-card {
     background: var(--bg-secondary);
     border: var(--border-w) solid var(--border-color);
     border-radius: var(--radius-md);
     box-shadow: var(--shadow-card);
     font-family: var(--font-mono);
     color: var(--text);
     transition: all var(--t-base);
   }
   .my-card:hover {
     border-color: var(--primary);
     box-shadow: var(--shadow-card-hover);
   }
   [data-theme='dark'] .my-card:hover {
     box-shadow: var(--shadow-neon), var(--shadow-card-hover);
   }
   ```
5. **Use the icon helpers** from `icons.js` — each is a template string you can insert as `innerHTML`
