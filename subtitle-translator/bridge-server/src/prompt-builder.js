/**
 * Prompt Builder - Xây dựng và parse prompt dịch phụ đề
 */

const logger = require('./logger');

/**
 * Xây dựng prompt dịch cho 1 chunk cue
 * @param {Array<{index: number, text: string}>} cues - Danh sách cue cần dịch
 * @param {string} targetLang - Ngôn ngữ đích (mặc định: tiếng Việt)
 * @returns {string} Prompt text
 */
function buildTranslationPrompt(cues, targetLang = 'tiếng Việt') {
    const numberedLines = cues
        .map((cue, i) => `${i + 1}. ${cue.text}`)
        .join(' ');

    // Prompt ngắn gọn, rõ ràng, một dòng (vì newlines bị sanitize)
    const instruction = `Dịch ${cues.length} phụ đề sau sang ${targetLang}. Trả về đúng format: mỗi dòng bắt đầu bằng số thứ tự và dấu chấm, theo sau là bản dịch. Không thêm giải thích. `;

    return instruction + numberedLines;
}

/**
 * Parse response AI → map translations
 * @param {string} response - Response text từ AI
 * @param {number} expectedCount - Số cue mong đợi
 * @returns {{ translations: Map<number, string>, parsed: number, missing: number[] }}
 */
function parseTranslationResponse(response, expectedCount) {
    const translations = new Map();
    const missing = [];

    // Parse numbered lines: "1. Xin chào" hoặc "1) Xin chào"
    const lines = response.split('\n');

    for (const line of lines) {
        const match = line.trim().match(/^(\d+)[.)]\s*(.+)$/);
        if (match) {
            const num = parseInt(match[1]);
            const text = match[2].trim();
            if (num >= 1 && num <= expectedCount && text.length > 0) {
                translations.set(num, text);
            }
        }
    }

    // Tìm các items bị thiếu
    for (let i = 1; i <= expectedCount; i++) {
        if (!translations.has(i)) {
            missing.push(i);
        }
    }

    logger.info(`[PROMPT] Parsed ${translations.size}/${expectedCount} translations, missing: [${missing.join(',')}]`);

    return { translations, parsed: translations.size, missing };
}

/**
 * Map parsed translations về cue indexes gốc
 * @param {Map<number, string>} parsedTranslations - Map 1-based index → text
 * @param {Array<{index: number}>} originalCues - Cue gốc của chunk
 * @returns {Map<number, string>} Map cue.index → translated text
 */
function mapTranslationsToCues(parsedTranslations, originalCues) {
    const result = new Map();

    for (let i = 0; i < originalCues.length; i++) {
        const promptIndex = i + 1; // 1-based trong prompt
        const cueIndex = originalCues[i].index;

        if (parsedTranslations.has(promptIndex)) {
            result.set(cueIndex, parsedTranslations.get(promptIndex));
        }
    }

    return result;
}

module.exports = { buildTranslationPrompt, parseTranslationResponse, mapTranslationsToCues };
