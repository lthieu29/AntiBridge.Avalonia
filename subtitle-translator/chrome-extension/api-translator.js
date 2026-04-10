/**
 * API Translator — Gọi 1min.ai Chat API trực tiếp từ background script
 * Hỗ trợ chunking VTT + streaming response
 */

const ONEMIN_API_URL = 'https://api.1min.ai/api/chat-with-ai';

/**
 * Dịch VTT content qua 1min.ai API (non-streaming per chunk)
 * @param {string} vttContent — Nội dung VTT gốc
 * @param {object} settings — { apiKey, model, chunkSize }
 * @param {function} onProgress — callback(progressObj)
 * @returns {Promise<string>} — VTT đã dịch
 */
async function translateVttViaApi(vttContent, settings, onProgress) {
    const { apiKey, model = 'deepseek-chat', chunkSize = 20 } = settings;

    if (!apiKey) throw new Error('API Key chưa được cấu hình');

    const { cues, header } = parseVttSimple(vttContent);
    if (cues.length === 0) throw new Error('Không tìm thấy cue nào trong VTT');

    const chunks = [];
    for (let i = 0; i < cues.length; i += chunkSize) {
        chunks.push(cues.slice(i, i + chunkSize));
    }

    const TRANSLATE_PROMPT = (count, lines) =>
        `Dịch ${count} phụ đề sau sang tiếng Việt. Giữ nguyên thuật ngữ kỹ thuật tiếng Anh (pod, replicas, API, prompt, AI...). Trả về đúng format: mỗi dòng bắt đầu bằng số thứ tự và dấu chấm, theo sau là bản dịch. Không thêm giải thích. ${lines}`;

    const apiCallFn = (chunk) => {
        const numberedLines = chunk.map((cue, idx) => `${idx + 1}. ${cue.text}`).join(' ');
        return callOneminApi(apiKey, model, TRANSLATE_PROMPT(chunk.length, numberedLines));
    };

    const allTranslations = await processChunksBatched(chunks, 5, apiCallFn, onProgress);
    return reconstructVtt(header, cues, allTranslations);
}

/**
 * Gọi 1min.ai Chat API (non-streaming)
 */
async function callOneminApi(apiKey, model, prompt) {
    const res = await fetch(ONEMIN_API_URL, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'API-KEY': apiKey,
        },
        body: JSON.stringify({
            type: 'UNIFY_CHAT_WITH_AI',
            model,
            promptObject: {
                prompt,
            },
        }),
    });

    if (!res.ok) {
        const errText = await res.text().catch(() => 'Unknown error');
        if (res.status === 401) throw new Error('API Key không hợp lệ');
        if (res.status === 429) throw new Error('Rate limited — chờ vài giây rồi thử lại');
        throw new Error(`API error ${res.status}: ${errText.substring(0, 200)}`);
    }

    const data = await res.json();

    // Extract result text
    const resultObj = data?.aiRecord?.aiRecordDetail?.resultObject;
    if (Array.isArray(resultObj) && resultObj.length > 0) {
        return resultObj.join('\n');
    }

    throw new Error('Response không có resultObject');
}

/**
 * Parse numbered response: "1. Xin chào" → Map(1 → "Xin chào")
 */
function parseNumberedResponse(response, expectedCount) {
    const translations = new Map();

    // Strip <think>...</think> blocks (Qwen3 thinking mode)
    let cleaned = response.replace(/<think>[\s\S]*?<\/think>/gi, '').trim();

    const lines = cleaned.split('\n');

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

    return translations;
}

// ==================== VTT PARSING (simplified for background) ====================

function parseVttSimple(vttText) {
    const lines = vttText.replace(/\r\n/g, '\n').split('\n');
    const cues = [];
    let header = 'WEBVTT';
    let i = 0;
    let cueIndex = 1;

    // Header
    if (lines[0]?.trim().startsWith('WEBVTT')) {
        header = lines[0].trim();
        i = 1;
        while (i < lines.length && lines[i].trim() !== '') i++;
    }

    while (i < lines.length) {
        while (i < lines.length && lines[i].trim() === '') i++;
        if (i >= lines.length) break;

        // Skip NOTE/STYLE
        if (lines[i].trim().startsWith('NOTE') || lines[i].trim().startsWith('STYLE')) {
            while (i < lines.length && lines[i].trim() !== '') i++;
            continue;
        }

        // Skip optional cue number
        if (lines[i].trim().match(/^\d+$/) && i + 1 < lines.length && lines[i + 1].includes('-->')) {
            i++;
        }

        // Timestamp — HH:MM:SS.mmm hoặc MM:SS.mmm
        const match = lines[i]?.trim().match(/^((?:\d{2}:)?\d{2}:\d{2}\.\d{3})\s*-->\s*((?:\d{2}:)?\d{2}:\d{2}\.\d{3})/);
        if (!match) { i++; continue; }

        const startTime = match[1];
        const endTime = match[2];
        i++;

        const textLines = [];
        while (i < lines.length && lines[i].trim() !== '') {
            textLines.push(lines[i].replace(/<[^>]+>/g, '').trim());
            i++;
        }

        if (textLines.length > 0) {
            cues.push({
                index: cueIndex++,
                startTime,
                endTime,
                text: textLines.join(' '),
            });
        }
    }

    return { cues, header };
}

