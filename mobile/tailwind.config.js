const { themeColors } = require("./theme.config");

const colors = Object.fromEntries(
  Object.entries(themeColors).map(([name, value]) => [name, value.light])
);

module.exports = {
  content: ["./app/**/*.{js,jsx,ts,tsx}", "./components/**/*.{js,jsx,ts,tsx}", "./lib/**/*.{js,jsx,ts,tsx}"],
  presets: [require("nativewind/preset")],
  theme: {
    extend: { colors },
  },
};
