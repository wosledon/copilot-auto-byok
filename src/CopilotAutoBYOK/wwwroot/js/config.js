// ===== Global State =====
const API_BASE = '/api';
let config = null;
let providers = [];
let autocopilot = null;
let apiKeys = [];
let byokEnv = null;

// ===== Initialization =====
document.addEventListener('DOMContentLoaded', () => {
    loadConfig();
    setupNavigation();
});

async function loadConfig() {
    try {
        const [configRes, providersRes, autocopilotRes, keysRes, byokRes] = await Promise.all([
            fetch(`${API_BASE}/config`),
            fetch(`${API_BASE}/providers`),
            fetch(`${API_BASE}/autocopilot`),
            fetch(`${API_BASE}/keys`),
            fetch(`${API_BASE}/byok`)
        ]);

        config = await configRes.json();
        providers = await providersRes.json();
        autocopilot = await autocopilotRes.json();
        apiKeys = await keysRes.json();
        byokEnv = await byokRes.json();

        renderProviders();
        renderAutoCopilot();
        renderApiKeys();
        renderConnectionInfo();
        renderByokForm();
    } catch (err) {
        showToast('加载配置失败: ' + err.message, 'error');
    }
}

// ===== Navigation =====
function setupNavigation() {
    const navItems = document.querySelectorAll('.nav-item');
    const tabs = document.querySelectorAll('.tab-content');

    navItems.forEach(item => {
        item.addEventListener('click', () => {
            const tab = item.dataset.tab;
            navItems.forEach(n => n.classList.remove('active'));
            tabs.forEach(t => t.classList.remove('active'));
            item.classList.add('active');
            document.getElementById(tab).classList.add('active');
        });
    });
}

// ===== Provider Management =====
function renderProviders() {
    const openaiGrid = document.getElementById('openaiProvidersGrid');
    const anthropicGrid = document.getElementById('anthropicProvidersGrid');
    const openaiEmpty = document.getElementById('openaiProvidersEmpty');
    const anthropicEmpty = document.getElementById('anthropicProvidersEmpty');

    const openaiList = providers.filter(p => p.type === 'openai');
    const anthropicList = providers.filter(p => p.type === 'anthropic');

    if (openaiGrid) {
        if (openaiList.length === 0) {
            openaiGrid.innerHTML = '';
            if (openaiEmpty) openaiEmpty.style.display = 'block';
        } else {
            if (openaiEmpty) openaiEmpty.style.display = 'none';
            openaiGrid.innerHTML = openaiList.map(p => renderProviderCard(p)).join('');
        }
    }

    if (anthropicGrid) {
        if (anthropicList.length === 0) {
            anthropicGrid.innerHTML = '';
            if (anthropicEmpty) anthropicEmpty.style.display = 'block';
        } else {
            if (anthropicEmpty) anthropicEmpty.style.display = 'none';
            anthropicGrid.innerHTML = anthropicList.map(p => renderProviderCard(p)).join('');
        }
    }
}

function renderProviderCard(p) {
    const visibleSet = new Set(p.visibleModels || []);
    const allModels = p.models || [];
    const visibleModels = allModels.filter(m => visibleSet.has(m));
    const hiddenCount = allModels.length - visibleModels.length;

    return `
        <div class="provider-card" data-id="${p.id}">
            <div class="provider-card-header">
                <h3>${escapeHtml(p.name)}</h3>
                <span class="provider-type-badge ${p.type}">
                    ${p.type === 'openai' ? '🟢 OpenAI' : '🟣 Anthropic'}
                </span>
            </div>
            <div class="provider-card-body">
                <div class="info-row">
                    <span class="info-label">基础 URL</span>
                    <span class="info-value" title="${escapeHtml(p.baseUrl)}">${escapeHtml(p.baseUrl)}</span>
                </div>
                <div class="info-row">
                    <span class="info-label">API 密钥</span>
                    <span class="info-value api-key-value" data-key="${escapeHtml(p.apiKey)}">
                        <span class="key-masked">${maskKey(p.apiKey)}</span>
                        <span class="key-full" style="display:none">${escapeHtml(p.apiKey)}</span>
                        <button class="btn-icon key-toggle" onclick="toggleKeyVisibility(this)" title="显示/隐藏">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px">
                                <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                            </svg>
                        </button>
                        <button class="btn-icon key-copy" onclick="copyKey(this)" title="复制">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px">
                                <rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>
                            </svg>
                        </button>
                    </span>
                </div>
                ${p.description ? `<div class="info-row"><span class="info-label">描述</span><span class="info-value">${escapeHtml(p.description)}</span></div>` : ''}
                <div class="provider-models-section">
                    <div class="models-header">
                        <span class="models-label">可见模型 (${visibleModels.length}${hiddenCount > 0 ? ` / ${allModels.length}` : ''})</span>
                    </div>
                    <div class="provider-models-list">
                        ${visibleModels.map(m => `<span class="model-tag active">${escapeHtml(m)}</span>`).join('')}
                        ${hiddenCount > 0 ? `<span class="model-tag hidden">+${hiddenCount} 隐藏</span>` : ''}
                    </div>
                </div>
            </div>
            <div class="provider-card-footer">
                <button class="btn btn-secondary btn-sm" onclick="editProvider('${p.id}')">编辑</button>
                <button class="btn btn-danger btn-sm" onclick="deleteProvider('${p.id}')">删除</button>
            </div>
        </div>
    `;
}

