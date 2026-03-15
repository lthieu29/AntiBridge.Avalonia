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

    if (message.type === 'TRANSLATE_API') {
        // Content script yêu cầu dịch trực tiếp qua API
        const { vttContent, settings } = message;
        const tabId = sender.tab?.id;

        console.log(`[BG] TRANSLATE_API: ${vttContent.length} chars, model=${settings.apiModel}`);

        // Keepalive: Chrome MV3 kills service worker after 30s idle
        const keepAlive = setInterval(() => {
            console.log('[BG] API keepalive ping');
        }, 25000);

        // Gọi API translator (async)
        globalThis.ApiTranslator.translateVttViaApi(
            vttContent,
            {
                apiKey: settings.apiKey,
                model: settings.apiModel,
                chunkSize: settings.chunkSize || 20,
            },
            // onProgress callback — gửi lại content script
            (progress) => {
                if (tabId) {
                    chrome.tabs.sendMessage(tabId, {
                        type: 'TRANSLATE_PROGRESS',
                        progress,
                    }).catch(() => {});
                }
            }
        ).then(translatedVtt => {
            // Gửi kết quả về content script
            if (tabId) {
                chrome.tabs.sendMessage(tabId, {
                    type: 'TRANSLATE_COMPLETE',
                    translatedVtt,
                }).catch(() => {});
            }
        }).catch(err => {
            console.error(`[BG] TRANSLATE_API error:`, err);
            if (tabId) {
                chrome.tabs.sendMessage(tabId, {
                    type: 'TRANSLATE_ERROR',
                    error: err.message,
                }).catch(() => {});
            }
        }).finally(() => {
            clearInterval(keepAlive);
        });

        sendResponse({ started: true });
        return true;
    }

    if (message.type === 'TRANSLATE_BRIDGE') {
        // Content script yêu cầu dịch qua Bridge Server
        // Route qua background để tránh Chrome content script fetch bị disconnect
        const { vttContent, bridgeUrl } = message;
        const tabId = sender.tab?.id;

        console.log(`[BG] TRANSLATE_BRIDGE: ${vttContent.length} chars → ${bridgeUrl}`);

        // Keepalive: Chrome MV3 kills service worker after 30s idle
        // Timer giữ worker sống trong suốt quá trình dịch
        const keepAlive = setInterval(() => {
            console.log('[BG] Bridge keepalive ping');
        }, 25000);

        // Gọi Bridge Server SSE từ background
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

                // Parse SSE events
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

                        if (eventType === 'progress' && tabId) {
                            chrome.tabs.sendMessage(tabId, {
                                type: 'TRANSLATE_PROGRESS',
                                progress: parsed,
                            }).catch(() => {});
                        }

                        if (eventType === 'complete' && tabId) {
                            console.log('[BG] Bridge translation complete, sending to tab');
                            chrome.tabs.sendMessage(tabId, {
                                type: 'TRANSLATE_COMPLETE',
                                translatedVtt: parsed.translatedVtt,
                            }).catch(() => {});
                        }

                        if (eventType === 'error' && tabId) {
                            chrome.tabs.sendMessage(tabId, {
                                type: 'TRANSLATE_ERROR',
                                error: parsed.message,
                            }).catch(() => {});
                        }

                        if (eventType === 'cancelled' && tabId) {
                            chrome.tabs.sendMessage(tabId, {
                                type: 'TRANSLATE_ERROR',
                                error: 'Đã hủy',
                            }).catch(() => {});
                        }
                    } catch (e) {
                        console.warn('[BG] Parse SSE error:', e);
                    }
                }
            }
        }).catch(err => {
            console.error(`[BG] TRANSLATE_BRIDGE error:`, err);
            if (tabId) {
                chrome.tabs.sendMessage(tabId, {
                    type: 'TRANSLATE_ERROR',
                    error: err.message,
                }).catch(() => {});
            }
        }).finally(() => {
            clearInterval(keepAlive);
            console.log('[BG] Bridge keepalive cleared');
        });

        sendResponse({ started: true });
        return true;
    }
});

// Cleanup khi tab đóng
chrome.tabs.onRemoved.addListener((tabId) => {
    detectedVttUrls.delete(tabId);
});

console.log('[BG] Subtitle Translator background loaded');

