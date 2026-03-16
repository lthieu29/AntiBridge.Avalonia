/**
 * Content Script - Chạy trên trang Udemy
 * - Nhận VTT từ background
 * - Inject floating translate panel vào video player
 * - Gửi dịch tới Bridge Server
 * - Hiển thị phụ đề overlay
 */

const DEFAULT_BRIDGE_URL = 'http://localhost:3000';

// State
let subtitles = [];       // [{startTime, endTime, text}]
let overlayEnabled = true;
let overlayElement = null;
let videoElement = null;
let currentVttUrl = null;
let panelElement = null;
let isTranslating = false;

// Settings
const SETTINGS_KEY = 'ust_settings';
const SECURE_KEYS_KEY = 'ust_secure_keys'; // chrome.storage.local
let currentSettings = {
    translationMode: 'openai',
    apiKey: '',           // 1min.ai key (stored in chrome.storage)
    apiModel: 'deepseek-chat',
    openaiUrl: 'https://nano-gpt.com/api',
    openaiKey: '',        // OpenAI key (stored in chrome.storage)
    chunkSize: 20,
    bridgeUrl: DEFAULT_BRIDGE_URL,
};

/**
 * Load settings: non-sensitive from localStorage, API keys from chrome.storage.local
 */
function loadSettings() {
    try {
        const saved = localStorage.getItem(SETTINGS_KEY);
        if (saved) {
            Object.assign(currentSettings, JSON.parse(saved));
        }
    } catch (e) {
        console.warn('[UST] Failed to load settings:', e);
    }
    // Load API keys from secure storage (async)
    chrome.storage.local.get(SECURE_KEYS_KEY, (result) => {
        if (result[SECURE_KEYS_KEY]) {
            const keys = result[SECURE_KEYS_KEY];
            if (keys.apiKey) currentSettings.apiKey = keys.apiKey;
            if (keys.openaiKey) currentSettings.openaiKey = keys.openaiKey;
        }
        console.log(`[UST] Settings loaded: mode=${currentSettings.translationMode}, model=${currentSettings.apiModel}`);
    });
}

/**
 * Save settings: non-sensitive to localStorage, API keys to chrome.storage.local
 */
function saveSettings() {
    try {
        // Non-sensitive settings → localStorage
        const nonSensitive = { ...currentSettings };
        delete nonSensitive.apiKey;
        delete nonSensitive.openaiKey;
        localStorage.setItem(SETTINGS_KEY, JSON.stringify(nonSensitive));

        // API keys → chrome.storage.local (Udemy không đọc được)
        chrome.storage.local.set({
            [SECURE_KEYS_KEY]: {
                apiKey: currentSettings.apiKey,
                openaiKey: currentSettings.openaiKey,
            }
        });

        console.log('[UST] Settings saved (keys in secure storage)');
    } catch (e) {
        console.warn('[UST] Failed to save settings:', e);
    }
}

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

        // Timestamp — hỗ trợ cả MM:SS.mmm và HH:MM:SS.mmm
        const match = lines[i]?.trim().match(/^((?:\d{2}:)?\d{2}:\d{2}\.\d{3})\s*-->\s*((?:\d{2}:)?\d{2}:\d{2}\.\d{3})/);
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
    if (parts.length === 3) {
        // HH:MM:SS.mmm
        const h = parseInt(parts[0]);
        const m = parseInt(parts[1]);
        const [s, ms] = parts[2].split('.');
        return h * 3600 + m * 60 + parseInt(s) + parseInt(ms) / 1000;
    } else {
        // MM:SS.mmm
        const m = parseInt(parts[0]);
        const [s, ms] = parts[1].split('.');
        return m * 60 + parseInt(s) + parseInt(ms) / 1000;
    }
}

// ==================== IN-PAGE TRANSLATE PANEL ====================

/**
 * Inject floating translate panel vào trang Udemy
 */
