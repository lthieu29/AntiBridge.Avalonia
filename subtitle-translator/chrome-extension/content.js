/**
 * Content Script - Chạy trên trang Udemy
 * - Nhận VTT từ background
 * - Gửi dịch tới Bridge Server
 * - Hiển thị phụ đề overlay
 */

const BRIDGE_URL = 'http://localhost:3000';

// State
let subtitles = [];       // [{startTime, endTime, text}]
let overlayEnabled = true;
let overlayElement = null;
let videoElement = null;
let currentVttUrl = null;

// ==================== VTT HANDLING ====================

/**
 * Parse VTT text → mảng subtitle objects với thời gian (giây)
 */
function parseVttToSubtitles(vttText) {
    const lines = vttText.replace(/\r\n/g, '\n').split('\n');
    const subs = [];
    let i = 0;

    // Skip header
    if (lines[0]?.trim().startsWith('WEBVTT')) {
        i = 1;
        while (i < lines.length && lines[i].trim() !== '') i++;
    }

    while (i < lines.length) {
        while (i < lines.length && lines[i].trim() === '') i++;
        if (i >= lines.length) break;

        // Skip NOTE/STYLE blocks
        if (lines[i].trim().startsWith('NOTE') || lines[i].trim().startsWith('STYLE')) {
            while (i < lines.length && lines[i].trim() !== '') i++;
            continue;
        }

        // Skip optional cue number
        if (lines[i].trim().match(/^\d+$/) && i + 1 < lines.length && lines[i + 1].includes('-->')) {
            i++;
        }

        // Timestamp
        const match = lines[i]?.trim().match(/^(\d{2}:\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}\.\d{3})/);
        if (!match) { i++; continue; }

        const startTime = timestampToSeconds(match[1]);
        const endTime = timestampToSeconds(match[2]);
        i++;

        // Text lines
        const textLines = [];
        while (i < lines.length && lines[i].trim() !== '') {
            // Strip VTT tags like <c>, </c>, <b>, etc
            textLines.push(lines[i].replace(/<[^>]+>/g, '').trim());
            i++;
        }

        if (textLines.length > 0) {
            subs.push({
                startTime,
                endTime,
                text: textLines.join(' '),
            });
        }
    }

    return subs;
}

function timestampToSeconds(ts) {
    const parts = ts.split(':');
    const h = parseInt(parts[0]);
    const m = parseInt(parts[1]);
    const [s, ms] = parts[2].split('.');
    return h * 3600 + m * 60 + parseInt(s) + parseInt(ms) / 1000;
}

// ==================== SUBTITLE OVERLAY ====================

/**
 * Tạo overlay element
 */
function createOverlay() {
    if (overlayElement) return overlayElement;

    overlayElement = document.createElement('div');
    overlayElement.id = 'ust-subtitle-overlay';
    overlayElement.className = 'ust-overlay';
    document.body.appendChild(overlayElement);

    return overlayElement;
}

/**
 * Binary search tìm subtitle active tại thời điểm hiện tại
 */
function findActiveSubtitle(time) {
    if (subtitles.length === 0) return null;

    let low = 0;
    let high = subtitles.length - 1;
    let result = null;

    while (low <= high) {
        const mid = Math.floor((low + high) / 2);
        const sub = subtitles[mid];

        if (time >= sub.startTime && time <= sub.endTime) {
            return sub;
        } else if (time < sub.startTime) {
            high = mid - 1;
        } else {
            result = sub; // Giữ lại subtitle cuối cùng đã qua
            low = mid + 1;
        }
    }

    return null; // Không có subtitle active tại thời điểm này
}

/**
 * Đồng bộ overlay với video
 */
function syncSubtitles() {
    if (!videoElement || !overlayElement || !overlayEnabled) {
        if (overlayElement) overlayElement.style.display = 'none';
        return;
    }

    const time = videoElement.currentTime;
    const active = findActiveSubtitle(time);

    if (active) {
        overlayElement.textContent = active.text;
        overlayElement.style.display = 'block';
    } else {
        overlayElement.style.display = 'none';
    }
}

/**
 * Tìm và attach vào video player
 */
function attachToVideo() {
    if (videoElement) return;

    const video = document.querySelector('video');
    if (!video) {
        // Retry sau 2s
        setTimeout(attachToVideo, 2000);
        return;
    }

    videoElement = video;
    createOverlay();

    // Position overlay relative to video
    positionOverlay();

    // Listen cho timeupdate
    videoElement.addEventListener('timeupdate', syncSubtitles);

    // Reposition khi fullscreen thay đổi
    document.addEventListener('fullscreenchange', () => {
        setTimeout(positionOverlay, 500);
    });

    // Reposition khi resize
    window.addEventListener('resize', () => {
        requestAnimationFrame(positionOverlay);
    });

    console.log('[UST] Attached to video player');
}

/**
 * Vị trí overlay
 */
