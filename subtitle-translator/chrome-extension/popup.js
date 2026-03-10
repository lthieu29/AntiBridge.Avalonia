/**
 * Popup Script - Logic cho extension popup
 */

const BRIDGE_URL = 'http://localhost:3000';

// DOM Elements
const bridgeStatus = document.getElementById('bridgeStatus');
const bridgeLabel = document.getElementById('bridgeLabel');
const vttStatus = document.getElementById('vttStatus');
const vttLabel = document.getElementById('vttLabel');
const progressSection = document.getElementById('progressSection');
const progressLabel = document.getElementById('progressLabel');
const progressFill = document.getElementById('progressFill');
const progressText = document.getElementById('progressText');
const translateBtn = document.getElementById('translateBtn');
const toggleBtn = document.getElementById('toggleBtn');
const infoText = document.getElementById('infoText');

// ==================== STATUS CHECK ====================

async function checkBridgeStatus() {
    try {
        const res = await fetch(`${BRIDGE_URL}/api/status`, { signal: AbortSignal.timeout(3000) });
        const data = await res.json();

        bridgeStatus.className = 'status-dot connected';
        bridgeLabel.textContent = data.connected
            ? 'Bridge Server ✅ (CDP kết nối)'
            : 'Bridge Server ✅ (CDP chưa kết nối)';

        if (data.translating && data.progress) {
            showProgress(data.progress);
        }

        return true;
    } catch (e) {
        bridgeStatus.className = 'status-dot disconnected';
        bridgeLabel.textContent = 'Bridge Server ❌ (Chưa chạy)';
        return false;
    }
}

async function checkContentState() {
    try {
        const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
        if (!tabs[0]) return;

        const response = await chrome.tabs.sendMessage(tabs[0].id, { type: 'GET_STATE' });

        if (response.hasVtt) {
            vttStatus.className = 'status-dot detected';
            vttLabel.textContent = 'VTT đã phát hiện ✅';
            translateBtn.disabled = false;
        } else {
            vttStatus.className = 'status-dot';
            vttLabel.textContent = 'VTT chưa phát hiện';
        }

        if (response.hasSubtitles) {
            toggleBtn.disabled = false;
            toggleBtn.textContent = response.overlayEnabled ? '👁️ Tắt' : '👁️ Bật';
            infoText.textContent = `${response.subtitleCount} phụ đề đã dịch`;
            infoText.className = 'info success';
        }
    } catch (e) {
        // Content script chưa inject (không phải trang Udemy)
        vttLabel.textContent = 'Không phải trang Udemy';
    }
}

// ==================== PROGRESS ====================

function showProgress(data) {
    progressSection.style.display = 'block';
    progressLabel.textContent = `Đang dịch chunk ${data.chunk}/${data.total}...`;
    progressFill.style.width = `${data.percent}%`;
    progressText.textContent = `${data.percent}%`;
    translateBtn.disabled = true;
    translateBtn.textContent = '⏳ Đang dịch...';
}

function hideProgress() {
    progressSection.style.display = 'none';
    translateBtn.disabled = false;
    translateBtn.textContent = '🌐 Dịch Phụ Đề';
}

// ==================== ACTIONS ====================

translateBtn.addEventListener('click', async () => {
    const bridgeOk = await checkBridgeStatus();
    if (!bridgeOk) {
        infoText.textContent = 'Chạy Bridge Server trước!';
        infoText.className = 'info error';
        return;
    }

    translateBtn.disabled = true;
    translateBtn.textContent = '⏳ Đang dịch...';
    progressSection.style.display = 'block';
    infoText.textContent = '';

    try {
        const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
        if (tabs[0]) {
            chrome.tabs.sendMessage(tabs[0].id, { type: 'START_TRANSLATION' });
        }
    } catch (e) {
        infoText.textContent = 'Lỗi: ' + e.message;
        infoText.className = 'info error';
        hideProgress();
    }
});

toggleBtn.addEventListener('click', async () => {
    try {
        const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
        if (tabs[0]) {
            const response = await chrome.tabs.sendMessage(tabs[0].id, { type: 'TOGGLE_SUBTITLES' });
            toggleBtn.textContent = response.enabled ? '👁️ Tắt' : '👁️ Bật';
        }
    } catch (e) {
        infoText.textContent = 'Lỗi toggle';
        infoText.className = 'info error';
    }
});

// ==================== MESSAGE LISTENER ====================

chrome.runtime.onMessage.addListener((message) => {
    if (message.type === 'TRANSLATION_PROGRESS') {
        showProgress(message);
    }

    if (message.type === 'TRANSLATION_COMPLETE') {
        hideProgress();
        infoText.textContent = `✅ Dịch xong ${message.count} phụ đề!`;
        infoText.className = 'info success';
        toggleBtn.disabled = false;
        toggleBtn.textContent = '👁️ Tắt';
    }

    if (message.type === 'TRANSLATION_ERROR') {
        hideProgress();
        infoText.textContent = `❌ ${message.message}`;
        infoText.className = 'info error';
    }
});

// ==================== INIT ====================

(async () => {
    await checkBridgeStatus();
    await checkContentState();
})();