function createTranslatePanel() {
    if (panelElement) return panelElement;

    // Load settings first
    loadSettings();

    // Container
    panelElement = document.createElement('div');
    panelElement.id = 'ust-translate-panel';
    panelElement.innerHTML = `
        <div class="ust-panel-header">
            <span class="ust-panel-title">🌐 Subtitle Translator</span>
            <div style="display:flex;gap:4px;align-items:center;">
                <button class="ust-panel-settings" id="ust-panel-settings" title="Settings">⚙️</button>
                <button class="ust-panel-close" id="ust-panel-close">✕</button>
            </div>
        </div>

        <!-- MAIN VIEW -->
        <div class="ust-panel-body" id="ust-main-view">
            <div class="ust-mode-indicator" id="ust-mode-indicator"></div>
            <div class="ust-status-row">
                <span class="ust-dot" id="ust-bridge-dot"></span>
                <span class="ust-status-label" id="ust-bridge-label">Kiểm tra...</span>
            </div>
            <div class="ust-status-row">
                <span class="ust-dot" id="ust-vtt-dot"></span>
                <span class="ust-status-label" id="ust-vtt-label">VTT chưa phát hiện</span>
            </div>
            <div class="ust-progress" id="ust-progress" style="display:none;">
                <div class="ust-progress-label" id="ust-progress-label">Đang dịch...</div>
                <div class="ust-progress-bar">
                    <div class="ust-progress-fill" id="ust-progress-fill"></div>
                </div>
                <div class="ust-progress-text" id="ust-progress-text">0%</div>
            </div>
            <div class="ust-actions">
                <button class="ust-btn ust-btn-primary" id="ust-translate-btn" disabled>🌐 Dịch Phụ Đề</button>
                <button class="ust-btn ust-btn-secondary" id="ust-toggle-btn" disabled>👁️ Bật/Tắt</button>
            </div>
            <div class="ust-info" id="ust-info"></div>
        </div>

        <!-- SETTINGS VIEW -->
        <div class="ust-panel-body ust-settings-view" id="ust-settings-view" style="display:none;">
            <div class="ust-field">
                <label class="ust-label">Translation Mode</label>
                <div class="ust-radio-group">
                    <label class="ust-radio"><input type="radio" name="ust-mode" value="openai" id="ust-mode-openai"> 🤖 OpenAI API <small>(deepseek-v3.2)</small></label>
                    <label class="ust-radio"><input type="radio" name="ust-mode" value="api" id="ust-mode-api"> 🚀 1min.ai <small>(nhiều model)</small></label>
                    <label class="ust-radio"><input type="radio" name="ust-mode" value="bridge" id="ust-mode-bridge"> 🌉 Bridge Server <small>(miễn phí)</small></label>
                </div>
            </div>
            <div id="ust-openai-fields">
                <div class="ust-field">
                    <label class="ust-label">🌐 Server URL</label>
                    <input type="text" id="ust-openai-url" class="ust-input" value="https://nano-gpt.com/api">
                </div>
                <div class="ust-field">
                    <label class="ust-label">🔑 API Key <small>(nếu cần)</small></label>
                    <input type="password" id="ust-openai-key" class="ust-input" placeholder="Bearer key (optional)">
                </div>
                <div class="ust-field">
                    <label class="ust-label">📌 Model: <code>deepseek/deepseek-v3.2</code> (cố định)</label>
                </div>
            </div>
            <div id="ust-api-fields" style="display:none;">
                <div class="ust-field">
                    <label class="ust-label">🔑 API Key</label>
                    <input type="password" id="ust-api-key" class="ust-input" placeholder="Nhập 1min.ai API key...">
                </div>
                <div class="ust-field">
                    <label class="ust-label">🤖 Model</label>
                    <select id="ust-api-model" class="ust-input">
                        <option value="deepseek-chat">DeepSeek Chat</option>
                        <option value="gemini-2.0-flash">Gemini 2.0 Flash</option>
                        <option value="gpt-4o-mini">GPT-4o Mini</option>
                        <option value="qwen-plus">Qwen Plus</option>
                        <option value="gpt-4o">GPT-4o</option>
                        <option value="claude-3.5-sonnet">Claude 3.5 Sonnet</option>
                    </select>
                </div>
            </div>
            <div id="ust-bridge-fields" style="display:none;">
                <div class="ust-field">
                    <label class="ust-label">🌐 Bridge URL</label>
                    <input type="text" id="ust-bridge-url" class="ust-input" value="http://localhost:3000">
                </div>
            </div>
            <div class="ust-actions">
                <button class="ust-btn ust-btn-primary" id="ust-save-settings">💾 Lưu</button>
                <button class="ust-btn ust-btn-secondary" id="ust-cancel-settings">← Quay lại</button>
            </div>
            <div class="ust-info" id="ust-settings-info"></div>
        </div>
    `;
    document.body.appendChild(panelElement);

    // Floating trigger button
    const trigger = document.createElement('button');
    trigger.id = 'ust-trigger-btn';
    trigger.innerHTML = '🌐';
    trigger.title = 'Subtitle Translator';
    document.body.appendChild(trigger);

    // Event: Toggle panel
    trigger.addEventListener('click', () => {
        const isVisible = panelElement.classList.contains('ust-panel-visible');
        panelElement.classList.toggle('ust-panel-visible', !isVisible);
        if (!isVisible) {
            showMainView();
        }
    });

    // Event: Close panel
    document.getElementById('ust-panel-close').addEventListener('click', () => {
        panelElement.classList.remove('ust-panel-visible');
    });

    // Event: Settings toggle
    document.getElementById('ust-panel-settings').addEventListener('click', () => {
        const settingsView = document.getElementById('ust-settings-view');
        const isSettings = settingsView.style.display !== 'none';
        if (isSettings) {
            showMainView();
        } else {
            showSettingsView();
        }
    });

    // Event: Save settings
    document.getElementById('ust-save-settings').addEventListener('click', () => {
        let mode = 'openai';
        if (document.getElementById('ust-mode-api').checked) mode = 'api';
        if (document.getElementById('ust-mode-bridge').checked) mode = 'bridge';

        const apiKey = document.getElementById('ust-api-key').value.trim();
        const apiModel = document.getElementById('ust-api-model').value;
        const openaiUrl = document.getElementById('ust-openai-url').value.trim();
        const openaiKey = document.getElementById('ust-openai-key').value.trim();
        const bridgeUrl = document.getElementById('ust-bridge-url').value.trim() || DEFAULT_BRIDGE_URL;

        if (mode === 'api' && !apiKey) {
            const info = document.getElementById('ust-settings-info');
            if (info) { info.textContent = '⚠️ Vui lòng nhập 1min.ai API Key'; info.className = 'ust-info ust-info-err'; }
            return;
        }
        if (mode === 'openai' && !openaiUrl) {
            const info = document.getElementById('ust-settings-info');
            if (info) { info.textContent = '⚠️ Vui lòng nhập Server URL'; info.className = 'ust-info ust-info-err'; }
            return;
        }

        currentSettings.translationMode = mode;
        currentSettings.apiKey = apiKey;
        currentSettings.apiModel = apiModel;
        currentSettings.openaiUrl = openaiUrl;
        currentSettings.openaiKey = openaiKey;
        currentSettings.bridgeUrl = bridgeUrl;
        saveSettings();

        const info = document.getElementById('ust-settings-info');
        if (info) { info.textContent = '✅ Đã lưu!'; info.className = 'ust-info ust-info-ok'; }

        setTimeout(() => showMainView(), 500);
    });

    // Event: Cancel settings
    document.getElementById('ust-cancel-settings').addEventListener('click', () => {
        showMainView();
    });

    // Event: Mode radio toggle
    function updateModeFields() {
        const mode = document.querySelector('input[name="ust-mode"]:checked')?.value || 'openai';
        document.getElementById('ust-openai-fields').style.display = mode === 'openai' ? 'block' : 'none';
        document.getElementById('ust-api-fields').style.display = mode === 'api' ? 'block' : 'none';
        document.getElementById('ust-bridge-fields').style.display = mode === 'bridge' ? 'block' : 'none';
    }
    document.querySelectorAll('input[name="ust-mode"]').forEach(radio => {
        radio.addEventListener('change', updateModeFields);
    });

    // Event: Translate
    document.getElementById('ust-translate-btn').addEventListener('click', () => {
        handleStartTranslation();
    });

    // Event: Toggle subtitles
    document.getElementById('ust-toggle-btn').addEventListener('click', () => {
        overlayEnabled = !overlayEnabled;
        syncSubtitles();
        document.getElementById('ust-toggle-btn').textContent = overlayEnabled ? '👁️ Tắt' : '👁️ Bật';
    });

    // Init
    updateModeIndicator();
    checkConnectionStatus();

    return panelElement;
}

