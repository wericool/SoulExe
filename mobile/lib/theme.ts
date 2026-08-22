/**
 * SoulExe Mobile — Telegram-inspired dark theme.
 * Palette close to Telegram Android "Night" (not pure black card blocks).
 * Interaction patterns ported from ChatActivityEnterView (GPL) concepts into RN.
 */
export const colors = {
  // Telegram-like surfaces
  background: "#0E1621",
  surface0: "#0E1621",
  panel: "#17212B",
  card: "#182533",
  elevated: "#232E3C",
  input: "#242F3D",
  border: "#0E1621",
  borderStrong: "#2B3645",
  hairline: "rgba(255,255,255,0.06)",

  text: "#F5F5F5",
  textSecondary: "#C5D0DC",
  muted: "#8B9AAB",
  dim: "#6D7F91",

  // TG accent teal/blue family + brand violet for primary actions
  accent: "#5288C1",
  accentHover: "#6FA3D8",
  accentBlue: "#2AABEE",
  accentSoft: "rgba(42, 171, 238, 0.16)",
  send: "#2AABEE",
  link: "#6BC3F5",

  success: "#4FAE4E",
  successText: "#A0D8A0",
  danger: "#E53935",
  dangerBg: "#4A2222",
  warning: "#E8B86D",

  // Bubbles (Telegram style)
  bubbleIn: "#182533",
  bubbleOut: "#2B5278",
  bubbleMine: "#2B5278",
  bubbleMineBorder: "transparent",
  bubbleDirector: "rgba(42, 171, 238, 0.14)",

  online: "#4DCD5E",
  badge: "#2AABEE",
  overlay: "rgba(14, 22, 33, 0.72)",
} as const;

export const radii = {
  sm: 6,
  md: 10,
  lg: 14,
  xl: 18,
  bubble: 16,
  enter: 22,
  pill: 999,
} as const;

export const space = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 20,
  xxl: 28,
} as const;

/** Telegram chat list row height ~72dp, enter min height ~48 */
export const layout = {
  chatRowHeight: 72,
  threadHeaderHeight: 56,
  enterMinHeight: 48,
  enterMaxHeight: 120,
  enterButton: 40,
  avatarList: 54,
  avatarHeader: 42,
} as const;

export const typography = {
  hero: { fontSize: 26, fontWeight: "700" as const, letterSpacing: -0.3 },
  title: { fontSize: 19, fontWeight: "700" as const, letterSpacing: -0.2 },
  section: { fontSize: 15, fontWeight: "600" as const },
  body: { fontSize: 15, fontWeight: "400" as const, lineHeight: 21 },
  caption: { fontSize: 13, fontWeight: "400" as const },
  label: { fontSize: 11, fontWeight: "600" as const, letterSpacing: 0.4 },
  meta: { fontSize: 12, fontWeight: "400" as const },
  chatTitle: { fontSize: 16, fontWeight: "600" as const },
  chatPreview: { fontSize: 14, fontWeight: "400" as const, lineHeight: 18 },
  bubble: { fontSize: 15, fontWeight: "400" as const, lineHeight: 21 },
  time: { fontSize: 11, fontWeight: "400" as const },
};
