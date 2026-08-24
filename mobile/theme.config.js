/**
 * Cross-platform semantic colour tokens.
 *
 * This file is deliberately plain CommonJS: Metro, NativeWind and the runtime
 * theme helper all load it directly.
 */
const themeColors = {
  primary: { light: "#2A76B8", dark: "#5288C1" },
  background: { light: "#F4F7FA", dark: "#0E1621" },
  surface: { light: "#FFFFFF", dark: "#17212B" },
  foreground: { light: "#18212B", dark: "#F5F5F5" },
  muted: { light: "#687789", dark: "#8B9AAB" },
  border: { light: "#D9E1E8", dark: "#2B3645" },
  success: { light: "#308C3B", dark: "#4FAE4E" },
  warning: { light: "#B77718", dark: "#E8B86D" },
  error: { light: "#C83733", dark: "#E53935" },
};

module.exports = { themeColors };