/**
 * Hiển main view, ẩn settings view
 */
function showMainView() {
    document.getElementById('ust-main-view').style.display = 'block';
    document.getElementById('ust-settings-view').style.display = 'none';
    updateModeIndicator();
    checkConnectionStatus();
    updateVttStatus();
}

/**
 * Hiển settings view, ẩn main view, fill values
 */
function showSettingsView() {
    document.getElementById('ust-main-view').style.display = 'none';
    document.getElementById('ust-settings-view').style.display = 'block';

    // Fill current settings
    document.getElementById('ust-mode-openai').checked = currentSettings.translationMode === 'openai';
    document.getElementById('ust-mode-api').checked = currentSettings.translationMode === 'api';
    document.getElementById('ust-mode-bridge').checked = currentSettings.translationMode === 'bridge';
    document.getElementById('ust-api-key').value = currentSettings.apiKey || '';
    document.getElementById('ust-api-model').value = currentSettings.apiModel || 'deepseek-chat';
    document.getElementById('ust-openai-url').value = currentSettings.openaiUrl || 'https://nano-gpt.com/api';
    document.getElementById('ust-openai-key').value = currentSettings.openaiKey || '';
    document.getElementById('ust-bridge-url').value = currentSettings.bridgeUrl || DEFAULT_BRIDGE_URL;

    // Toggle fields visibility
    const mode = currentSettings.translationMode;
    document.getElementById('ust-openai-fields').style.display = mode === 'openai' ? 'block' : 'none';
    document.getElementById('ust-api-fields').style.display = mode === 'api' ? 'block' : 'none';
    document.getElementById('ust-bridge-fields').style.display = mode === 'bridge' ? 'block' : 'none';

    // Clear info
    const info = document.getElementById('ust-settings-info');
    if (info) info.textContent = '';
}