function maskKey(key) {
    if (!key || key.length < 10) return '***';
    return key.substring(0, 6) + '...' + key.substring(key.length - 4);
}

function toggleKeyVisibility(btn) {
    const container = btn.closest('.api-key-value');
    if (container) {
        const masked = container.querySelector('.key-masked');
        const full = container.querySelector('.key-full');
        const isHidden = full.style.display === 'none';

        if (isHidden) {
            masked.style.display = 'none';
            full.style.display = 'inline';
            btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>`;
            btn.title = '隐藏';
        } else {
            masked.style.display = 'inline';
            full.style.display = 'none';
            btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>`;
            btn.title = '显示';
        }
        return;
    }

    // For password input in dialog
    const input = btn.previousElementSibling;
    if (input && input.type === 'password') {
        input.type = 'text';
        btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:16px;height:16px"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/></svg>`;
        btn.title = '隐藏';
    } else if (input) {
        input.type = 'password';
        btn.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:16px;height:16px"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>`;
        btn.title = '显示';
    }
}

function copyKey(btn) {
    const container = btn.closest('.api-key-value');
    const full = container.querySelector('.key-full');
    const text = full.textContent || '';
    navigator.clipboard.writeText(text).then(() => {
        showToast('已复制到剪贴板');
    }).catch(() => {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
        showToast('已复制到剪贴板');
    });
}

function showAddProviderDialog() {
    const overlay = document.createElement('div');
    overlay.className = 'dialog-overlay';
    overlay.innerHTML = `
        <div class="dialog dialog-wide">
            <div class="dialog-header">
                <h3>添加模型提供商</h3>
                <button class="dialog-close" onclick="this.closest('.dialog-overlay').remove()">✕</button>
            </div>
            <div class="dialog-body">
                <div class="form-group">
                    <label>类型</label>
                    <div class="radio-group">
                        <label class="radio-card">
                            <input type="radio" name="providerType" value="openai" checked onchange="onProviderTypeChange(this)">
                            <div class="radio-content">
                                <span class="radio-icon">🟢</span>
                                <span>OpenAI 兼容</span>
                            </div>
                        </label>
                        <label class="radio-card">
                            <input type="radio" name="providerType" value="anthropic" onchange="onProviderTypeChange(this)">
                            <div class="radio-content">
                                <span class="radio-icon">🟣</span>
                                <span>Anthropic</span>
                            </div>
                        </label>
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>名称 <span class="required">*</span></label>
                        <input type="text" class="providerName" placeholder="例如：我的 OpenAI" autofocus>
                    </div>
                    <div class="form-group">
                        <label>基础 URL <span class="required">*</span></label>
                        <input type="text" class="providerBaseUrl" placeholder="https://api.openai.com/v1">
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group" style="flex:2">
                        <label>API 密钥 <span class="required">*</span></label>
                        <div class="input-with-icon">
                            <input type="password" class="providerApiKey" placeholder="sk-...">
                            <button type="button" class="btn-icon" onclick="togglePasswordVisibility(this)" title="显示/隐藏">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:16px;height:16px">
                                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                                </svg>
                            </button>
                        </div>
                    </div>
                    <div class="form-group" style="flex:1">
                        <label>&nbsp;</label>
                        <button class="btn btn-secondary" onclick="fetchModelsFromProvider(this)" style="width:100%">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px;display:inline;vertical-align:middle;margin-right:4px">
                                <polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>
                            </svg>
                            获取模型列表
                        </button>
                    </div>
                </div>
                <div class="form-group">
                    <label>模型列表</label>
                    <div id="fetchedModelsContainer" class="fetched-models-container" style="display:none">
                        <p class="models-hint">勾选要在工作台显示的模型：</p>
                        <div class="fetched-models-list" id="fetchedModelsList"></div>
                    </div>
                    <input type="text" class="providerModels" placeholder="手动输入模型，逗号分隔（如自动获取失败）" oninput="onManualModelsInput(this)">
                </div>
                <div class="form-group">
                    <label>描述（可选）</label>
                    <textarea class="providerDescription" placeholder="备注信息"></textarea>
                </div>
            </div>
            <div class="dialog-footer">
                <button class="btn btn-secondary" onclick="this.closest('.dialog-overlay').remove()">取消</button>
                <button class="btn btn-primary" onclick="saveNewProvider(this)">保存</button>
            </div>
        </div>
    `;
    document.body.appendChild(overlay);
    overlay.querySelector('.providerName').focus();
}

