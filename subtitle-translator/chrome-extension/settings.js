/**
 * Settings Page Logic — Load/Save chrome.storage.local
 */

const DEFAULTS = {
    translationMode: 'api',
    apiKey: '',
    apiModel: 'deepseek-chat',
    chunkSize: 20,
    bridgeUrl: 'http://localhost:3000',
};

// ==================== DOM Elements ====================

const modeApi = document.getElementById('modeApi');
const modeBridge = document.getElementById('modeBridge');
const apiSettingsSection = document.getElementById('apiSettingsSection');
const bridgeSettingsSection = document.getElementById('bridgeSettingsSection');
const apiKeyInput = document.getElementById('apiKey');
const apiModelSelect = document.getElementById('apiModel');
const chunkSizeInput = document.getElementById('chunkSize');
const bridgeUrlInput = document.getElementById('bridgeUrl');
const saveBtn = document.getElementById('saveBtn');
const resetBtn = document.getElementById('resetBtn');
const toggleApiKeyBtn = document.getElementById('toggleApiKey');
const toast = document.getElementById('toast');

// ==================== Mode Switching ====================

function updateSectionVisibility() {
    const isApi = modeApi.checked;
    apiSettingsSection.style.display = isApi ? 'block' : 'none';
    bridgeSettingsSection.style.display = isApi ? 'none' : 'block';
}

modeApi.addEventListener('change', updateSectionVisibility);
modeBridge.addEventListener('change', updateSectionVisibility);

// ==================== Toggle API Key Visibility ====================

toggleApiKeyBtn.addEventListener('click', () => {
    const isPassword = apiKeyInput.type === 'password';
    apiKeyInput.type = isPassword ? 'text' : 'password';
    toggleApiKeyBtn.textContent = isPassword ? '🙈' : '👁️';
});

// ==================== Load Settings ====================

async function loadSettings() {
    const data = await chrome.storage.local.get(DEFAULTS);

    // Mode
    if (data.translationMode === 'bridge') {
        modeBridge.checked = true;
    } else {
        modeApi.checked = true;
    }
    updateSectionVisibility();

    // API Settings
    apiKeyInput.value = data.apiKey || '';
    apiModelSelect.value = data.apiModel || DEFAULTS.apiModel;
    chunkSizeInput.value = data.chunkSize || DEFAULTS.chunkSize;

    // Bridge Settings
    bridgeUrlInput.value = data.bridgeUrl || DEFAULTS.bridgeUrl;
}

// ==================== Save Settings ====================

saveBtn.addEventListener('click', async () => {
    const settings = {
        translationMode: modeApi.checked ? 'api' : 'bridge',
        apiKey: apiKeyInput.value.trim(),
        apiModel: apiModelSelect.value,
        chunkSize: parseInt(chunkSizeInput.value) || DEFAULTS.chunkSize,
        bridgeUrl: bridgeUrlInput.value.trim() || DEFAULTS.bridgeUrl,
    };

    // Validation
    if (settings.translationMode === 'api' && !settings.apiKey) {
        showToast('⚠️ Vui lòng nhập API Key', 'warning');
        apiKeyInput.focus();
        return;
    }

    if (settings.chunkSize < 5 || settings.chunkSize > 50) {
        showToast('⚠️ Chunk size phải từ 5-50', 'warning');
        chunkSizeInput.focus();
        return;
    }

    await chrome.storage.local.set(settings);
    showToast('✅ Đã lưu settings!', 'success');
});

// ==================== Reset ====================

resetBtn.addEventListener('click', async () => {
    await chrome.storage.local.set(DEFAULTS);
    await loadSettings();
    showToast('🔄 Đã reset về mặc định', 'info');
});

// ==================== Toast ====================

function showToast(message, type = 'info') {
    toast.textContent = message;
    toast.className = `toast toast-${type} toast-visible`;
    setTimeout(() => {
        toast.classList.remove('toast-visible');
    }, 2500);
}

// ==================== Init ====================

loadSettings();