/**
 * Update mode indicator text
 */
function updateModeIndicator() {
    const indicator = document.getElementById('ust-mode-indicator');
    if (!indicator) return;

    if (currentSettings.translationMode === 'openai') {
        indicator.textContent = '🤖 OpenAI — deepseek-v3.2';
        indicator.className = 'ust-mode-indicator ust-mode-api';
    } else if (currentSettings.translationMode === 'api') {
        indicator.textContent = `🚀 1min.ai — ${currentSettings.apiModel}`;
        indicator.className = 'ust-mode-indicator ust-mode-api';
    } else {
        indicator.textContent = '🌉 Bridge Mode';
        indicator.className = 'ust-mode-indicator ust-mode-bridge';
    }
}

/**
 * Check connection status based on mode
 */
async function checkConnectionStatus() {
    if (currentSettings.translationMode === 'openai') {
        await checkOpenAiStatus();
    } else if (currentSettings.translationMode === 'api') {
        await checkApiStatus();
    } else {
        await checkBridgeStatus();
    }
}

/**
 * Check OpenAI mode status
 */
async function checkOpenAiStatus() {
    const dot = document.getElementById('ust-bridge-dot');
    const label = document.getElementById('ust-bridge-label');
    if (!dot || !label) return false;

    if (currentSettings.openaiUrl) {
        dot.className = 'ust-dot ust-dot-ok';
        label.textContent = `OpenAI ✅ (${currentSettings.openaiUrl.substring(0, 30)})`;
        return true;
    } else {
        dot.className = 'ust-dot ust-dot-err';
        label.textContent = 'OpenAI URL ❌ (Mở Settings)';
        return false;
    }
}

/**
 * Check API mode status (just verify key exists)
 */
async function checkApiStatus() {
    const dot = document.getElementById('ust-bridge-dot');
    const label = document.getElementById('ust-bridge-label');
    if (!dot || !label) return false;

    if (currentSettings.apiKey) {
        dot.className = 'ust-dot ust-dot-ok';
        label.textContent = `API Key ✅ (${currentSettings.apiModel})`;
        return true;
    } else {
        dot.className = 'ust-dot ust-dot-err';
        label.textContent = 'API Key ❌ (Mở Settings để cấu hình)';
        return false;
    }
}

/**
 * Kiểm tra Bridge Server status
 */
async function checkBridgeStatus() {
    const dot = document.getElementById('ust-bridge-dot');
    const label = document.getElementById('ust-bridge-label');
    if (!dot || !label) return false;

    const bridgeUrl = currentSettings.bridgeUrl || DEFAULT_BRIDGE_URL;
    try {
        const res = await fetch(`${bridgeUrl}/api/status`, { signal: AbortSignal.timeout(3000) });
        const data = await res.json();

        dot.className = 'ust-dot ust-dot-ok';
        label.textContent = data.connected
            ? 'Bridge Server ✅ (CDP kết nối)'
            : 'Bridge Server ✅ (CDP chưa kết nối)';

        if (data.translating && data.progress) {
            showProgress(data.progress);
        }
        return true;
    } catch (e) {
        dot.className = 'ust-dot ust-dot-err';
        label.textContent = 'Bridge Server ❌ (Chưa chạy)';
        return false;
    }
}

/**
 * Cập nhật VTT status
 */
function updateVttStatus() {
    const dot = document.getElementById('ust-vtt-dot');
    const label = document.getElementById('ust-vtt-label');
    const translateBtn = document.getElementById('ust-translate-btn');
    if (!dot || !label) return;

    if (currentVttUrl) {
        dot.className = 'ust-dot ust-dot-detected';
        label.textContent = 'VTT đã phát hiện ✅';
        if (translateBtn && !isTranslating) translateBtn.disabled = false;
    } else {
        dot.className = 'ust-dot';
        label.textContent = 'VTT chưa phát hiện';
    }

    if (subtitles.length > 0) {
        const toggleBtn = document.getElementById('ust-toggle-btn');
        if (toggleBtn) {
            toggleBtn.disabled = false;
            toggleBtn.textContent = overlayEnabled ? '👁️ Tắt' : '👁️ Bật';
        }
        const info = document.getElementById('ust-info');
        if (info) {
            info.textContent = `${subtitles.length} phụ đề đã dịch`;
            info.className = 'ust-info ust-info-ok';
        }
    }
}