function onProviderTypeChange(radio) {
    const dialog = radio.closest('.dialog');
    const baseUrlInput = dialog.querySelector('.providerBaseUrl');
    if (!baseUrlInput.value) {
        baseUrlInput.value = radio.value === 'openai' ? 'https://api.openai.com/v1' : 'https://api.anthropic.com';
    }
}

async function fetchModelsFromProvider(btn) {
    const dialog = btn.closest('.dialog');
    const baseUrl = dialog.querySelector('.providerBaseUrl').value.trim();
    const apiKey = dialog.querySelector('.providerApiKey').value.trim();

    if (!baseUrl || !apiKey) {
        showToast('请填写基础 URL 和 API 密钥', 'error');
        return;
    }

    btn.disabled = true;
    btn.innerHTML = `<span style="display:inline-block;width:14px;height:14px;border:2px solid currentColor;border-right-color:transparent;border-radius:50%;animation:spin 0.6s linear infinite;vertical-align:middle;margin-right:4px"></span>获取中...`;

    try {
        const res = await fetch(`${API_BASE}/providers/fetch-models`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ baseUrl, apiKey })
        });

        const data = await res.json();
        const container = dialog.querySelector('#fetchedModelsContainer');
        const list = dialog.querySelector('#fetchedModelsList');
        const modelsInput = dialog.querySelector('.providerModels');

        if (data.models && data.models.length > 0) {
            list.innerHTML = data.models.map(m => `
                <label class="model-checkbox">
                    <input type="checkbox" value="${escapeHtml(m)}" checked>
                    <span class="model-name">${escapeHtml(m)}</span>
                </label>
            `).join('');
            container.style.display = 'block';
            modelsInput.value = data.models.join(', ');
            showToast(`成功获取 ${data.models.length} 个模型`);
        } else if (data.error) {
            showToast(data.error, 'warning');
        } else {
            showToast('未获取到模型列表，请手动输入', 'warning');
        }
    } catch (err) {
        showToast('获取模型列表失败: ' + err.message, 'error');
    } finally {
        btn.disabled = false;
        btn.innerHTML = `
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px;display:inline;vertical-align:middle;margin-right:4px">
                <polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>
            </svg>
            获取模型列表
        `;
    }
}

function onManualModelsInput(input) {
    const dialog = input.closest('.dialog');
    const container = dialog.querySelector('#fetchedModelsContainer');
    const list = dialog.querySelector('#fetchedModelsList');
    const models = input.value.split(',').map(s => s.trim()).filter(Boolean);

    if (models.length > 0) {
        list.innerHTML = models.map(m => `
            <label class="model-checkbox">
                <input type="checkbox" value="${escapeHtml(m)}" checked>
                <span class="checkmark"></span>
                <span class="model-name">${escapeHtml(m)}</span>
            </label>
        `).join('');
        container.style.display = 'block';
    } else {
        container.style.display = 'none';
    }
}