function reconstructVtt(header, cues, translations) {
    let result = header + '\n\n';

    for (const cue of cues) {
        const text = translations.has(cue.index) ? translations.get(cue.index) : cue.text;
        result += `${cue.index}\n`;
        result += `${cue.startTime} --> ${cue.endTime}\n`;
        result += `${text}\n\n`;
    }

    return result.trim() + '\n';
}

// ==================== OPENAI-COMPATIBLE TRANSLATOR ====================

/**
 * Dịch VTT qua OpenAI-compatible API — batch 5 concurrent
 */
async function translateVttViaOpenAi(vttContent, settings, onProgress) {
    const { openaiUrl, openaiKey, chunkSize = 20 } = settings;

    if (!openaiUrl) throw new Error('OpenAI URL chưa được cấu hình');

    const { cues, header } = parseVttSimple(vttContent);
    if (cues.length === 0) throw new Error('Không tìm thấy cue nào trong VTT');

    const chunks = [];
    for (let i = 0; i < cues.length; i += chunkSize) {
        chunks.push(cues.slice(i, i + chunkSize));
    }

    const apiCallFn = (chunk) => {
        const numberedLines = chunk.map((cue, idx) => `${idx + 1}. ${cue.text}`).join('\n');
        const prompt = `Dịch ${chunk.length} phụ đề sau sang tiếng Việt. Giữ nguyên thuật ngữ kỹ thuật tiếng Anh (pod, replicas, API, prompt, AI...). Trả về đúng format: mỗi dòng bắt đầu bằng số thứ tự và dấu chấm, theo sau là bản dịch. Không thêm giải thích.\n${numberedLines}`;
        return callOpenAiCompatible(openaiUrl, openaiKey, prompt);
    };

    const allTranslations = await processChunksBatched(chunks, 5, apiCallFn, onProgress);
    return reconstructVtt(header, cues, allTranslations);
}

/**
 * Gọi OpenAI-compatible /v1/chat/completions
 */
async function callOpenAiCompatible(baseUrl, apiKey, prompt) {
    const url = baseUrl.replace(/\/$/, '') + '/v1/chat/completions';

    const headers = { 'Content-Type': 'application/json' };
    if (apiKey) headers['Authorization'] = `Bearer ${apiKey}`;

    const res = await fetch(url, {
        method: 'POST',
        headers,
        body: JSON.stringify({
            model: 'deepseek/deepseek-v3.2',
            messages: [{ role: 'user', content: prompt }],
            stream: false,
        }),
    });

    if (!res.ok) {
        const errText = await res.text().catch(() => 'Unknown error');
        if (res.status === 401) throw new Error('API Key không hợp lệ');
        if (res.status === 429) throw new Error('Rate limited — chờ vài giây');
        throw new Error(`OpenAI API error ${res.status}: ${errText.substring(0, 200)}`);
    }

    const data = await res.json();
    const content = data?.choices?.[0]?.message?.content;
    if (!content) throw new Error('Response không có content');
    return content;
}

// ==================== BATCH PROCESSING ====================

/**
 * Xử lý chunks theo batch (concurrent)
 * @param {Array} chunks — mảng chunks, mỗi chunk = mảng cues
 * @param {number} batchSize — số request chạy song song (default 5)
 * @param {function} apiCallFn — (chunk) => Promise<string> (trả về response text)
 * @param {function} onProgress — callback({ chunk, total, percent })
 * @returns {Map} allTranslations — Map(cueIndex → translatedText)
 */
async function processChunksBatched(chunks, batchSize, apiCallFn, onProgress) {
    const allTranslations = new Map();
    let completedCount = 0;

    for (let batchStart = 0; batchStart < chunks.length; batchStart += batchSize) {
        const batchEnd = Math.min(batchStart + batchSize, chunks.length);
        const batchChunks = chunks.slice(batchStart, batchEnd);

        // Chạy batch song song
        const promises = batchChunks.map(async (chunk, batchIdx) => {
            const globalIdx = batchStart + batchIdx;

            const response = await apiCallFn(chunk);
            const translations = parseNumberedResponse(response, chunk.length);

            // Map translations về cue index gốc
            for (let j = 0; j < chunk.length; j++) {
                const promptIdx = j + 1;
                if (translations.has(promptIdx)) {
                    allTranslations.set(chunk[j].index, translations.get(promptIdx));
                }
            }

            completedCount++;
            return { globalIdx, translations };
        });

        await Promise.all(promises);

        // Report progress sau mỗi batch
        onProgress?.({
            chunk: Math.min(batchEnd, chunks.length),
            total: chunks.length,
            percent: Math.round((completedCount / chunks.length) * 100),
        });
    }

    return allTranslations;
}