function showProgress(data) {
    const progress = document.getElementById('ust-progress');
    const label = document.getElementById('ust-progress-label');
    const fill = document.getElementById('ust-progress-fill');
    const text = document.getElementById('ust-progress-text');
    const translateBtn = document.getElementById('ust-translate-btn');
    if (!progress) return;

    progress.style.display = 'block';
    if (label) label.textContent = `Đang dịch chunk ${data.chunk}/${data.total}...`;
    if (fill) fill.style.width = `${data.percent}%`;
    if (text) text.textContent = `${data.percent}%`;
    if (translateBtn) {
        translateBtn.disabled = true;
        translateBtn.textContent = '⏳ Đang dịch...';
    }
}

function hideProgress() {
    const progress = document.getElementById('ust-progress');
    const translateBtn = document.getElementById('ust-translate-btn');
    if (progress) progress.style.display = 'none';
    if (translateBtn) {
        translateBtn.disabled = false;
        translateBtn.textContent = '🌐 Dịch Phụ Đề';
    }
}

// ==================== SUBTITLE OVERLAY ====================

// Position relative to video (0-1 ratios)
let overlayRelX = 0.5;   // Center horizontal
let overlayRelY = 0.85;  // Near bottom
let overlayDragged = false;

/**
 * Tạo overlay element với drag support (center-based)
 */
function createOverlay() {
    if (overlayElement) return overlayElement;

    overlayElement = document.createElement('div');
    overlayElement.id = 'ust-subtitle-overlay';
    overlayElement.className = 'ust-overlay';
    overlayElement.style.display = 'none';
    document.body.appendChild(overlayElement);

    // Load saved relative position
    try {
        const savedPos = localStorage.getItem('ust_overlay_pos');
        if (savedPos) {
            const pos = JSON.parse(savedPos);
            if (pos.relX != null) overlayRelX = pos.relX;
            if (pos.relY != null) overlayRelY = pos.relY;
            if (pos.width) overlayElement.style.width = pos.width;
            if (pos.height) overlayElement.style.height = pos.height;
            overlayDragged = true;
        }
    } catch (e) { }

    // Drag handlers — drag từ CENTER
    overlayElement.addEventListener('mousedown', (e) => {
        if (e.target !== overlayElement) return;
        e.preventDefault();
        overlayElement.classList.add('ust-dragging');

        // Offset từ center của overlay
        const rect = overlayElement.getBoundingClientRect();
        const centerX = rect.left + rect.width / 2;
        const centerY = rect.top + rect.height / 2;
        const offsetX = e.clientX - centerX;
        const offsetY = e.clientY - centerY;

        function onMouseMove(e) {
            // Tính center mới
            const newCenterX = e.clientX - offsetX;
            const newCenterY = e.clientY - offsetY;

            // Đặt left = center, dùng translateX(-50%) để căn giữa
            overlayElement.style.left = newCenterX + 'px';
            overlayElement.style.top = (newCenterY - overlayElement.offsetHeight / 2) + 'px';
            overlayElement.style.bottom = 'auto';
            overlayElement.style.transform = 'translateX(-50%)';
        }

        function onMouseUp(e) {
            overlayElement.classList.remove('ust-dragging');
            document.removeEventListener('mousemove', onMouseMove);
            document.removeEventListener('mouseup', onMouseUp);

            // Tính ratio relative to video
            if (videoElement) {
                const vRect = videoElement.getBoundingClientRect();
                const oRect = overlayElement.getBoundingClientRect();
                const oCenterX = oRect.left + oRect.width / 2;
                const oCenterY = oRect.top + oRect.height / 2;
                overlayRelX = (oCenterX - vRect.left) / vRect.width;
                overlayRelY = (oCenterY - vRect.top) / vRect.height;
            }

            overlayDragged = true;

            // Save ratio
            try {
                localStorage.setItem('ust_overlay_pos', JSON.stringify({
                    relX: overlayRelX,
                    relY: overlayRelY,
                    width: overlayElement.style.width || null,
                    height: overlayElement.style.height || null,
                }));
            } catch (e) { }
        }

        document.addEventListener('mousemove', onMouseMove);
        document.addEventListener('mouseup', onMouseUp);
    });

    return overlayElement;
}

/**
 * Binary search tìm subtitle active tại thời điểm hiện tại
 */