function getSelectedModels(dialog) {
    const checkboxes = dialog.querySelectorAll('#fetchedModelsList input[type="checkbox"]:checked');
    if (checkboxes.length > 0) {
        return Array.from(checkboxes).map(cb => cb.value);
    }
    const manualInput = dialog.querySelector('.providerModels');
    return manualInput.value.split(',').map(s => s.trim()).filter(Boolean);
}

function getAllModels(dialog) {
    const checkboxes = dialog.querySelectorAll('#fetchedModelsList input[type="checkbox"]');
    if (checkboxes.length > 0) {
        return Array.from(checkboxes).map(cb => cb.value);
    }
    const manualInput = dialog.querySelector('.providerModels');
    return manualInput.value.split(',').map(s => s.trim()).filter(Boolean);
}

async function saveNewProvider(btn) {
    const dialog = btn.closest('.dialog');
    const type = dialog.querySelector('input[name="providerType"]:checked').value;
    const name = dialog.querySelector('.providerName').value.trim();
    const baseUrl = dialog.querySelector('.providerBaseUrl').value.trim();
    const apiKey = dialog.querySelector('.providerApiKey').value.trim();
    const allModels = getAllModels(dialog);
    const visibleModels = getSelectedModels(dialog);
    const description = dialog.querySelector('.providerDescription').value.trim();

    if (!name || !baseUrl || !apiKey) {
        showToast('请填写必填字段', 'error');
        return;
    }

    if (allModels.length === 0) {
        showToast('请至少配置一个模型', 'error');
        return;
    }

    const provider = { type, name, baseUrl, apiKey, models: allModels, visibleModels, description };

    try {
        const res = await fetch(`${API_BASE}/providers`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(provider)
        });

        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.error || '保存失败');
        }

        showToast('提供商添加成功');
        dialog.closest('.dialog-overlay').remove();
        loadConfig();
    } catch (err) {
        showToast('保存失败: ' + err.message, 'error');
    }
}

function editProvider(id) {
    const p = providers.find(x => x.id === id);
    if (!p) return;

    const allModels = p.models || [];
    const visibleSet = new Set(p.visibleModels || []);

    const overlay = document.createElement('div');
    overlay.className = 'dialog-overlay';
    overlay.innerHTML = `
        <div class="dialog dialog-wide">
            <div class="dialog-header">
                <h3>编辑提供商</h3>
                <button class="dialog-close" onclick="this.closest('.dialog-overlay').remove()">✕</button>
            </div>
            <div class="dialog-body">
                <div class="form-group">
                    <label>类型</label>
                    <div class="radio-group">
                        <label class="radio-card">
                            <input type="radio" name="providerType" value="openai" ${p.type === 'openai' ? 'checked' : ''}>
                            <div class="radio-content">
                                <span class="radio-icon">🟢</span>
                                <span>OpenAI 兼容</span>
                            </div>
                        </label>
                        <label class="radio-card">
                            <input type="radio" name="providerType" value="anthropic" ${p.type === 'anthropic' ? 'checked' : ''}>
                            <div class="radio-content">
                                <span class="radio-icon">🟣</span>
                                <span>Anthropic</span>
                            </div>
                        </label>
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group">
                        <label>名称</label>
                        <input type="text" class="providerName" value="${escapeHtml(p.name)}">
                    </div>
                    <div class="form-group">
                        <label>基础 URL</label>
                        <input type="text" class="providerBaseUrl" value="${escapeHtml(p.baseUrl)}">
                    </div>
                </div>
                <div class="form-row">
                    <div class="form-group" style="flex:2">
                        <label>API 密钥（留空则不修改）</label>
                        <div class="input-with-icon">
                            <input type="password" class="providerApiKey" value="${escapeHtml(p.apiKey)}" placeholder="留空保持不变">
                            <button type="button" class="btn-icon" onclick="togglePasswordVisibility(this)" title="显示/隐藏">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:16px;height:16px">
                                    <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                                </svg>
                            </button>
                        </div>
                    </div>
                    <div class="form-group" style="flex:1">
                        <label>&nbsp;</label>
                        <button class="btn btn-secondary" onclick="fetchModelsFromProvider(this)" style="width:100%">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:14px;height:14px;display:inline;vertical-align:middle;margin-right:4px">
                                <polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>
                            </svg>
                            重新获取模型
                        </button>
                    </div>
                </div>
                <div class="form-group">
                    <label>模型可见性</label>
                    <div id="fetchedModelsContainer" class="fetched-models-container" style="display:${allModels.length > 0 ? 'block' : 'none'}">
                        <p class="models-hint">勾选要在工作台显示的模型：</p>
                        <div class="fetched-models-list" id="fetchedModelsList">
                            ${allModels.map(m => `
                                <label class="model-checkbox">
                                    <input type="checkbox" value="${escapeHtml(m)}" ${visibleSet.has(m) ? 'checked' : ''}>
                                    <span class="checkmark"></span>
                                    <span class="model-name">${escapeHtml(m)}</span>
                                </label>
                            `).join('')}
                        </div>
                    </div>
                    <input type="text" class="providerModels" value="${allModels.join(', ')}" placeholder="手动输入模型，逗号分隔" oninput="onManualModelsInput(this)">
                </div>
                <div class="form-group">
                    <label>描述（可选）</label>
                    <textarea class="providerDescription">${escapeHtml(p.description || '')}</textarea>
                </div>
            </div>
            <div class="dialog-footer">
                <button class="btn btn-secondary" onclick="this.closest('.dialog-overlay').remove()">取消</button>
                <button class="btn btn-primary" onclick="updateProvider(this, '${p.id}')">更新</button>
            </div>
        </div>
    `;
    document.body.appendChild(overlay);
}

