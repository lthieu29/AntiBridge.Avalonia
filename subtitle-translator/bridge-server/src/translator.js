/**
 * Translator - Điều phối quy trình dịch VTT
 */

const EventEmitter = require('events');
const antigravity = require('./antigravity');
const vttParser = require('./vtt-parser');
const promptBuilder = require('./prompt-builder');
const cache = require('./cache');
const config = require('./config');
const logger = require('./logger');

class Translator extends EventEmitter {
    constructor() {
        super();
        this.isTranslating = false;
        this.currentProgress = null;
    }

    /**
     * Dịch toàn bộ VTT content
     * @param {string} vttContent - Nội dung file VTT gốc
     * @param {string} targetLang - Ngôn ngữ đích
     * @returns {Promise<string>} VTT đã dịch
     */
    async translateVtt(vttContent, targetLang = 'tiếng Việt') {
        // 1. Kiểm tra cache
        const langCode = 'vi';
        const cached = cache.get(vttContent, langCode);
        if (cached) {
            logger.info(`[TRANSLATOR] Cache hit! Trả về bản dịch cached`);
            this.emit('complete', { translatedVtt: cached.translatedVtt, fromCache: true });
            return cached.translatedVtt;
        }

        // 2. Parse VTT
        logger.info('[TRANSLATOR] Parsing VTT...');
        const { cues, header } = vttParser.parseVtt(vttContent);
        logger.info(`[TRANSLATOR] Parsed ${cues.length} cues`);

        if (cues.length === 0) {
            throw new Error('Không tìm thấy cue nào trong file VTT');
        }

        // 3. Chunk
        const chunks = vttParser.chunkCues(cues, config.chunkSize);
        logger.info(`[TRANSLATOR] Chia thành ${chunks.length} chunks (${config.chunkSize} cues/chunk)`);

        // 4. Dịch tuần tự
        this.isTranslating = true;
        const allTranslations = new Map();

        try {
            for (let i = 0; i < chunks.length; i++) {
                if (antigravity.isCancelled) {
                    logger.info('[TRANSLATOR] Đã hủy bởi user');
                    this.isTranslating = false;
                    this.emit('cancelled');
                    throw new Error('Translation cancelled');
                }

                const chunk = chunks[i];
                const progress = {
                    chunk: i + 1,
                    total: chunks.length,
                    percent: Math.round(((i + 1) / chunks.length) * 100),
                };
                this.currentProgress = progress;

                logger.info(`[TRANSLATOR] === Chunk ${progress.chunk}/${progress.total} (${progress.percent}%) ===`);
                this.emit('progress', progress);

                // Xây prompt
                const prompt = promptBuilder.buildTranslationPrompt(chunk, targetLang);
                logger.info(`[TRANSLATOR] Prompt length: ${prompt.length} chars`);

                // Gửi tới Antigravity
                const sent = await antigravity.sendMessage(prompt);
                if (!sent) {
                    logger.error(`[TRANSLATOR] Lỗi gửi prompt chunk ${i + 1}`);
                    // Retry 1 lần
                    logger.info('[TRANSLATOR] Thử lại...');
                    const retrySent = await antigravity.sendMessage(prompt);
                    if (!retrySent) {
                        throw new Error(`Failed to send prompt for chunk ${i + 1}`);
                    }
                }

                // Chờ response
                const response = await antigravity.waitForResponse();
                logger.info(`[TRANSLATOR] Response: ${response?.length || 0} chars`);
                logger.debug(`[TRANSLATOR] Response preview: "${(response || '').substring(0, 200)}"`);

                if (!response || response.length < 10) {
                    logger.warn(`[TRANSLATOR] Response quá ngắn/rỗng cho chunk ${i + 1}, bỏ qua`);
                    continue;
                }

                // Parse translations
                const { translations, parsed, missing } = promptBuilder.parseTranslationResponse(response, chunk.length);

                if (parsed === 0) {
                    logger.warn(`[TRANSLATOR] Không parse được translation nào từ response chunk ${i + 1}`);
                    logger.warn(`[TRANSLATOR] Raw response: "${response.substring(0, 500)}"`);
                    continue;
                }

                // Map về cue indexes gốc
                const mappedTranslations = promptBuilder.mapTranslationsToCues(translations, chunk);
                for (const [idx, text] of mappedTranslations) {
                    allTranslations.set(idx, text);
                }

                logger.info(`[TRANSLATOR] Chunk ${i + 1}: ${parsed}/${chunk.length} dịch thành công`);

                // Delay giữa các chunks để tránh overwhelming
                if (i < chunks.length - 1) {
                    await new Promise(r => setTimeout(r, 1000));
                }
            }
        } finally {
            this.isTranslating = false;
            this.currentProgress = null;
        }

        // 5. Tái tạo VTT
        logger.info(`[TRANSLATOR] Tái tạo VTT với ${allTranslations.size}/${cues.length} translations`);
        const translatedVtt = vttParser.reconstructVtt(header, cues, allTranslations);

        // 6. Lưu cache
        cache.set(vttContent, langCode, translatedVtt);

        logger.info(`[TRANSLATOR] ✅ Hoàn tất! ${translatedVtt.length} chars`);
        this.emit('complete', { translatedVtt, fromCache: false });

        return translatedVtt;
    }

    /**
     * Hủy dịch
     */
    cancel() {
        antigravity.cancel();
    }

    /**
     * Reset cancel state
     */
    resetCancel() {
        antigravity.resetCancel();
    }

    /**
     * Lấy trạng thái hiện tại
     */
    getStatus() {
        return {
            connected: antigravity.isConnected,
            translating: this.isTranslating,
            progress: this.currentProgress,
        };
    }
}

module.exports = new Translator();