function findActiveSubtitle(time) {
    if (subtitles.length === 0) return null;

    let low = 0;
    let high = subtitles.length - 1;

    while (low <= high) {
        const mid = Math.floor((low + high) / 2);
        const sub = subtitles[mid];

        if (time >= sub.startTime && time <= sub.endTime) {
            return sub;
        } else if (time < sub.startTime) {
            high = mid - 1;
        } else {
            low = mid + 1;
        }
    }

    return null;
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
        positionOverlay();  // Luôn reposition theo video
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
        setTimeout(attachToVideo, 2000);
        return;
    }

    videoElement = video;
    createOverlay();

    // Listen cho timeupdate
    videoElement.addEventListener('timeupdate', syncSubtitles);

    // Reposition khi fullscreen/resize — LUÔN LUÔN (overlay follow video)
    document.addEventListener('fullscreenchange', () => {
        if (overlayElement) {
            const fsElement = document.fullscreenElement;
            if (fsElement) {
                // Entering fullscreen → move overlay vào fullscreen element
                fsElement.appendChild(overlayElement);
            } else {
                // Exiting fullscreen → move overlay về body
                document.body.appendChild(overlayElement);
            }
        }
        setTimeout(positionOverlay, 300);
    });
    window.addEventListener('resize', () => {
        requestAnimationFrame(positionOverlay);
    });

    console.log('[UST] Attached to video player');
}

/**
 * Position overlay relative to video using stored ratios
 * Luôn follow video dù đã drag hay chưa
 */
function positionOverlay() {
    if (!overlayElement || !videoElement) return;

    const vRect = videoElement.getBoundingClientRect();
    if (vRect.width === 0 || vRect.height === 0) return;

    // Tính center position dựa trên video rect + ratio
    const centerX = vRect.left + overlayRelX * vRect.width;
    const centerY = vRect.top + overlayRelY * vRect.height;

    overlayElement.style.left = centerX + 'px';
    overlayElement.style.top = (centerY - overlayElement.offsetHeight / 2) + 'px';
    overlayElement.style.bottom = 'auto';
    overlayElement.style.transform = 'translateX(-50%)';
}

// ==================== BRIDGE SERVER COMMUNICATION ====================

/**
 * Gửi VTT tới Bridge Server để dịch (SSE) — Bridge Mode
 */
