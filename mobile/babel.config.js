module.exports = function (api) {
  api.cache(true);

  return {
    presets: [
      ["babel-preset-expo", { jsxImportSource: "nativewind" }],
      "nativewind/babel",
    ],
    // Must stay last: it marks callbacks used by Reanimated 4 / Worklets as
    // executable on the UI runtime.
    plugins: ["react-native-worklets/plugin"],
  };
};