// ==================== FEATHERLESS AI TRANSLATOR ====================

/**
 * Dịch VTT qua Featherless AI (/v1/completions — prompt-based) — batch 5 concurrent
 */
async function translateVttViaFeatherless(vttContent, settings, onProgress) {
    const { featherlessKey, chunkSize = 20, featherlessBatchSize = 1 } = settings;

    if (!featherlessKey) throw new Error('Featherless API Key chưa được cấu hình');

    const { cues, header } = parseVttSimple(vttContent);
    if (cues.length === 0) throw new Error('Không tìm thấy cue nào trong VTT');

    const chunks = [];
    for (let i = 0; i < cues.length; i += chunkSize) {
        chunks.push(cues.slice(i, i + chunkSize));
    }

    // apiCallFn trả về raw string để processChunksBatched tự parse (tránh double-parse)
    const apiCallFn = async (chunk) => {
        const numberedLines = chunk.map((cue, idx) => `${idx + 1}. ${cue.text}`).join('\n');
        const prompt = `Dịch ${chunk.length} phụ đề sau sang tiếng Việt. Giữ nguyên thuật ngữ kỹ thuật tiếng Anh (pod, replicas, API, prompt, AI...). Trả về đúng format: mỗi dòng bắt đầu bằng số thứ tự và dấu chấm, theo sau là bản dịch. Không thêm giải thích.\n${numberedLines}`;
        return withRetry(() => callFeatherlessApi(featherlessKey, prompt));
    };

    const allTranslations = await processChunksBatched(chunks, featherlessBatchSize, apiCallFn, onProgress);
    return reconstructVtt(header, cues, allTranslations);
}

/**
 * Gọi Featherless /v1/chat/completions (chat format)
 */
async function callFeatherlessApi(apiKey, prompt) {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 90000); // 90s timeout

    try {
        const res = await fetch('https://api.featherless.ai/v1/chat/completions', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${apiKey}`,
            },
            body: JSON.stringify({
                model: 'Sunbird/translategemma-12b-ug40-sft-merged',
                messages: [
                    {
                        role: 'system',
                        content: 'You are a subtitle translator. Translate English to Vietnamese. Keep technical terms in English. Return ONLY the numbered translations. No explanations, no extra text.',
                    },
                    { role: 'user', content: prompt },
                ],
                temperature: 0.3,
                max_tokens: 5000,
            }),
            signal: controller.signal,
        });

        if (!res.ok) {
            const errText = await res.text().catch(() => 'Unknown error');
            if (res.status === 401) throw new Error('Featherless API Key không hợp lệ');
            if (res.status === 429) throw new Error('Rate limited — chờ vài giây');
            throw new Error(`Featherless API error ${res.status}: ${errText.substring(0, 200)}`);
        }

        const data = await res.json();
        const content = data?.choices?.[0]?.message?.content;
        if (!content) throw new Error('Featherless response không có content');
        return content;
    } catch (err) {
        if (err.name === 'AbortError') throw new Error('Featherless timeout (90s)');
        throw err;
    } finally {
        clearTimeout(timeoutId);
    }
}

/**
 * Retry wrapper — retry tối đa maxRetries lần khi gặp network error
 */
async function withRetry(fn, maxRetries = 2) {
    let lastError;
    for (let attempt = 0; attempt <= maxRetries; attempt++) {
        try {
            return await fn();
        } catch (err) {
            lastError = err;
            const isRetryable = err.message.includes('Failed to fetch')
                || err.message.includes('timeout')
                || err.message.includes('network')
                || err.message.includes('AbortError');
            if (!isRetryable || attempt === maxRetries) throw err;
            console.log(`[API] Retry ${attempt + 1}/${maxRetries}: ${err.message}`);
            await new Promise(r => setTimeout(r, 2000 * (attempt + 1))); // backoff 2s, 4s
        }
    }
    throw lastError;
}

// Export for background script
if (typeof globalThis !== 'undefined') {
    globalThis.ApiTranslator = {
        translateVttViaApi,
        translateVttViaOpenAi,
        translateVttViaFeatherless,
        processChunksBatched,
        callOneminApi,
        callOpenAiCompatible,
        callFeatherlessApi,
        parseNumberedResponse,
        parseVttSimple,
        reconstructVtt,
    };
}