async function translateVttBridge(vttContent, bridgeUrl) {
    console.log('[UST] Bắt đầu dịch VTT...');

    return new Promise((resolve, reject) => {
        // Dùng fetch + ReadableStream để đọc SSE
        fetch(`${bridgeUrl}/api/translate-vtt`, {
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
                            showProgress(parsed);
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

// Nhận messages từ background (VTT detected)
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
    if (message.type === 'VTT_DETECTED') {
        console.log(`[UST] VTT detected: ${message.url.substring(0, 80)}`);
        currentVttUrl = message.url;

        // Reset state khi phát hiện video mới
        subtitles = [];
        isTranslating = false;
        overlayEnabled = true;

        // Reset overlay
        if (overlayElement) overlayElement.style.display = 'none';

        // Re-attach video nếu video element bị thay (Udemy SPA)
        const currentVideo = document.querySelector('video');
        if (currentVideo && currentVideo !== videoElement) {
            console.log('[UST] Video element changed, re-attaching');
            if (videoElement) {
                videoElement.removeEventListener('timeupdate', syncSubtitles);
            }
            videoElement = null;
            attachToVideo();
        }

        // Reset panel UI
        const translateBtn = document.getElementById('ust-translate-btn');
        if (translateBtn) {
            translateBtn.disabled = false;
            translateBtn.textContent = '🌐 Dịch Phụ Đề';
        }
        const toggleBtn = document.getElementById('ust-toggle-btn');
        if (toggleBtn) {
            toggleBtn.disabled = true;
            toggleBtn.textContent = '👁️ Bật/Tắt';
        }
        const info = document.getElementById('ust-info');
        if (info) {
            info.textContent = '';
            info.className = 'ust-info';
        }
        hideProgress();

        updateVttStatus();

        // Show notification trên trigger button
        const trigger = document.getElementById('ust-trigger-btn');
        if (trigger) {
            trigger.classList.add('ust-trigger-notify');
            setTimeout(() => trigger.classList.remove('ust-trigger-notify'), 3000);
        }
        sendResponse({ received: true });
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

// ==================== PORT-BASED TRANSLATION ====================

/**
 * Mở port tới background → gửi task → nhận progress/complete/error
 * Port giữ service worker sống suốt quá trình dịch (fix MV3 30s timeout)
 */
function startTranslationPort(message) {
    const port = chrome.runtime.connect({ name: 'translate' });
    console.log('[UST] Translation port opened');

    port.onMessage.addListener((msg) => {
        if (msg.type === 'TRANSLATE_PROGRESS') {
            showProgress(msg.progress);
        }

        if (msg.type === 'TRANSLATE_COMPLETE') {
            console.log('[UST] ✅ Dịch xong qua port!');
            subtitles = parseVttToSubtitles(msg.translatedVtt);
            console.log(`[UST] Loaded ${subtitles.length} translated subtitles`);

            overlayEnabled = true;
            attachToVideo();
            positionOverlay();
            syncSubtitles();
            hideProgress();

            const info = document.getElementById('ust-info');
            if (info) {
                info.textContent = `✅ Dịch xong ${subtitles.length} phụ đề!`;
                info.className = 'ust-info ust-info-ok';
            }
            const toggleBtn = document.getElementById('ust-toggle-btn');
            if (toggleBtn) {
                toggleBtn.disabled = false;
                toggleBtn.textContent = '👁️ Tắt';
            }
            isTranslating = false;
            const translateBtn = document.getElementById('ust-translate-btn');
            if (translateBtn) {
                translateBtn.disabled = false;
                translateBtn.textContent = '🌐 Dịch Phụ Đề';
            }
        }

        if (msg.type === 'TRANSLATE_ERROR') {
            console.error('[UST] Translation error via port:', msg.error);
            hideProgress();
            const info = document.getElementById('ust-info');
            if (info) {
                info.textContent = `❌ ${msg.error}`;
                info.className = 'ust-info ust-info-err';
            }
            isTranslating = false;
            const translateBtn = document.getElementById('ust-translate-btn');
            if (translateBtn) {
                translateBtn.disabled = false;
                translateBtn.textContent = '🌐 Dịch Phụ Đề';
            }
        }
    });

    port.onDisconnect.addListener(() => {
        console.log('[UST] Translation port disconnected');
    });

    // Gửi task qua port
    port.postMessage(message);
}

/**
 * Xử lý bắt đầu dịch
 */
async function handleStartTranslation() {
    if (isTranslating) return;

    // Reload settings mỗi lần dịch (user có thể đã thay đổi)
    loadSettings();
    updateModeIndicator();

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
            const info = document.getElementById('ust-info');
            if (info) {
                info.textContent = 'Không tìm thấy phụ đề. Bật captions trong video player trước.';
                info.className = 'ust-info ust-info-err';
            }
            return;
        }
    }

    // Validation based on mode
    if (currentSettings.translationMode === 'openai') {
        if (!currentSettings.openaiUrl) {
            const info = document.getElementById('ust-info');
            if (info) {
                info.textContent = 'Chưa có Server URL! Mở Settings để cấu hình.';
                info.className = 'ust-info ust-info-err';
            }
            return;
        }
    } else if (currentSettings.translationMode === 'api') {
        if (!currentSettings.apiKey) {
            const info = document.getElementById('ust-info');
            if (info) {
                info.textContent = 'Chưa có API Key! Mở Settings để cấu hình.';
                info.className = 'ust-info ust-info-err';
            }
            return;
        }
    } else {
        const bridgeOk = await checkBridgeStatus();
        if (!bridgeOk) {
            const info = document.getElementById('ust-info');
            if (info) {
                info.textContent = 'Chạy Bridge Server trước! (npm start)';
                info.className = 'ust-info ust-info-err';
            }
            return;
        }
    }

    isTranslating = true;
    const translateBtn = document.getElementById('ust-translate-btn');
    if (translateBtn) {
        translateBtn.disabled = true;
        translateBtn.textContent = '⏳ Đang dịch...';
    }

    // Mở panel nếu chưa mở
    if (panelElement) panelElement.classList.add('ust-panel-visible');

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

        if (currentSettings.translationMode === 'openai') {
            // === OPENAI MODE: qua port ===
            console.log('[UST] Using OpenAI mode via port');
            startTranslationPort({
                type: 'TRANSLATE_OPENAI',
                vttContent,
                settings: currentSettings,
            });
            return;
        }

        if (currentSettings.translationMode === 'api') {
            // === API MODE: qua port ===
            console.log('[UST] Using API mode via port');
            startTranslationPort({
                type: 'TRANSLATE_API',
                vttContent,
                settings: currentSettings,
            });
            return;
        }

        // === BRIDGE MODE: qua port ===
        console.log('[UST] Using Bridge mode via port');
        const bridgeUrl = currentSettings.bridgeUrl || DEFAULT_BRIDGE_URL;
        startTranslationPort({
            type: 'TRANSLATE_BRIDGE',
            vttContent,
            bridgeUrl,
        });
        return;

    } catch (err) {
        console.error('[UST] Translation error:', err);
        hideProgress();
        const info = document.getElementById('ust-info');
        if (info) {
            info.textContent = `❌ ${err.message}`;
            info.className = 'ust-info ust-info-err';
        }
        isTranslating = false;
        const translateBtn = document.getElementById('ust-translate-btn');
        if (translateBtn) {
            translateBtn.disabled = false;
            translateBtn.textContent = '🌐 Dịch Phụ Đề';
        }
    }
}

// ==================== INIT ====================

// Attach to video when page loads
attachToVideo();

// Create translate panel
createTranslatePanel();

// Observe DOM changes (Udemy SPA — video element có thể load sau)
const observer = new MutationObserver(() => {
    if (!videoElement) attachToVideo();
});
observer.observe(document.body, { childList: true, subtree: true });

// ==================== AUTO-LOAD SUBTITLES ON SPA NAVIGATION ====================

let lastUrl = window.location.href;

/**
 * Phát hiện Udemy SPA navigation — MutationObserver + polling fallback
 * (monkey-patch history không work vì content script chạy trong isolated world)
 */
function watchUrlChanges() {
    const check = () => {
        if (location.href !== lastUrl) {
            lastUrl = location.href;
            onUrlChange();
        }
    };

    // MutationObserver: DOM thay đổi → check URL (bắt nhanh nhất)
    new MutationObserver(check).observe(document.body, {
        subtree: true, childList: true,
    });

    // Polling 500ms: fallback đáng tin cậy
    setInterval(check, 500);

    // Back/forward
    window.addEventListener('popstate', check);
}

function onUrlChange() {
    console.log('urlChange');
    const newUrl = window.location.href;
    if (newUrl === lastUrl) return;

    // Chỉ trigger khi chuyển video (URL chứa /lecture/)
    const isLecture = newUrl.includes('/lecture/') || newUrl.includes('/learn/');
    if (!isLecture) {
        lastUrl = newUrl;
        return;
    }

    console.log(`[UST] URL changed → ${newUrl.substring(0, 80)}`);
    lastUrl = newUrl;

    // Reset video element (SPA thường tạo video mới)
    if (videoElement) {
        videoElement.removeEventListener('timeupdate', syncSubtitles);
        videoElement = null;
    }

    // Delay để Udemy load video player
    setTimeout(() => {
        attachToVideo();
        autoLoadSubtitle();
    }, 2500);
}

/**
 * Tự động click "Tiếng Anh" trong CC menu → trigger VTT download → click "Tắt"
 */
async function autoLoadSubtitle() {
    console.log('[UST] Auto-loading subtitle...');

    // Bước 1: Tìm nút CC trên video player để mở menu
    const ccButton = document.querySelector('[data-purpose="captions-dropdown-button"]')
        || document.querySelector('button[aria-label*="aption"]')
        || document.querySelector('button[aria-label*="ubtitle"]');

    if (ccButton) {
        ccButton.click();
        console.log('[UST] Clicked CC button');
        await sleep(800);
    }

    // Bước 2: Tìm button "Tiếng Anh" trong danh sách
    let englishBtn = null;
    const buttons = document.querySelectorAll('.ud-popper-open .ud-unstyled-list li button');

    for (const btn of buttons) {
        const text = (btn.textContent || btn.innerText || '').toLowerCase().trim();
        if (text.includes('tiếng anh') || text.includes('english')) {
            englishBtn = btn;
            break;
        }
    }

    if (englishBtn) {
        englishBtn.click();
        console.log('[UST] ✅ Clicked "Tiếng Anh" subtitle');

        // Bước 3: Đợi VTT load, rồi tắt subtitle gốc
        await sleep(1500);

        // Tìm nút "Tắt" 
        if (ccButton) ccButton.click(); // Mở lại menu
        await sleep(500);

        const allButtons = document.querySelectorAll('.ud-popper-open .ud-unstyled-list li button');
        for (const btn of allButtons) {
            const text = (btn.textContent || btn.innerText || '').toLowerCase().trim();
            if (text === 'tắt' || text === 'off') {
                btn.click();
                console.log('[UST] ✅ Clicked "Tắt" — ẩn subtitle gốc');
                break;
            }
        }
    } else {
        console.log('[UST] ⚠️ Không tìm thấy button "Tiếng Anh"');
        // Đóng menu nếu đã mở
        if (ccButton) ccButton.click();
    }
}

function sleep(ms) {
    return new Promise(r => setTimeout(r, ms));
}

// Start watching
watchUrlChanges();

console.log('[UST] Content script loaded on:', window.location.href);
