const path = require("node:path");
const { getDefaultConfig } = require("expo/metro-config");
const { withNativeWind } = require("nativewind/metro");

const config = getDefaultConfig(__dirname);

// Android native builds keep pnpm's virtual store on a short path to avoid
// Windows CMake path-length limits. Metro must explicitly be allowed to read
// packages resolved from that external directory.
config.watchFolders = [path.resolve(__dirname, "../../../../p")];
config.resolver.nodeModulesPaths = [
  path.resolve(__dirname, "node_modules"),
  ...(config.resolver.nodeModulesPaths ?? []),
];

module.exports = withNativeWind(config, { input: "./global.css" });
