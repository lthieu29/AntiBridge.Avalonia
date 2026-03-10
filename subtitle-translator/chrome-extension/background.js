/**
 * Background Service Worker - Bắt VTT từ Udemy network requests
 */

// Lưu VTT URL phát hiện được cho mỗi tab
const detectedVttUrls = new Map();

// Lắng nghe network requests hoàn tất
chrome.webRequest.onCompleted.addListener(
    (details) => {
        const url = details.url;

        // Kiểm tra VTT file
        if (url.includes('.vtt') || url.match(/subtitle|caption|track/i)) {
            console.log(`[BG] VTT detected: ${url.substring(0, 100)}`);

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

// Cleanup khi tab đóng
chrome.tabs.onRemoved.addListener((tabId) => {
    detectedVttUrls.delete(tabId);
});

console.log('[BG] Subtitle Translator background loaded');
