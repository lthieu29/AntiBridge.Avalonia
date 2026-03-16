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

    // 1. Parse VTT
    const { cues, header } = parseVttSimple(vttContent);
    if (cues.length === 0) throw new Error('Không tìm thấy cue nào trong VTT');

    // 2. Chunk cues
    const chunks = [];
    for (let i = 0; i < cues.length; i += chunkSize) {
        chunks.push(cues.slice(i, i + chunkSize));
    }

    // 3. Dịch từng chunk
    const allTranslations = new Map();

    for (let i = 0; i < chunks.length; i++) {
        const chunk = chunks[i];
        const progress = {
            chunk: i + 1,
            total: chunks.length,
            percent: Math.round(((i + 1) / chunks.length) * 100),
        };
        onProgress?.(progress);

        // Build prompt
        const numberedLines = chunk
            .map((cue, idx) => `${idx + 1}. ${cue.text}`)
            .join(' ');
        const prompt = `Dịch ${chunk.length} phụ đề sau sang tiếng Việt. Giữ nguyên thuật ngữ kỹ thuật tiếng Anh (pod, replicas, API, prompt, AI...). Trả về đúng format: mỗi dòng bắt đầu bằng số thứ tự và dấu chấm, theo sau là bản dịch. Không thêm giải thích. ${numberedLines}`;

        // Call API (non-streaming for reliability)
        const response = await callOneminApi(apiKey, model, prompt);

        // Parse translations
        const translations = parseNumberedResponse(response, chunk.length);

        // Map back to original cue indices
        for (let j = 0; j < chunk.length; j++) {
            const promptIdx = j + 1;
            if (translations.has(promptIdx)) {
                allTranslations.set(chunk[j].index, translations.get(promptIdx));
            }
        }

        // Delay giữa chunks
        if (i < chunks.length - 1) {
            await new Promise(r => setTimeout(r, 500));
        }
    }

    // 4. Reconstruct VTT
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
 * Dịch VTT qua OpenAI-compatible API (nano-gpt.com, etc.)
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

    const allTranslations = new Map();

    for (let i = 0; i < chunks.length; i++) {
        const chunk = chunks[i];
        const progress = {
            chunk: i + 1,
            total: chunks.length,
            percent: Math.round(((i + 1) / chunks.length) * 100),
        };
        onProgress?.(progress);

        const numberedLines = chunk
            .map((cue, idx) => `${idx + 1}. ${cue.text}`)
            .join('\n');
        const prompt = `Dịch ${chunk.length} phụ đề sau sang tiếng Việt. Giữ nguyên thuật ngữ kỹ thuật tiếng Anh (pod, replicas, API, prompt, AI...). Trả về đúng format: mỗi dòng bắt đầu bằng số thứ tự và dấu chấm, theo sau là bản dịch. Không thêm giải thích.\n${numberedLines}`;

        const response = await callOpenAiCompatible(openaiUrl, openaiKey, prompt);

        const translations = parseNumberedResponse(response, chunk.length);

        for (let j = 0; j < chunk.length; j++) {
            const promptIdx = j + 1;
            if (translations.has(promptIdx)) {
                allTranslations.set(chunk[j].index, translations.get(promptIdx));
            }
        }

        if (i < chunks.length - 1) {
            await new Promise(r => setTimeout(r, 300));
        }
    }

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

// Export for background script
if (typeof globalThis !== 'undefined') {
    globalThis.ApiTranslator = {
        translateVttViaApi,
        translateVttViaOpenAi,
        callOneminApi,
        callOpenAiCompatible,
        parseNumberedResponse,
        parseVttSimple,
        reconstructVtt,
    };
}
