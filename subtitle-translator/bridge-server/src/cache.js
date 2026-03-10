/**
 * Cache - File-based cache cho translations
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const config = require('./config');
const logger = require('./logger');

const cacheDir = path.resolve(config.cacheDir);

/**
 * Đảm bảo thư mục cache tồn tại
 */
function ensureCacheDir() {
    if (!fs.existsSync(cacheDir)) {
        fs.mkdirSync(cacheDir, { recursive: true });
        logger.info(`[CACHE] Tạo thư mục cache: ${cacheDir}`);
    }
}

/**
 * Tạo cache key từ VTT content + language
 */
function getCacheKey(vttContent, lang = 'vi') {
    const hash = crypto.createHash('sha256')
        .update(vttContent + '::' + lang)
        .digest('hex')
        .substring(0, 16);
    return hash;
}

/**
 * Lấy bản dịch từ cache
 * @returns {{ translatedVtt: string, timestamp: string } | null}
 */
function get(vttContent, lang = 'vi') {
    ensureCacheDir();

    const key = getCacheKey(vttContent, lang);
    const filePath = path.join(cacheDir, `${key}.json`);

    if (!fs.existsSync(filePath)) {
        logger.debug(`[CACHE] MISS: ${key}`);
        return null;
    }

    try {
        const data = JSON.parse(fs.readFileSync(filePath, 'utf8'));
        logger.info(`[CACHE] HIT: ${key} (${data.translatedVtt.length} chars, cached ${data.timestamp})`);
        return data;
    } catch (err) {
        logger.warn(`[CACHE] Lỗi đọc cache ${key}: ${err.message}`);
        return null;
    }
}

/**
 * Lưu bản dịch vào cache
 */
function set(vttContent, lang, translatedVtt) {
    ensureCacheDir();

    const key = getCacheKey(vttContent, lang);
    const filePath = path.join(cacheDir, `${key}.json`);

    const data = {
        lang,
        originalLength: vttContent.length,
        translatedVtt,
        timestamp: new Date().toISOString(),
    };

    try {
        fs.writeFileSync(filePath, JSON.stringify(data, null, 2), 'utf8');
        logger.info(`[CACHE] SAVED: ${key} (${translatedVtt.length} chars)`);
    } catch (err) {
        logger.error(`[CACHE] Lỗi lưu cache ${key}: ${err.message}`);
    }
}

module.exports = { get, set, getCacheKey };