async function updateProvider(btn, id) {
    const dialog = btn.closest('.dialog');
    const type = dialog.querySelector('input[name="providerType"]:checked').value;
    const name = dialog.querySelector('.providerName').value.trim();
    const baseUrl = dialog.querySelector('.providerBaseUrl').value.trim();
    const apiKey = dialog.querySelector('.providerApiKey').value.trim();
    const allModels = getAllModels(dialog);
    const visibleModels = getSelectedModels(dialog);
    const description = dialog.querySelector('.providerDescription').value.trim();

    if (!name || !baseUrl) {
        showToast('请填写必填字段', 'error');
        return;
    }

    const p = providers.find(x => x.id === id);
    const provider = {
        id, type, name, baseUrl,
        apiKey: apiKey || p.apiKey,
        models: allModels,
        visibleModels,
        description,
        createdAt: p.createdAt
    };

    try {
        const res = await fetch(`${API_BASE}/providers/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(provider)
        });

        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.error || '更新失败');
        }

        showToast('更新成功');
        dialog.closest('.dialog-overlay').remove();
        loadConfig();
    } catch (err) {
        showToast('更新失败: ' + err.message, 'error');
    }
}

async function deleteProvider(id) {
    if (!confirm('确定要删除此提供商吗？')) return;

    try {
        await fetch(`${API_BASE}/providers/${id}`, { method: 'DELETE' });
        showToast('已删除');
        loadConfig();
    } catch (err) {
        showToast('删除失败: ' + err.message, 'error');
    }
}

// ===== AutoCopilot =====
function renderAutoCopilot() {
    const select = document.getElementById('autocopilotModel');
    if (!select) return;

    let options = '<option value="">-- 选择模型 --</option>';
    for (const p of providers) {
        const visibleSet = new Set(p.visibleModels || p.models || []);
        for (const m of p.models || []) {
            if (!visibleSet.has(m)) continue;
            const selected = (autocopilot.currentModel === m && autocopilot.currentProviderId === p.id) ? 'selected' : '';
            options += `<option value="${m}" data-provider="${p.id}" ${selected}>${escapeHtml(m)} (${escapeHtml(p.name)})</option>`;
        }
    }
    select.innerHTML = options;
}

async function switchAutoCopilotModel() {
    const select = document.getElementById('autocopilotModel');
    const option = select.options[select.selectedIndex];
    const model = select.value;
    const providerId = option.dataset.provider || '';

    if (!model) {
        showToast('请选择一个模型', 'error');
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/autocopilot`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ currentModel: model, currentProviderId: providerId })
        });

        if (!res.ok) throw new Error('切换失败');

        const msg = document.getElementById('autocopilotStatus');
        const bindingEl = document.getElementById('conn-binding');
        const dot = document.getElementById('binding-dot');

        if (msg) {
            msg.textContent = '✓ 已切换到 ' + model;
            msg.className = 'status-msg success';
        }
        if (bindingEl) bindingEl.textContent = model;
        if (dot) dot.classList.add('active');

        showToast('已切换到 ' + model);
    } catch (err) {
        const msg = document.getElementById('autocopilotStatus');
        if (msg) {
            msg.textContent = '✗ 切换失败';
            msg.className = 'status-msg error';
        }
        showToast('切换失败', 'error');
    }
}

