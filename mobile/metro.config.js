const { getDefaultConfig } = require("expo/metro-config");

const config = getDefaultConfig(__dirname);

// The managed preview shares a constrained sandbox with the API process.
// A single transformer worker keeps the initial web bundle from being OOM-killed.
config.maxWorkers = 1;

module.exports = config;
