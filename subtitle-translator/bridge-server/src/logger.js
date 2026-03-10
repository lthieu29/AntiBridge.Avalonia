const config = require('./config');

const LEVELS = { debug: 0, info: 1, warn: 2, error: 3 };
const currentLevel = LEVELS[config.logLevel] ?? LEVELS.info;

function timestamp() {
    return new Date().toISOString().replace('T', ' ').substring(0, 23);
}

function formatArgs(args) {
    return args.map(a => {
        if (typeof a === 'object' && a !== null) {
            try { return JSON.stringify(a, null, 2); } catch { return String(a); }
        }
        return String(a);
    }).join(' ');
}

const logger = {
    debug(...args) {
        if (currentLevel <= LEVELS.debug) {
            console.log(`${timestamp()} [DEBUG] ${formatArgs(args)}`);
        }
    },

    info(...args) {
        if (currentLevel <= LEVELS.info) {
            console.log(`${timestamp()} [INFO]  ${formatArgs(args)}`);
        }
    },

    warn(...args) {
        if (currentLevel <= LEVELS.warn) {
            console.warn(`${timestamp()} [WARN]  ${formatArgs(args)}`);
        }
    },

    error(...args) {
        if (currentLevel <= LEVELS.error) {
            console.error(`${timestamp()} [ERROR] ${formatArgs(args)}`);
        }
    },
};

module.exports = logger;
