/**
 * Background Service Worker - Bắt VTT từ Udemy network requests
 */

// Import API translator
importScripts('api-translator.js');

// Lưu VTT URL phát hiện được cho mỗi tab
const detectedVttUrls = new Map();

// Lắng nghe network requests hoàn tất
chrome.webRequest.onCompleted.addListener(
    (details) => {
        const url = details.url;

        // Chỉ bắt subtitle VTT thực từ vtt-c.udemycdn.com
        // Loại bỏ thumb-sprites.vtt (từ mp4-c.udemycdn.com)
        const isSubtitleVtt = url.match(/vtt-[a-z]\.udemycdn\.com\/.+\.vtt/i);
        if (isSubtitleVtt) {
            console.log(`[BG] Subtitle VTT detected: ${url.substring(0, 100)}`);

            // Lưu URL cho tab
            detectedVttUrls.set(details.tabId, url);

            // Thông báo content script
            chrome.tabs.sendMessage(details.tabId, {
                type: 'VTT_DETECTED',
                url: url,
            }).catch(err => {
                console.log(`[BG] Cannot send to tab ${details.tabId}: ${err.message}`);
            });
        }
    },
    {
        urls: [
            "*://*.udemy.com/*",
            "*://*.udemycdn.com/*",
            "*://*.cloudfront.net/*",
            "*://*.amazonaws.com/*"
        ]
    }
);

// Xử lý messages từ popup/content script
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === 'GET_VTT_URL') {
        // Popup yêu cầu VTT URL cho tab hiện tại
        chrome.tabs.query({ active: true, currentWindow: true }, (tabs) => {
            if (tabs[0]) {
                const url = detectedVttUrls.get(tabs[0].id);
                sendResponse({ url: url || null });
            } else {
                sendResponse({ url: null });
            }
        });
        return true; // async response
    }

    if (message.type === 'FETCH_VTT') {
        // Content script yêu cầu fetch VTT (bypass CORS)
        fetch(message.url)
            .then(r => r.text())
            .then(text => sendResponse({ content: text }))
            .catch(err => sendResponse({ error: err.message }));
        return true; // async response
    }
});

// ==================== PORT-BASED TRANSLATION ====================
// Port giữ service worker sống suốt quá trình dịch (fix MV3 30s timeout)

chrome.runtime.onConnect.addListener((port) => {
    if (port.name !== 'translate') return;

    console.log('[BG] Translation port connected');

    port.onMessage.addListener((message) => {
        if (message.type === 'TRANSLATE_API') {
            handleApiTranslation(port, message);
        }
        if (message.type === 'TRANSLATE_BRIDGE') {
            handleBridgeTranslation(port, message);
        }
        if (message.type === 'TRANSLATE_OPENAI') {
            handleOpenAiTranslation(port, message);
        }
    });

    port.onDisconnect.addListener(() => {
        console.log('[BG] Translation port disconnected');
    });
});

/**
 * Cache helpers — kiểm tra và lưu cache qua bridge server
 * Nếu server tắt → bỏ qua (graceful)
 */
const DEFAULT_BRIDGE = 'http://localhost:3000';

async function checkCache(bridgeUrl, vttContent) {
    try {
        const res = await fetch(`${bridgeUrl}/api/cache/get`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ vttContent }),
            signal: AbortSignal.timeout(3000),
        });
        if (!res.ok) return null;
        const data = await res.json();
        if (data.hit) {
            console.log(`[BG] Cache HIT: ${data.translatedVtt.length} chars`);
            return data.translatedVtt;
        }
        console.log('[BG] Cache MISS');
        return null;
    } catch (e) {
        console.log('[BG] Cache check skipped (server down)');
        return null;
    }
}

function saveCache(bridgeUrl, vttContent, translatedVtt) {
    // Fire-and-forget — không block
    fetch(`${bridgeUrl}/api/cache/set`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ vttContent, translatedVtt }),
        signal: AbortSignal.timeout(5000),
    }).then(() => {
        console.log('[BG] Cache SAVED');
    }).catch(() => {
        console.log('[BG] Cache save skipped (server down)');
    });
}

/**
 * API mode: cache-first → 1min.ai API (batch 5) → save cache
 */
