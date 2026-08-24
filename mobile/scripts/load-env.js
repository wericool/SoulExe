const path = require("node:path");
const dotenv = require("dotenv");

// Load local defaults without replacing values explicitly supplied by the host process.
dotenv.config({ path: path.resolve(__dirname, "..", ".env"), override: false, quiet: true });