// ===== API Keys =====
function renderApiKeys() {
    const list = document.getElementById('keysList');
    if (!list) return;

    if (apiKeys.length === 0) {
        list.innerHTML = '<p class="empty-text">尚未配置 API 密钥</p>';
        return;
    }

    list.innerHTML = apiKeys.map(k => `
        <div class="key-item">
            <div class="key-info">
                <div class="key-name">${escapeHtml(k.name)}</div>
                <div class="key-meta">创建于 ${new Date(k.createdAt).toLocaleString('zh-CN')}</div>
            </div>
            <button class="btn btn-danger btn-sm" onclick="deleteApiKey('${k.id}')">删除</button>
        </div>
    `).join('');
}

function generateApiKey() {
    const chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789';
    let result = 'sk-';
    for (let i = 0; i < 48; i++) {
        result += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return result;
}

function showAddKeyDialog() {
    const overlay = document.createElement('div');
    overlay.className = 'dialog-overlay';
    overlay.innerHTML = `
        <div class="dialog">
            <div class="dialog-header">
                <h3>添加 API 密钥</h3>
                <button class="dialog-close" onclick="this.closest('.dialog-overlay').remove()">✕</button>
            </div>
            <div class="dialog-body">
                <div class="form-group">
                    <label>名称</label>
                    <input type="text" class="keyName" placeholder="例如：生产环境密钥" autofocus>
                </div>
                <div class="form-group">
                    <label>密钥值 <span style="color:var(--text-tertiary);font-size:12px;font-weight:400">（已自动生成，可修改）</span></label>
                    <div style="display:flex;gap:8px;align-items:center">
                        <input type="text" class="keyValue" value="${generateApiKey()}" readonly
                            style="font-family:var(--font-mono);font-size:13px;background:var(--bg-base);color:var(--text-secondary);flex:1">
                        <button class="btn btn-secondary" onclick="regenerateKey(this)" title="重新生成" style="flex-shrink:0;padding:8px 12px">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="width:16px;height:16px">
                                <polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/>
                            </svg>
                        </button>
                    </div>
                </div>
            </div>
            <div class="dialog-footer">
                <button class="btn btn-secondary" onclick="this.closest('.dialog-overlay').remove()">取消</button>
                <button class="btn btn-primary" onclick="saveApiKey(this)">保存</button>
            </div>
        </div>
    `;
    document.body.appendChild(overlay);
    overlay.querySelector('.keyName').focus();
}

function regenerateKey(btn) {
    const dialog = btn.closest('.dialog');
    const input = dialog.querySelector('.keyValue');
    if (input) input.value = generateApiKey();
}

async function saveApiKey(btn) {
    const dialog = btn.closest('.dialog');
    const name = dialog.querySelector('.keyName').value.trim();
    const key = dialog.querySelector('.keyValue').value.trim();

    if (!name || !key) {
        showToast('请填写必填字段', 'error');
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/keys`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ name, key })
        });

        if (!res.ok) {
            const err = await res.json();
            throw new Error(err.error || '保存失败');
        }

        showToast('密钥添加成功');
        dialog.closest('.dialog-overlay').remove();
        loadConfig();
    } catch (err) {
        showToast('保存失败: ' + err.message, 'error');
    }
}

async function deleteApiKey(id) {
    if (!confirm('确定要删除此密钥吗？')) return;

    try {
        await fetch(`${API_BASE}/keys/${id}`, { method: 'DELETE' });
        showToast('已删除');
        loadConfig();
    } catch (err) {
        showToast('删除失败: ' + err.message, 'error');
    }
}

// ===== BYOK Form =====
function renderByokForm() {
    if (!byokEnv) return;

    const fields = {
        'byok-providerBaseUrl': byokEnv.providerBaseUrl || window.location.origin + '/v1',
        'byok-providerType': byokEnv.providerType || 'openai',
        'byok-providerApiKey': byokEnv.providerApiKey || '',
        'byok-providerBearerToken': byokEnv.providerBearerToken || '',
        'byok-providerWireApi': byokEnv.providerWireApi || 'completions',
        'byok-providerAzureApiVersion': byokEnv.providerAzureApiVersion || '',
        'byok-model': byokEnv.model || 'auto-copilot',
        'byok-providerModelId': byokEnv.providerModelId || '',
        'byok-providerWireModel': byokEnv.providerWireModel || '',
        'byok-providerMaxPromptTokens': byokEnv.providerMaxPromptTokens ?? '',
        'byok-providerMaxOutputTokens': byokEnv.providerMaxOutputTokens ?? ''
    };

    for (const [id, value] of Object.entries(fields)) {
        const el = document.getElementById(id);
        if (el) el.value = value;
    }
}

async function saveByokEnv() {
    const form = document.getElementById('byokForm');
    const formData = new FormData(form);

    const payload = {
        providerBaseUrl: formData.get('providerBaseUrl')?.trim() || '',
        providerType: formData.get('providerType')?.trim() || 'openai',
        providerApiKey: formData.get('providerApiKey')?.trim() || '',
        providerBearerToken: formData.get('providerBearerToken')?.trim() || '',
        providerWireApi: formData.get('providerWireApi')?.trim() || 'completions',
        providerAzureApiVersion: formData.get('providerAzureApiVersion')?.trim() || '',
        model: formData.get('model')?.trim() || 'auto-copilot',
        providerModelId: formData.get('providerModelId')?.trim() || formData.get('model')?.trim() || 'auto-copilot',
        providerWireModel: formData.get('providerWireModel')?.trim() || '',
        providerMaxPromptTokens: formData.get('providerMaxPromptTokens') ? parseInt(formData.get('providerMaxPromptTokens')) : null,
        providerMaxOutputTokens: formData.get('providerMaxOutputTokens') ? parseInt(formData.get('providerMaxOutputTokens')) : null
    };

    if (!payload.providerBaseUrl) {
        showToast('请填写 COPILOT_PROVIDER_BASE_URL', 'error');
        return;
    }

    try {
        const res = await fetch(`${API_BASE}/byok`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });

        if (!res.ok) throw new Error('保存失败');

        const status = document.getElementById('byokSaveStatus');
        if (status) {
            status.textContent = '✓ 配置已保存';
            status.className = 'status-msg success';
            setTimeout(() => { status.textContent = ''; }, 3000);
        }
        showToast('BYOK 配置已保存');
        loadConfig();
    } catch (err) {
        const status = document.getElementById('byokSaveStatus');
        if (status) {
            status.textContent = '✗ 保存失败';
            status.className = 'status-msg error';
        }
        showToast('保存失败: ' + err.message, 'error');
    }
}

// ===== Connection Info =====
function renderConnectionInfo() {
    const baseUrl = window.location.origin;
    const openaiUrl = `${baseUrl}/v1`;
    const anthropicUrl = `${baseUrl}/v1`;

    const openaiCode = document.getElementById('openaiUrl');
    const anthropicCode = document.getElementById('anthropicUrl');
    const proxyCode = document.getElementById('proxyModel');

    if (openaiCode) openaiCode.textContent = openaiUrl;
    if (anthropicCode) anthropicCode.textContent = anthropicUrl;
    if (proxyCode) proxyCode.textContent = 'auto-copilot';
}

// ===== Copy to Clipboard =====
async function copyToClipboard(elementId) {
    const el = document.getElementById(elementId);
    if (!el) return;

    const text = el.textContent || el.value;
    try {
        await navigator.clipboard.writeText(text);
        showToast('已复制到剪贴板');
    } catch {
        const textarea = document.createElement('textarea');
        textarea.value = text;
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
        showToast('已复制到剪贴板');
    }
}

// ===== Toast =====
function showToast(message, type = 'success') {
    const existing = document.querySelector('.toast');
    if (existing) existing.remove();

    const toast = document.createElement('div');
    toast.className = 'toast';
    toast.textContent = message;
    document.body.appendChild(toast);
    setTimeout(() => toast.remove(), 2200);
}

// ===== Utility =====
function escapeHtml(str) {
    if (!str) return '';
    const div = document.createElement('div');
    div.textContent = str;
    return div.innerHTML;
}
