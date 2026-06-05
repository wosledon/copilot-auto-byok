// AutoCopilot helper - most logic is now in config.js
// This file handles connection info display and code tab switching

document.addEventListener('DOMContentLoaded', () => {
    // Update connection URL to current host
    const connUrl = document.getElementById('conn-url');
    if (connUrl) {
        connUrl.textContent = window.location.origin;
    }

    // Code tab switching
    const codeTabs = document.querySelectorAll('.code-tab');
    codeTabs.forEach(tab => {
        tab.addEventListener('click', () => {
            codeTabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            const lang = tab.dataset.lang;
            document.getElementById('code-openai').style.display = lang === 'openai' ? 'block' : 'none';
            document.getElementById('code-anthropic').style.display = lang === 'anthropic' ? 'block' : 'none';
        });
    });

    // Update binding display
    updateBindingDisplay();

    // Update BYOK env values
    updateByokValues();
});

async function updateBindingDisplay() {
    try {
        const res = await fetch('/api/autocopilot');
        const binding = await res.json();
        const el = document.getElementById('conn-binding');
        const dot = document.getElementById('binding-dot');
        if (el && binding.currentModel) {
            el.textContent = `${binding.currentModel}`;
            if (dot) dot.classList.add('active');
        } else {
            if (dot) dot.classList.remove('active');
        }
    } catch {
        // ignore
    }
}

// Copy text helper
function copyText(text) {
    navigator.clipboard.writeText(text).then(() => {
        showToast('已复制到剪贴板');
    }).catch(() => {
        showToast('复制失败', 'error');
    });
}

// BYOK env copy
function copyByokEnv(format) {
    const baseUrl = document.getElementById('byok-base-url')?.textContent || window.location.origin + '/v1';
    const type = document.getElementById('byok-type')?.textContent || 'openai';
    const apiKey = document.getElementById('byok-api-key')?.textContent || 'YOUR_API_KEY';
    const model = document.getElementById('byok-model')?.textContent || 'auto-copilot';

    let text = '';
    if (format === 'powershell') {
        text = `$env:COPILOT_PROVIDER_BASE_URL = "${baseUrl}"\n` +
               `$env:COPILOT_PROVIDER_TYPE = "${type}"\n` +
               `$env:COPILOT_PROVIDER_API_KEY = "${apiKey}"\n` +
               `$env:COPILOT_MODEL = "${model}"`;
    } else if (format === 'bash') {
        text = `export COPILOT_PROVIDER_BASE_URL="${baseUrl}"\n` +
               `export COPILOT_PROVIDER_TYPE="${type}"\n` +
               `export COPILOT_PROVIDER_API_KEY="${apiKey}"\n` +
               `export COPILOT_MODEL="${model}"`;
    } else {
        text = `COPILOT_PROVIDER_BASE_URL=${baseUrl}\n` +
               `COPILOT_PROVIDER_TYPE=${type}\n` +
               `COPILOT_PROVIDER_API_KEY=${apiKey}\n` +
               `COPILOT_MODEL=${model}`;
    }

    navigator.clipboard.writeText(text).then(() => {
        showToast('环境变量已复制到剪贴板');
    }).catch(() => {
        showToast('复制失败', 'error');
    });
}

// Update BYOK env values from current config
function updateByokValues() {
    const baseUrl = window.location.origin + '/v1';
    const baseUrlEl = document.getElementById('byok-base-url');
    if (baseUrlEl) baseUrlEl.textContent = baseUrl;
}