function positionOverlay() {
    if (!overlayElement || !videoElement) return;

    // Tìm container video
    const container = videoElement.closest('[data-purpose="curriculum-item-viewer"]')
        || videoElement.closest('.video-player--container--')
        || videoElement.parentElement;

    if (container) {
        container.style.position = 'relative';
        if (overlayElement.parentElement !== container) {
            container.appendChild(overlayElement);
        }
    }
}

// ==================== BRIDGE SERVER COMMUNICATION ====================

/**
 * Gửi VTT tới Bridge Server để dịch (SSE)
 */
async function translateVtt(vttContent) {
    console.log('[UST] Bắt đầu dịch VTT...');

    return new Promise((resolve, reject) => {
        // Dùng fetch + ReadableStream để đọc SSE
        fetch(`${BRIDGE_URL}/api/translate-vtt`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ vttContent }),
        }).then(async response => {
            if (!response.ok) {
                const err = await response.text();
                reject(new Error(`Bridge Server error: ${err}`));
                return;
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
                buffer = events.pop() || ''; // Giữ lại phần chưa hoàn chỉnh

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
                            console.log(`[UST] Progress: ${parsed.chunk}/${parsed.total} (${parsed.percent}%)`);
                            // Thông báo popup
                            chrome.runtime.sendMessage({
                                type: 'TRANSLATION_PROGRESS',
                                ...parsed,
                            }).catch(() => { });
                        }

                        if (eventType === 'complete') {
                            console.log('[UST] ✅ Dịch xong!');
                            resolve(parsed.translatedVtt);
                        }

                        if (eventType === 'error') {
                            reject(new Error(parsed.message));
                        }

                        if (eventType === 'cancelled') {
                            reject(new Error('Đã hủy'));
                        }
                    } catch (e) {
                        console.warn('[UST] Parse SSE error:', e);
                    }
                }
            }
        }).catch(reject);
    });
}

// ==================== MESSAGE HANDLERS ====================

// Nhận messages từ background (VTT detected) và popup (commands)
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === 'VTT_DETECTED') {
        console.log(`[UST] VTT detected: ${message.url.substring(0, 80)}`);
        currentVttUrl = message.url;
        sendResponse({ received: true });
    }

    if (message.type === 'START_TRANSLATION') {
        handleStartTranslation();
        sendResponse({ started: true });
    }

    if (message.type === 'TOGGLE_SUBTITLES') {
        overlayEnabled = !overlayEnabled;
        syncSubtitles();
        sendResponse({ enabled: overlayEnabled });
    }

    if (message.type === 'GET_STATE') {
        sendResponse({
            hasVtt: !!currentVttUrl,
            hasSubtitles: subtitles.length > 0,
            overlayEnabled,
            subtitleCount: subtitles.length,
        });
    }

    return true;
});

/**
 * Xử lý bắt đầu dịch
 */
async function handleStartTranslation() {
    if (!currentVttUrl) {
        console.warn('[UST] Chưa phát hiện VTT URL');
        // Thử tìm qua <track> element
        const vid = document.querySelector('video');
        if (vid) {
            const tracks = vid.querySelectorAll('track[kind="captions"], track[kind="subtitles"]');
            if (tracks.length > 0) {
                currentVttUrl = tracks[0].src;
                console.log(`[UST] Tìm thấy VTT qua <track>: ${currentVttUrl}`);
            }
        }

        if (!currentVttUrl) {
            chrome.runtime.sendMessage({
                type: 'TRANSLATION_ERROR',
                message: 'Không tìm thấy phụ đề. Hãy bật captions/subtitles trong video player trước.',
            }).catch(() => { });
            return;
        }
    }

    try {
        // Fetch VTT qua background (bypass CORS)
        const response = await new Promise((resolve, reject) => {
            chrome.runtime.sendMessage(
                { type: 'FETCH_VTT', url: currentVttUrl },
                (resp) => {
                    if (resp?.error) reject(new Error(resp.error));
                    else resolve(resp);
                }
            );
        });

        const vttContent = response.content;
        console.log(`[UST] VTT fetched: ${vttContent.length} chars`);

        // Dịch
        const translatedVtt = await translateVtt(vttContent);

        // Parse translated VTT → subtitles
        subtitles = parseVttToSubtitles(translatedVtt);
        console.log(`[UST] Loaded ${subtitles.length} translated subtitles`);

        // Kích hoạt overlay
        overlayEnabled = true;
        attachToVideo();
        syncSubtitles();

        chrome.runtime.sendMessage({
            type: 'TRANSLATION_COMPLETE',
            count: subtitles.length,
        }).catch(() => { });

    } catch (err) {
        console.error('[UST] Translation error:', err);
        chrome.runtime.sendMessage({
            type: 'TRANSLATION_ERROR',
            message: err.message,
        }).catch(() => { });
    }
}

// ==================== INIT ====================

// Attach to video when page loads
attachToVideo();

// Observe DOM changes (Udemy SPA — video element có thể load sau)
const observer = new MutationObserver(() => {
    if (!videoElement) attachToVideo();
});
observer.observe(document.body, { childList: true, subtree: true });

console.log('[UST] Content script loaded on:', window.location.href);