async function handleApiTranslation(port, message) {
    const { vttContent, settings } = message;
    const bridgeUrl = settings.bridgeUrl || DEFAULT_BRIDGE;
    console.log(`[BG] TRANSLATE_API: ${vttContent.length} chars, model=${settings.apiModel}`);

    try {
        // 1. Check cache
        const cached = await checkCache(bridgeUrl, vttContent);
        if (cached) {
            try { port.postMessage({ type: 'TRANSLATE_COMPLETE', translatedVtt: cached }); } catch (e) {}
            try { port.disconnect(); } catch (e) {}
            return;
        }

        // 2. Call API (batch 5)
        const translatedVtt = await globalThis.ApiTranslator.translateVttViaApi(
            vttContent,
            {
                apiKey: settings.apiKey,
                model: settings.apiModel,
                chunkSize: settings.chunkSize || 20,
            },
            (progress) => {
                try { port.postMessage({ type: 'TRANSLATE_PROGRESS', progress }); } catch (e) {}
            }
        );

        // 3. Save cache (fire-and-forget)
        saveCache(bridgeUrl, vttContent, translatedVtt);

        try { port.postMessage({ type: 'TRANSLATE_COMPLETE', translatedVtt }); } catch (e) {}
    } catch (err) {
        console.error('[BG] TRANSLATE_API error:', err);
        try { port.postMessage({ type: 'TRANSLATE_ERROR', error: err.message }); } catch (e) {}
    } finally {
        try { port.disconnect(); } catch (e) {}
    }
}

/**
 * OpenAI mode: cache-first → OpenAI API (batch 5) → save cache
 */
async function handleOpenAiTranslation(port, message) {
    const { vttContent, settings } = message;
    const bridgeUrl = settings.bridgeUrl || DEFAULT_BRIDGE;
    console.log(`[BG] TRANSLATE_OPENAI: ${vttContent.length} chars → ${settings.openaiUrl}`);

    try {
        // 1. Check cache
        const cached = await checkCache(bridgeUrl, vttContent);
        if (cached) {
            try { port.postMessage({ type: 'TRANSLATE_COMPLETE', translatedVtt: cached }); } catch (e) {}
            try { port.disconnect(); } catch (e) {}
            return;
        }

        // 2. Call API (batch 5)
        const translatedVtt = await globalThis.ApiTranslator.translateVttViaOpenAi(
            vttContent,
            {
                openaiUrl: settings.openaiUrl,
                openaiKey: settings.openaiKey,
                chunkSize: settings.chunkSize || 20,
            },
            (progress) => {
                try { port.postMessage({ type: 'TRANSLATE_PROGRESS', progress }); } catch (e) {}
            }
        );

        // 3. Save cache (fire-and-forget)
        saveCache(bridgeUrl, vttContent, translatedVtt);

        try { port.postMessage({ type: 'TRANSLATE_COMPLETE', translatedVtt }); } catch (e) {}
    } catch (err) {
        console.error('[BG] TRANSLATE_OPENAI error:', err);
        try { port.postMessage({ type: 'TRANSLATE_ERROR', error: err.message }); } catch (e) {}
    } finally {
        try { port.disconnect(); } catch (e) {}
    }
}

/**
 * Bridge mode: dịch qua Bridge Server SSE
 */
function handleBridgeTranslation(port, message) {
    const { vttContent, bridgeUrl } = message;
    console.log(`[BG] TRANSLATE_BRIDGE via port: ${vttContent.length} chars → ${bridgeUrl}`);

    fetch(`${bridgeUrl}/api/translate-vtt`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ vttContent }),
    }).then(async response => {
        if (!response.ok) {
            const err = await response.text();
            throw new Error(`Bridge Server error: ${err}`);
        }

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = '';

        while (true) {
            const { done, value } = await reader.read();
            if (done) break;

            buffer += decoder.decode(value, { stream: true });

            const events = buffer.split('\n\n');
            buffer = events.pop() || '';

            for (const event of events) {
                const lines = event.split('\n');
                let eventType = '';
                let data = '';

                for (const line of lines) {
                    if (line.startsWith('event: ')) eventType = line.substring(7);
                    if (line.startsWith('data: ')) data = line.substring(6);
                }

                if (!eventType || !data) continue;

                try {
                    const parsed = JSON.parse(data);

                    if (eventType === 'progress') {
                        try { port.postMessage({ type: 'TRANSLATE_PROGRESS', progress: parsed }); } catch (e) {}
                    }
                    if (eventType === 'complete') {
                        console.log('[BG] Bridge translation complete');
                        try { port.postMessage({ type: 'TRANSLATE_COMPLETE', translatedVtt: parsed.translatedVtt }); } catch (e) {}
                    }
                    if (eventType === 'error') {
                        try { port.postMessage({ type: 'TRANSLATE_ERROR', error: parsed.message }); } catch (e) {}
                    }
                    if (eventType === 'cancelled') {
                        try { port.postMessage({ type: 'TRANSLATE_ERROR', error: 'Đã hủy' }); } catch (e) {}
                    }
                } catch (e) {
                    console.warn('[BG] Parse SSE error:', e);
                }
            }
        }
    }).catch(err => {
        console.error('[BG] TRANSLATE_BRIDGE error:', err);
        try { port.postMessage({ type: 'TRANSLATE_ERROR', error: err.message }); } catch (e) {}
    }).finally(() => {
        try { port.disconnect(); } catch (e) {}
    });
}

// Cleanup khi tab đóng
chrome.tabs.onRemoved.addListener((tabId) => {
    detectedVttUrls.delete(tabId);
});

console.log('[BG] Subtitle Translator background loaded');

