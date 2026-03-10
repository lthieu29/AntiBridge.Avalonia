/**
 * Antigravity Bridge - CDP Connection Module
 * Kết nối và giao tiếp với Antigravity IDE qua Chrome DevTools Protocol
 * 
 * Adapted from telegram-antigravity-bot với:
 * - Comprehensive logging tại mọi bước
 * - iframe-first DOM traversal
 * - Tăng timeout cho translation tasks
 */

const puppeteer = require('puppeteer-core');
const fs = require('fs');
const path = require('path');
const config = require('./config');
const logger = require('./logger');

class AntigravityBridge {
    constructor() {
        this.browser = null;
        this.page = null;       // workbench page (chính)
        this.chatPage = null;   // cascade-panel webview page
        this.isConnected = false;
        this.lastResponseText = '';

        // Cancellation support
        this.isCancelled = false;

        // Auto-reconnect
        this.autoReconnect = true;
        this.reconnectAttempts = 0;
        this.maxReconnectAttempts = 3;
        this.reconnectDelay = 2000;

        // Đếm null responses liên tiếp cho auto DOM dump
        this.consecutiveNulls = 0;
        this.NULL_DUMP_THRESHOLD = 10;

        // ========== NOISE PATTERNS ==========
        this.NOISE_PATTERNS = [
            // Model names
            /^GPT-?OS{1,2}\s+\d+\w*\s*\([^)]+\)\s*$/i,
            /^Claude\s+\d+(\.\d+)?\s*\w*\s*(\([^)]+\))?\s*$/i,
            /^Gemini\s+\d+(\.\d+)?\s*\w*\s*(\([^)]+\))?\s*$/i,
            /^Llama\s+\d+(\.\d+)?\s*\w*\s*$/i,
            /^GPT-?4[ov]?\s*(-turbo|-mini)?\s*$/i,
            /^o[123]-?(mini|preview)?\s*$/i,
            /^Anthropic\s+/i,
            /^Mistral\s+/i,
            /^DeepSeek\s+/i,
            /Claude Opus/i,
            /Claude Sonnet/i,
            /Gemini \d+ Pro/i,

            // UI Labels
            /^AI may make mistakes/i,
            /^Double-check all generated code/i,
            /^Agent will execute tasks directly/i,
            /^Agent can plan before executing/i,
            /^Use for (simple|deep|complex)/i,
            /^Conversation mode$/i,
            /^Ask anything/i,
            /^Ctrl\+[A-Z]/i,
            /^@ to mention/i,
            /^\/ for workflows$/i,

            // Model selector dropdown
            /Add\s*context/i,
            /^Images$/i,
            /^Mentions$/i,
            /^Workflows$/i,
            /^Planning$/i,
            /^Fast$/i,
            /^Model$/i,
            /^New$/i,
            /^Claude.*\(Thinking\)\s*$/i,
            /^Claude Sonnet[\s\d.]*$/i,
            /^Claude Opus[\s\d.]*$/i,
            /^Gemini\s*\d+[\s\w()]*$/i,
            /^GPT-OSS[\s\d\w()]*$/i,
            /^\s*\(High\)\s*$|^\s*\(Low\)\s*$|^\s*\(Medium\)\s*$/i,
            /Nhập lệnh cho AI agent/i,

            // File paths
            /^[a-zA-Z]:\\[^<>:"|?*]+$/,
            /^\/[^<>:"|?*]+$/,

            // Folder segments
            /^\.agent\\?$/i,
            /^\\+$/,
            /^workflows?$/i,
            /^scripts?$/i,
            /^backend$/i,
            /^frontend$/i,
            /^node_modules$/i,
            /^[a-zA-Z0-9_-]+\\$/,

            // UI elements
            /^(Accept|Reject|Cancel|Submit|Send|Gửi|Hủy|Copy|Edit|Delete)$/i,
            /^(Yes|No|OK|Done|Close|Đóng|Xác nhận)$/i,
            /^\d+\s*(tokens?|words?|chars?)\s*$/i,
            /^Model:?\s*$/i,
            /^Response:?\s*$/i,
            /^Thinking\.{0,3}$/i,
            /^Loading\.{0,3}$/i,
            /^Generating\.{0,3}$/i,
            /^Thinking for \d+s$/i,
            /^Progress Updates$/i,
            /^Show items analyzed$/i,
            /^\d+ Files With Changes$/i,
            /^Error while editing$/i,
            /^Auto-proceeded by/i,
        ];

        this.MIN_RESPONSE_LENGTH = 50;
    }

    /**
     * Kiểm tra text có phải noise không
     */
    isNoise(text) {
        if (!text || text.length < this.MIN_RESPONSE_LENGTH) {
            return { isNoise: true, reason: `Too short (${text?.length || 0} chars)` };
        }

        const trimmed = text.trim();

        for (const pattern of this.NOISE_PATTERNS) {
            if (pattern.test(trimmed)) {
                return { isNoise: true, reason: `Matched pattern: ${pattern.toString()}` };
            }
        }

        // Model name starts
        const modelStarts = ['Claude', 'Gemini', 'GPT', 'Llama', 'Mistral', 'DeepSeek'];
        for (const model of modelStarts) {
            if (trimmed.startsWith(model) && trimmed.length < 100) {
                return { isNoise: true, reason: `Starts with model name: ${model}` };
            }
        }

        // Mostly model keywords
        const modelKeywords = ['Claude', 'Gemini', 'GPT', 'Opus', 'Sonnet', 'Pro', 'Flash', 'Thinking', 'High', 'Medium', 'Low'];
        let modelCount = 0;
        for (const kw of modelKeywords) {
            if (trimmed.includes(kw)) modelCount++;
        }
        if (modelCount >= 3 && trimmed.length < 300) {
            return { isNoise: true, reason: `Too many model keywords (${modelCount})` };
        }

        return { isNoise: false, reason: null };
    }

    /**
     * Làm sạch text response
     */
    cleanResponse(text) {
        if (!text) return '';

        let cleaned = text;

        const prefixPatterns = [
            /^(Claude|Gemini|GPT|Anthropic|DeepSeek|Mistral)[^\n]*\n+/i,
            /^(Thinking|Loading|Generating)\.{0,3}\s*\n*/i,
        ];

        for (const pattern of prefixPatterns) {
            cleaned = cleaned.replace(pattern, '');
        }

        const uiPrefixes = [
            /^Add context\s*\n*/i,
            /^Images\s+Mentions\s+Workflows\s*/i,
            /^Planning\s+Fast\s+Model\s*/i,
        ];

        for (const pattern of uiPrefixes) {
            cleaned = cleaned.replace(pattern, '');
        }

        return cleaned.trim();
    }

    /**
     * Kết nối đến Antigravity qua CDP
     * Sử dụng targetFilter để bao gồm TẤT CẢ target types (bao gồm webview)
     */
    async connect() {
        if (this.isConnected) return true;

        logger.info(`[CDP] Đang kết nối tới ${config.cdpUrl}...`);

        try {
            // === BƯỚC 1: Log tất cả CDP targets (debug) ===
            const targetsRes = await fetch(`${config.cdpUrl}/json`);
            const targets = await targetsRes.json();
            logger.info(`[CDP] Tìm thấy ${targets.length} CDP targets:`);
            for (let i = 0; i < targets.length; i++) {
                const t = targets[i];
                logger.info(`[CDP]   Target ${i}: type="${t.type}" title="${(t.title || '').substring(0, 60)}" url="${(t.url || '').substring(0, 120)}"`);
            }

            // === BƯỚC 2: Connect browser-level, bao gồm TẤT CẢ target types ===
            const versionRes = await fetch(`${config.cdpUrl}/json/version`);
            const versionData = await versionRes.json();
            const wsEndpoint = versionData.webSocketDebuggerUrl;
            logger.info(`[CDP] Browser WS: ${wsEndpoint}`);

            this.browser = await puppeteer.connect({
                browserWSEndpoint: wsEndpoint,
                defaultViewport: null,
                // Bao gồm TẤT CẢ target types (page, webview, worker, other, ...)
                targetFilter: (target) => true
            });

            // === BƯỚC 3: Tìm tất cả pages (bao gồm webview targets) ===
            const allTargets = this.browser.targets();
            logger.info(`[CDP] Browser targets sau connect: ${allTargets.length}`);
            for (let i = 0; i < allTargets.length; i++) {
                const t = allTargets[i];
                logger.info(`[CDP]   BrowserTarget ${i}: type="${t.type()}" url="${(t.url() || '').substring(0, 120)}"`);
            }

            const pages = await this.browser.pages();
            logger.info(`[CDP] Pages: ${pages.length}`);

            for (let i = 0; i < pages.length; i++) {
                const url = pages[i].url();
                const title = await pages[i].title().catch(() => '');
                logger.info(`[CDP]   Page ${i}: title="${title}" url="${url.substring(0, 120)}"`);

                if (url.includes('cascade-panel')) {
                    this.chatPage = pages[i];
                    logger.info(`[CDP] ✅ Tìm thấy chatPage (cascade-panel) tại index ${i}`);
                }
                if (url.includes('workbench')) {
                    this.page = pages[i];
                }
            }

            // === BƯỚC 4: Nếu chưa có chatPage, thử attach vào webview target ===
            if (!this.chatPage) {
                logger.warn('[CDP] chatPage chưa tìm thấy qua pages(), thử attach webview target...');
                for (const target of allTargets) {
                    const url = target.url() || '';
                    if (url.includes('cascade-panel')) {
                        try {
                            const targetPage = await target.page();
                            if (targetPage) {
                                this.chatPage = targetPage;
                                logger.info(`[CDP] ✅ Attach chatPage từ target: ${url.substring(0, 100)}`);
                                break;
                            }
                        } catch (e) {
                            logger.debug(`[CDP] Không thể attach target: ${e.message}`);
                        }
                    }
                }
            }

            if (!this.chatPage) {
                logger.warn('[CDP] ⚠️ Không tìm thấy cascade-panel! Chat panel có đang mở không?');
            }

            // Fallback page
            if (!this.page && pages.length > 0) {
                this.page = pages[0];
            }

            this.browser.on('disconnected', () => {
                logger.warn('[CDP] ❌ Mất kết nối browser!');
                this.isConnected = false;
                this.chatPage = null;
                if (this.autoReconnect) {
                    this.tryReconnect();
                }
            });

            if (this.chatPage || this.page) {
                this.isConnected = true;
                this.reconnectAttempts = 0;
                logger.info(`[CDP] ✅ Kết nối thành công! chatPage=${!!this.chatPage}, workbenchPage=${!!this.page}`);
                return true;
            } else {
                logger.error('[CDP] ❌ Không tìm thấy page nào phù hợp');
                throw new Error('No Antigravity page found');
            }

        } catch (err) {
            logger.error('[CDP] ❌ Lỗi kết nối:', err.message);
            this.isConnected = false;
            return false;
        }
    }

    /**
     * Thử kết nối lại
     */
    async tryReconnect() {
        if (this.reconnectAttempts >= this.maxReconnectAttempts) {
            logger.error(`[CDP] Đã thử ${this.maxReconnectAttempts} lần, dừng reconnect`);
            return false;
        }

        this.reconnectAttempts++;
        logger.info(`[CDP] Reconnect ${this.reconnectAttempts}/${this.maxReconnectAttempts}...`);

        await new Promise(r => setTimeout(r, this.reconnectDelay));

        this.browser = null;
        this.page = null;
        this.chatPage = null;

        return await this.connect();
    }

    cancel() {
        this.isCancelled = true;
        logger.info('[CANCEL] Đã yêu cầu hủy operation');
    }

    resetCancel() {
        this.isCancelled = false;
    }

    /**
     * Tìm chat panel page/frame
     * Ưu tiên dùng chatPage (CDP target trực tiếp), fallback tìm frame trong workbench
     */
    async findChatFrame() {
        // === Ưu tiên 1: chatPage (direct CDP target) ===
        if (this.chatPage) {
            // chatPage là 1 Page object, mainFrame() trả về Frame interface tương thích
            logger.debug('[FRAME] Dùng chatPage (CDP target trực tiếp)');
            return this.chatPage.mainFrame();
        }

        // === Fallback: Tìm trong frames của workbench page ===
        if (!this.page) {
            logger.error('[FRAME] Không có page nào!');
            return null;
        }

        const frames = this.page.frames();
        logger.info(`[FRAME] Fallback: tìm trong ${frames.length} frames của workbench`);

        for (let i = 0; i < frames.length; i++) {
            const frameUrl = frames[i].url();
            logger.debug(`[FRAME] Frame ${i}: url="${(frameUrl || '').substring(0, 100)}"`);

            if (frameUrl && frameUrl.includes('cascade-panel')) {
                logger.info(`[FRAME] ✅ Tìm thấy cascade-panel frame tại index ${i}`);
                return frames[i];
            }
        }

        // === Fallback 2: Thử re-discover cascade-panel target qua browser.targets() ===
        logger.warn('[FRAME] ❌ Không tìm thấy cascade-panel. Thử re-discover...');
        try {
            if (this.browser) {
                const allTargets = this.browser.targets();
                for (const target of allTargets) {
                    const url = target.url() || '';
                    if (url.includes('cascade-panel')) {
                        const targetPage = await target.page();
                        if (targetPage) {
                            this.chatPage = targetPage;
                            logger.info(`[FRAME] ✅ Re-discovered chatPage: ${url.substring(0, 100)}`);
                            return this.chatPage.mainFrame();
                        }
                    }
                }
            }

            // Log tất cả targets từ /json để debug
            const targetsRes = await fetch(`${config.cdpUrl}/json`);
            const targets = await targetsRes.json();
            logger.warn('[FRAME] cascade-panel target không tìm thấy');
            logger.warn('[FRAME] 📋 Targets hiện có:');
            for (const t of targets) {
                logger.warn(`[FRAME]   type=${t.type} url=${(t.url || '').substring(0, 120)}`);
            }
        } catch (e) {
            logger.error(`[FRAME] Re-discover error: ${e.message}`);
        }

        return null;
    }

    /**
     * Gửi tin nhắn đến Antigravity chat
     * LOGGING: Bước 3 — chi tiết input discovery + injection
     */
    async sendMessage(text) {
        if (!this.isConnected) {
            const connected = await this.connect();
            if (!connected) return false;
        }

        // Sanitize newlines (prompt dùng numbered list format)
        const sanitizedText = text.replace(/\n+/g, ' ').trim();

        logger.info(`[INPUT] Gửi message: "${sanitizedText.substring(0, 80)}..." (${sanitizedText.length} chars)`);

        try {
            const chatFrame = await this.findChatFrame();
            if (!chatFrame) return false;

            try {
                // Thử từng selector
                const selectors = ['textarea', '[contenteditable="true"]', '[role="textbox"]'];
                let input = null;

                for (const sel of selectors) {
                    const found = await chatFrame.$(sel);
                    logger.info(`[INPUT] Selector thử: "${sel}" → ${found ? '✅ có' : '❌ không'}`);
                    if (found && !input) {
                        input = found;
                    }
                }

                if (!input) {
                    logger.error('[INPUT] ❌ Không tìm thấy input element nào trong cascade-panel');
                    // Dump top elements for debugging
                    try {
                        const dump = await chatFrame.evaluate(() => {
                            const els = [];
                            document.querySelectorAll('*').forEach(el => {
                                if (el.tagName && el.className) {
                                    els.push(`${el.tagName}.${(el.className.toString() || '').substring(0, 60)}`);
                                }
                            });
                            return els.slice(0, 20);
                        });
                        logger.error(`[INPUT] 📋 Top 20 elements: ${dump.join(', ')}`);
                    } catch (e) { }
                    return false;
                }

                const inputInfo = await chatFrame.evaluate(el => ({
                    tag: el.tagName,
                    id: el.id,
                    className: (el.className?.toString?.() || '').substring(0, 80)
                }), input);
                logger.info(`[INPUT] ✅ Input: tag=${inputInfo.tag}, id=${inputInfo.id || 'none'}, class="${inputInfo.className}"`);

                // Focus + click
                await input.focus();
                await input.click();
                logger.debug('[INPUT] Focus + click → OK');

                // Type in chunks
                if (sanitizedText.length <= 100) {
                    await input.type(sanitizedText, { delay: 0 });
                } else {
                    const chunks = sanitizedText.match(/.{1,50}/g) || [];
                    logger.debug(`[INPUT] Đang type ${sanitizedText.length} chars (${chunks.length} chunks)...`);
                    for (const chunk of chunks) {
                        await input.type(chunk, { delay: 0 });
                    }
                }

                logger.debug('[INPUT] ✅ Type xong, đang press Enter...');
                await new Promise(r => setTimeout(r, 200));
                await chatFrame.page().keyboard.press('Enter');

                logger.info('[INPUT] ✅ Message đã gửi thành công');

                this.lastResponseText = await this.getLatestResponse();
                return true;

            } catch (frameErr) {
                logger.error(`[INPUT] Frame error: ${frameErr.message}`);
                return false;
            }

        } catch (err) {
            logger.error('[INPUT] Lỗi gửi message:', err.message);
            return false;
        }
    }

    /**
     * Lấy response mới nhất từ AI
     * LOGGING: Bước 4 — chi tiết DOM traversal với iframe-first
     */
    async getLatestResponse() {
        if (!this.page) {
            logger.debug('[RESPONSE] No page');
            return null;
        }

        try {
            const chatFrame = await this.findChatFrame();
            if (!chatFrame) {
                this.consecutiveNulls++;
                return null;
            }

            try {
                const result = await chatFrame.evaluate(() => {
                    const debugLog = [];
                    const candidates = [];

                    // === IFRAME-FIRST: Kiểm tra nested iframe ===
                    let targetDoc = document;
                    const iframe = document.querySelector('iframe');
                    if (iframe) {
                        try {
                            const iframeDoc = iframe.contentDocument || iframe.contentWindow?.document;
                            if (iframeDoc) {
                                targetDoc = iframeDoc;
                                debugLog.push('[RESPONSE] ✅ Tìm thấy nested iframe, dùng iframe document');
                            }
                        } catch (e) {
                            debugLog.push(`[RESPONSE] ⚠️ Iframe cross-origin: ${e.message}, dùng document gốc`);
                        }
                    } else {
                        debugLog.push('[RESPONSE] ⚠️ Không có nested iframe, dùng document gốc');
                    }

                    // Helper: HTML to text
                    function htmlToText(el) {
                        let text = '';
                        function processNode(node) {
                            if (node.nodeType === Node.TEXT_NODE) {
                                text += node.textContent;
                            } else if (node.nodeType === Node.ELEMENT_NODE) {
                                const tag = node.tagName.toLowerCase();
                                if (tag === 'li') {
                                    const parent = node.parentElement?.tagName?.toLowerCase();
                                    if (parent === 'ol') {
                                        const items = Array.from(node.parentElement.children);
                                        const idx = items.indexOf(node) + 1;
                                        text += `\n${idx}. `;
                                    } else {
                                        text += '\n• ';
                                    }
                                }
                                if (['p', 'div', 'br', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'].includes(tag)) {
                                    if (text && !text.endsWith('\n')) text += '\n';
                                }
                                for (const child of node.childNodes) {
                                    processNode(child);
                                }
                                if (['p', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6'].includes(tag)) {
                                    if (!text.endsWith('\n')) text += '\n';
                                }
                            }
                        }
                        processNode(el);
                        return text.trim().replace(/\n{3,}/g, '\n\n');
                    }

                    // Tìm prose elements trong targetDoc
                    const proseElements = targetDoc.querySelectorAll('[class*="prose"]');
                    debugLog.push(`[RESPONSE] targetDoc.querySelectorAll('[class*="prose"]'): ${proseElements.length} elements`);

                    proseElements.forEach((el, i) => {
                        const className = el.className?.toString?.() || '';
                        const parentClass = el.parentElement?.className?.toString?.() || '';

                        const hasOpacity70 = className.includes('opacity-70');
                        const hasMaxH200 = parentClass.includes('max-h-[200px]') || parentClass.includes('overflow-y-auto');
                        const hasInput = className.includes('input') || className.includes('menu');

                        debugLog.push(`[RESPONSE] Element ${i}: class="${className.substring(0, 80)}" parentClass="${parentClass.substring(0, 80)}" opacity70=${hasOpacity70} maxH200=${hasMaxH200}`);

                        if (hasOpacity70) {
                            debugLog.push(`[RESPONSE]   → BỎ QUA (thinking: opacity-70)`);
                            return;
                        }
                        if (hasMaxH200) {
                            debugLog.push(`[RESPONSE]   → BỎ QUA (collapsed thinking: maxH200)`);
                            return;
                        }
                        if (hasInput) {
                            debugLog.push(`[RESPONSE]   → BỎ QUA (input/menu element)`);
                            return;
                        }

                        const text = htmlToText(el);
                        debugLog.push(`[RESPONSE]   textLength=${text.length}, preview="${text.substring(0, 100).replace(/\n/g, '\\n')}"`);

                        if (text.length < 50) {
                            debugLog.push(`[RESPONSE]   → BỎ QUA (quá ngắn)`);
                            return;
                        }

                        if (/^(I'm currently|I've|My plan|Initiating|Verifying|Considering|Examining|Analyzing|Acknowledging|Assessing)/i.test(text)) {
                            debugLog.push(`[RESPONSE]   → BỎ QUA (thinking pattern)`);
                            return;
                        }

                        candidates.push({
                            text,
                            len: text.length,
                            className: className.substring(0, 100),
                            hasVietnamese: /[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]/i.test(text),
                            hasNumberedList: /^\d+\.\s+/m.test(text)
                        });
                    });

                    // Sort: numbered list > Vietnamese > longest
                    candidates.sort((a, b) => {
                        if (a.hasNumberedList !== b.hasNumberedList) return a.hasNumberedList ? -1 : 1;
                        if (a.hasVietnamese !== b.hasVietnamese) return a.hasVietnamese ? -1 : 1;
                        return b.len - a.len;
                    });

                    debugLog.push(`[RESPONSE] Candidates sau filter: ${candidates.length}`);
                    if (candidates[0]) {
                        debugLog.push(`[RESPONSE] Best: len=${candidates[0].len}, hasVI=${candidates[0].hasVietnamese}, hasNumbered=${candidates[0].hasNumberedList}`);
                    }

                    return {
                        text: candidates.length > 0 ? candidates[0].text : null,
                        debugLog,
                        candidateCount: candidates.length,
                    };
                });

                // Log tất cả debug info từ evaluate
                for (const line of result.debugLog) {
                    logger.debug(line);
                }

                if (result.text && result.text.length > 50) {
                    const noiseCheck = this.isNoise(result.text);
                    if (!noiseCheck.isNoise) {
                        this.consecutiveNulls = 0;
                        return this.cleanResponse(result.text);
                    } else {
                        logger.debug(`[RESPONSE] Noise detected: ${noiseCheck.reason}`);
                    }
                }

            } catch (e) {
                logger.debug(`[RESPONSE] Frame eval error: ${e.message}`);
            }

        } catch (err) {
            logger.error('[RESPONSE] Lỗi:', err.message);
        }

        this.consecutiveNulls++;

        // Auto DOM dump khi null quá nhiều lần
        if (this.consecutiveNulls >= this.NULL_DUMP_THRESHOLD) {
            this.consecutiveNulls = 0;
            await this.dumpDOM();
        }

        return null;
    }

    /**
     * Chờ response mới từ AI
     * LOGGING: progress + completion detection
     */
    async waitForResponse(timeout = config.responseTimeout) {
        logger.info(`[RESPONSE] Bắt đầu poll (timeout=${timeout / 1000}s, interval=${config.pollInterval}ms)`);

        const startTime = Date.now();
        let lastContent = this.lastResponseText;
        let lastLength = 0;
        let stableCount = 0;
        let slowCount = 0;
        let pollCount = 0;

        const STABLE_THRESHOLD = 5;
        const SLOW_THRESHOLD = 3;
        const SLOW_GROWTH = 50;

        while (Date.now() - startTime < timeout) {
            if (this.isCancelled) {
                logger.info('[RESPONSE] Đã hủy bởi user');
                return null;
            }

            await new Promise(r => setTimeout(r, config.pollInterval));
            pollCount++;

            const currentContent = await this.getLatestResponse();

            if (!currentContent) continue;

            const noiseCheck = this.isNoise(currentContent);
            if (noiseCheck.isNoise) {
                logger.debug(`[RESPONSE] Poll #${pollCount}: bỏ qua noise — ${noiseCheck.reason}`);
                continue;
            }

            const currentLength = currentContent.length;
            const growth = currentLength - lastLength;

            if (currentContent !== lastContent && currentContent !== this.lastResponseText) {
                lastContent = currentContent;
                stableCount = 0;

                if (growth > 0 && growth < SLOW_GROWTH && currentLength > 500) {
                    slowCount++;
                    logger.debug(`[RESPONSE] Poll #${pollCount}: slow growth +${growth} chars (${slowCount}/${SLOW_THRESHOLD}), total=${currentLength}`);

                    if (slowCount >= SLOW_THRESHOLD) {
                        const elapsed = Date.now() - startTime;
                        logger.info(`[RESPONSE] ✅ Hoàn tất (slow growth) — ${currentLength} chars, ${pollCount} polls, ${elapsed}ms`);
                        this.lastResponseText = currentContent;
                        return this.cleanResponse(currentContent);
                    }
                } else {
                    slowCount = 0;
                    logger.debug(`[RESPONSE] Poll #${pollCount}: content thay đổi +${growth} chars, total=${currentLength}`);
                }

                lastLength = currentLength;
            } else if (currentLength > 30 && currentContent !== this.lastResponseText) {
                stableCount++;
                logger.debug(`[RESPONSE] Poll #${pollCount}: stable ${stableCount}/${STABLE_THRESHOLD}, len=${currentLength}`);

                if (stableCount >= STABLE_THRESHOLD) {
                    const elapsed = Date.now() - startTime;
                    logger.info(`[RESPONSE] ✅ Hoàn tất (stable) — ${currentLength} chars, ${pollCount} polls, ${elapsed}ms`);
                    this.lastResponseText = currentContent;
                    return this.cleanResponse(currentContent);
                }
            }
        }

        // Timeout
        const elapsed = Date.now() - startTime;
        logger.warn(`[RESPONSE] ⏰ Timeout sau ${elapsed}ms (${pollCount} polls)`);
        if (lastContent && lastContent.length > 100) {
            logger.info(`[RESPONSE] Trả về partial response (${lastContent.length} chars)`);
            this.lastResponseText = lastContent;
            return this.cleanResponse(lastContent);
        }
        return this.cleanResponse(lastContent || 'Không nhận được phản hồi từ AI.');
    }

    /**
     * Chờ response với streaming callback
     */
    async waitForResponseStreaming(onChunk, timeout = config.responseTimeout) {
        logger.info(`[STREAM] Bắt đầu streaming poll (timeout=${timeout / 1000}s)`);

        const startTime = Date.now();
        let lastContent = this.lastResponseText;
        let lastLength = 0;
        let stableCount = 0;
        let slowCount = 0;
        let lastEmitLength = 0;

        const STABLE_THRESHOLD = 5;
        const SLOW_THRESHOLD = 3;
        const SLOW_GROWTH = 50;

        while (Date.now() - startTime < timeout) {
            if (this.isCancelled) {
                logger.info('[STREAM] Đã hủy');
                return null;
            }

            await new Promise(r => setTimeout(r, config.pollInterval));

            const currentContent = await this.getLatestResponse();
            if (!currentContent) continue;

            const noiseCheck = this.isNoise(currentContent);
            if (noiseCheck.isNoise) continue;

            const currentLength = currentContent.length;
            const growth = currentLength - lastLength;

            if (currentContent !== lastContent && currentContent !== this.lastResponseText) {
                lastContent = currentContent;
                stableCount = 0;

                if (currentLength > lastEmitLength + 20) {
                    try {
                        await onChunk(this.cleanResponse(currentContent), false);
                        lastEmitLength = currentLength;
                    } catch (e) {
                        logger.error('[STREAM] Callback error:', e.message);
                    }
                }

                if (growth > 0 && growth < SLOW_GROWTH && currentLength > 500) {
                    slowCount++;
                    if (slowCount >= SLOW_THRESHOLD) {
                        this.lastResponseText = currentContent;
                        const finalContent = this.cleanResponse(currentContent);
                        try { await onChunk(finalContent, true); } catch (e) { }
                        logger.info(`[STREAM] ✅ Hoàn tất (slow growth) — ${currentLength} chars`);
                        return finalContent;
                    }
                } else {
                    slowCount = 0;
                }

                lastLength = currentLength;
            } else if (currentLength > 30 && currentContent !== this.lastResponseText) {
                stableCount++;
                if (stableCount >= STABLE_THRESHOLD) {
                    this.lastResponseText = currentContent;
                    const finalContent = this.cleanResponse(currentContent);
                    try { await onChunk(finalContent, true); } catch (e) { }
                    logger.info(`[STREAM] ✅ Hoàn tất (stable) — ${currentLength} chars`);
                    return finalContent;
                }
            }
        }

        logger.warn('[STREAM] ⏰ Timeout');
        const result = this.cleanResponse(lastContent || 'Không nhận được phản hồi từ AI.');
        try { await onChunk(result, true); } catch (e) { }
        return result;
    }

    /**
     * Auto DOM dump — Bước 5
     * Xuất toàn bộ DOM structure ra file khi không tìm thấy response
     */
    async dumpDOM() {
        logger.warn('[DOM-DUMP] Không tìm thấy response sau nhiều lần poll, đang dump DOM...');

        try {
            const frames = this.page.frames();
            let dump = `=== DOM DUMP at ${new Date().toISOString()} ===\n`;
            dump += `Total frames: ${frames.length}\n\n`;

            for (let i = 0; i < frames.length; i++) {
                const frameUrl = frames[i].url();
                dump += `\n${'='.repeat(80)}\nFRAME ${i}: ${frameUrl || '(empty)'}\n${'='.repeat(80)}\n\n`;

                if (!frameUrl || frameUrl === 'about:blank') {
                    dump += '(empty frame - skipped)\n';
                    continue;
                }

                try {
                    const domInfo = await frames[i].evaluate(() => {
                        const elements = [];
                        const seenTexts = new Set();

                        // Kiểm tra iframe nội bộ
                        let targetDoc = document;
                        const iframe = document.querySelector('iframe');
                        if (iframe) {
                            try {
                                const iframeDoc = iframe.contentDocument || iframe.contentWindow?.document;
                                if (iframeDoc) targetDoc = iframeDoc;
                            } catch (e) { }
                        }

                        const selectors = [
                            '[class*="prose"]', '[class*="message"]', '[class*="response"]',
                            '[class*="content"]', '[class*="chat"]', '[class*="assistant"]',
                            '[class*="turn-"]', 'article', '[data-role]',
                            'textarea', '[contenteditable]', '[role="textbox"]',
                        ];

                        for (const selector of selectors) {
                            try {
                                targetDoc.querySelectorAll(selector).forEach(el => {
                                    const text = (el.innerText || '').trim();
                                    const key = text.substring(0, 80);
                                    if (seenTexts.has(key)) return;
                                    seenTexts.add(key);

                                    let parentChain = [];
                                    let parent = el.parentElement;
                                    for (let j = 0; j < 3 && parent; j++) {
                                        parentChain.push(`${parent.tagName}.${(parent.className?.toString?.() || '').substring(0, 60)}`);
                                        parent = parent.parentElement;
                                    }

                                    elements.push({
                                        selector,
                                        tag: el.tagName,
                                        class: (el.className?.toString?.() || '').substring(0, 100),
                                        id: el.id || '',
                                        textLen: text.length,
                                        textPreview: text.substring(0, 200),
                                        parentChain,
                                    });
                                });
                            } catch (e) { }
                        }
                        return { elements, hasIframe: !!iframe, bodyTextLen: (targetDoc.body?.innerText || '').length };
                    });

                    dump += `Has nested iframe: ${domInfo.hasIframe}\n`;
                    dump += `Body text length: ${domInfo.bodyTextLen}\n`;
                    dump += `Found ${domInfo.elements.length} elements:\n\n`;

                    for (const el of domInfo.elements) {
                        dump += `─ ${el.selector} → <${el.tag}> class="${el.class}" id="${el.id}" textLen=${el.textLen}\n`;
                        dump += `  Parents: ${el.parentChain.join(' → ')}\n`;
                        if (el.textLen > 0) {
                            dump += `  Text: "${el.textPreview}"\n`;
                        }
                        dump += '\n';
                    }
                } catch (e) {
                    dump += `Error: ${e.message}\n`;
                }
            }

            // Lưu file
            const logsDir = path.join(__dirname, '..', 'logs');
            if (!fs.existsSync(logsDir)) {
                fs.mkdirSync(logsDir, { recursive: true });
            }
            const timestamp = new Date().toISOString().replace(/[:.]/g, '-');
            const dumpFile = path.join(logsDir, `dom-debug-${timestamp}.txt`);
            fs.writeFileSync(dumpFile, dump);
            logger.warn(`[DOM-DUMP] ✅ Đã lưu tại: ${dumpFile} (${dump.length} bytes)`);

        } catch (err) {
            logger.error(`[DOM-DUMP] Lỗi: ${err.message}`);
        }
    }

    /**
     * Đóng kết nối
     */
    async disconnect() {
        if (this.browser) {
            await this.browser.disconnect().catch(() => { });
            this.browser = null;
            this.page = null;
            this.chatPage = null;
            this.isConnected = false;
            logger.info('[CDP] Đã ngắt kết nối');
        }
    }
}

module.exports = new AntigravityBridge();
