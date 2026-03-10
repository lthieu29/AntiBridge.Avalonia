require('dotenv').config();

module.exports = {
    // Antigravity CDP
    cdpUrl: process.env.ANTIGRAVITY_CDP_URL || 'http://127.0.0.1:9222',

    // Bridge Server
    bridgePort: parseInt(process.env.BRIDGE_PORT) || 3000,

    // Timeouts
    responseTimeout: parseInt(process.env.RESPONSE_TIMEOUT) || 180000, // 3 phút cho translation chunks
    pollInterval: parseInt(process.env.POLL_INTERVAL) || 500,          // 500ms giữa mỗi lần poll

    // Logging
    logLevel: process.env.LOG_LEVEL || 'info',

    // Translation
    chunkSize: 20,          // Số cue mỗi chunk
    cacheDir: './cache',    // Thư mục cache
};
