// Metrics dashboard
let currentPeriod = '24h';
let currentPage = 1;
let modelChart = null;
let latencyChart = null;
let hourlyReqChart = null;
let hourlyTokenChart = null;
let hourlyLatencyChart = null;
let hourlyTpsChart = null;

// Tab switching - use nav-item like config.js
document.querySelectorAll('.nav-item').forEach(btn => {
    btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        document.querySelectorAll('.nav-item').forEach(n => n.classList.remove('active'));
        document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
        btn.classList.add('active');
        document.getElementById(tab).classList.add('active');

        if (tab === 'metrics') {
            loadMetrics();
        }
    });
});

// Period selector
document.querySelectorAll('.period-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.period-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        currentPeriod = btn.dataset.period;
        loadMetrics();
    });
});

async function loadMetrics() {
    await Promise.all([
        loadSummary(),
        loadCharts(),
        loadHourlyCharts(),
        loadModelFilter(),
        loadRequestLog()
    ]);
}

async function loadSummary() {
    try {
        const response = await fetch(`/api/metrics/summary?period=${currentPeriod}`);
        const data = await response.json();

        const totalEl = document.getElementById('m-total-requests');
        const successEl = document.getElementById('m-success-rate');
        const promptEl = document.getElementById('m-prompt-tokens');
        const completionEl = document.getElementById('m-completion-tokens');
        const cachedEl = document.getElementById('m-cached-tokens');
        const costEl = document.getElementById('m-est-cost');
        const latencyEl = document.getElementById('m-avg-latency');
        const cacheEl = document.getElementById('m-cache-rate');

        if (totalEl) totalEl.textContent = formatNumber(data.totalRequests);
        if (successEl) successEl.textContent = data.successRate + '%';
        if (promptEl) promptEl.textContent = formatNumber(data.tokenUsage.promptTokens);
        if (completionEl) completionEl.textContent = formatNumber(data.tokenUsage.completionTokens);
        if (cachedEl) cachedEl.textContent = formatNumber(data.tokenUsage.cachedTokens);
        if (costEl) costEl.textContent = '$' + data.tokenUsage.estimatedCost.toFixed(4);
        if (latencyEl) latencyEl.textContent = data.performance.avgLatencyMs + 'ms';
        if (cacheEl) cacheEl.textContent = data.performance.cacheHitRate + '%';
    } catch (err) {
        console.error('Failed to load summary:', err);
    }
}

// Vibrant color palette for dark backgrounds
const CHART_COLORS = [
    '#60a5fa', // blue-400
    '#f472b6', // pink-400
    '#34d399', // emerald-400
    '#fbbf24', // amber-400
    '#a78bfa', // violet-400
    '#fb7185', // rose-400
    '#22d3ee', // cyan-400
    '#a3e635', // lime-400
    '#f87171', // red-400
    '#2dd4bf', // teal-400
];

const CHART_FILLS = CHART_COLORS.map(c => c + '25');

async function loadCharts() {
    try {
        const response = await fetch(`/api/metrics/summary?period=${currentPeriod}`);
        const data = await response.json();

        // Model usage pie chart
        const modelCtx = document.getElementById('model-chart');
        if (modelCtx) {
            if (modelChart) modelChart.destroy();

            const labels = data.modelBreakdown.map(m => m.model);
            const values = data.modelBreakdown.map(m => m.requests);

            if (labels.length === 0) {
                modelChart = new Chart(modelCtx, {
                    type: 'doughnut',
                    data: { labels: ['暂无数据'], datasets: [{ data: [1], backgroundColor: ['#3a3a3a'] }] },
                    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } } }
                });
            } else {
                modelChart = new Chart(modelCtx, {
                    type: 'doughnut',
                    data: {
                        labels: labels,
                        datasets: [{
                            data: values,
                            backgroundColor: CHART_COLORS,
                            borderWidth: 0
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        cutout: '55%',
                        plugins: {
                            legend: { position: 'bottom', labels: { usePointStyle: true, padding: 16, color: '#c0c0c0' } }
                        }
                    }
                });
            }
        }

        // Latency bar chart
        const latencyCtx = document.getElementById('latency-chart');
        if (latencyCtx) {
            if (latencyChart) latencyChart.destroy();

            const labels = data.modelBreakdown.map(m => m.model);

            if (labels.length === 0) {
                latencyChart = new Chart(latencyCtx, {
                    type: 'bar',
                    data: { labels: ['暂无数据'], datasets: [{ data: [0], backgroundColor: '#3a3a3a' }] },
                    options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } } }
                });
            } else {
                latencyChart = new Chart(latencyCtx, {
                    type: 'bar',
                    data: {
                        labels: labels,
                        datasets: [{
                            label: '平均延迟 (ms)',
                            data: data.modelBreakdown.map(m => m.avgLatencyMs),
                            backgroundColor: CHART_COLORS[0],
                            borderRadius: 6,
                            barThickness: 32
                        }]
                    },
                    options: {
                        responsive: true,
                        maintainAspectRatio: false,
                        scales: {
                            y: { beginAtZero: true, grid: { color: 'rgba(255,255,255,0.06)' }, ticks: { color: '#888' } },
                            x: { grid: { display: false }, ticks: { color: '#888' } }
                        },
                        plugins: { legend: { display: false } }
                    }
                });
            }
        }
    } catch (err) {
        console.error('Failed to load charts:', err);
    }
}

async function loadHourlyCharts() {
    try {
        const response = await fetch(`/api/metrics/hourly?period=${currentPeriod}`);
        const data = await response.json();

        const hours = data.hours;
        const series = data.series;

        const makeDataset = (propFn) => series.map((s, i) => ({
            label: s.provider ? `${s.provider} / ${s.model}` : s.model,
            data: propFn(s),
            borderColor: CHART_COLORS[i % CHART_COLORS.length],
            backgroundColor: CHART_FILLS[i % CHART_FILLS.length],
            fill: true,
            tension: 0.4,
            pointRadius: 4,
            pointHoverRadius: 7,
            pointBackgroundColor: CHART_COLORS[i % CHART_COLORS.length],
            pointBorderColor: '#1e1e1e',
            pointBorderWidth: 2,
            borderWidth: 2.5
        }));

        const commonOptions = {
            responsive: true,
            maintainAspectRatio: false,
            interaction: { mode: 'index', intersect: false },
            scales: {
                x: {
                    grid: { color: 'rgba(255,255,255,0.04)' },
                    ticks: { color: '#888', maxRotation: 0, autoSkip: true, maxTicksLimit: 12 }
                },
                y: {
                    beginAtZero: true,
                    grid: { color: 'rgba(255,255,255,0.06)' },
                    ticks: { color: '#888' }
                }
            },
            plugins: {
                legend: {
                    position: 'top',
                    labels: { usePointStyle: true, boxWidth: 10, padding: 14, color: '#c0c0c0' }
                },
                tooltip: {
                    backgroundColor: 'rgba(30,30,30,0.95)',
                    titleColor: '#fff',
                    bodyColor: '#ccc',
                    padding: 12,
                    cornerRadius: 8,
                    borderColor: 'rgba(255,255,255,0.1)',
                    borderWidth: 1
                }
            }
        };

        const emptyDataset = [{ data: [0], borderColor: '#3a3a3a', backgroundColor: 'transparent' }];

        // Hourly requests
        const reqCtx = document.getElementById('hourly-requests-chart');
        if (reqCtx) {
            if (hourlyReqChart) hourlyReqChart.destroy();
            hourlyReqChart = new Chart(reqCtx, {
                type: 'line',
                data: series.length === 0 ? { labels: ['暂无数据'], datasets: emptyDataset } : { labels: hours, datasets: makeDataset(s => s.requests) },
                options: commonOptions
            });
        }

        // Hourly tokens
        const tokenCtx = document.getElementById('hourly-tokens-chart');
        if (tokenCtx) {
            if (hourlyTokenChart) hourlyTokenChart.destroy();
            hourlyTokenChart = new Chart(tokenCtx, {
                type: 'line',
                data: series.length === 0 ? { labels: ['暂无数据'], datasets: emptyDataset } : { labels: hours, datasets: makeDataset(s => s.tokens) },
                options: commonOptions
            });
        }

        // Hourly latency
        const latCtx = document.getElementById('hourly-latency-chart');
        if (latCtx) {
            if (hourlyLatencyChart) hourlyLatencyChart.destroy();
            hourlyLatencyChart = new Chart(latCtx, {
                type: 'line',
                data: series.length === 0 ? { labels: ['暂无数据'], datasets: emptyDataset } : { labels: hours, datasets: makeDataset(s => s.latency) },
                options: commonOptions
            });
        }

        // Hourly TPS
        const tpsCtx = document.getElementById('hourly-tps-chart');
        if (tpsCtx) {
            if (hourlyTpsChart) hourlyTpsChart.destroy();
            hourlyTpsChart = new Chart(tpsCtx, {
                type: 'line',
                data: series.length === 0 ? { labels: ['暂无数据'], datasets: emptyDataset } : { labels: hours, datasets: makeDataset(s => s.tps) },
                options: commonOptions
            });
        }
    } catch (err) {
        console.error('Failed to load hourly charts:', err);
    }
}

async function loadModelFilter() {
    try {
        const response = await fetch('/api/metrics/models');
        const models = await response.json();
        const select = document.getElementById('log-filter');
        if (!select) return;

        const currentVal = select.value;
        select.innerHTML = '<option value="">全部模型</option>';
        models.forEach(m => {
            const opt = document.createElement('option');
            opt.value = m;
            opt.textContent = m;
            select.appendChild(opt);
        });
        select.value = currentVal;
    } catch (err) {
        console.error('Failed to load model filter:', err);
    }
}

async function loadRequestLog(page = 1) {
    currentPage = page;
    const model = document.getElementById('log-filter')?.value?.trim() || '';

    try {
        const url = `/api/metrics/requests?page=${page}&pageSize=20${model ? '&model=' + encodeURIComponent(model) : ''}`;
        const response = await fetch(url);
        const data = await response.json();

        const tbody = document.getElementById('logBody');
        if (!tbody) return;

        if (data.data.length === 0) {
            tbody.innerHTML = '<tr><td colspan="11" class="empty-cell">暂无数据</td></tr>';
        } else {
            tbody.innerHTML = data.data.map(r => `
                <tr>
                    <td>${new Date(r.timestamp).toLocaleString('zh-CN')}</td>
                    <td>${escapeHtml(r.requestedModel)}</td>
                    <td>${escapeHtml(r.actualModel)}</td>
                    <td>${escapeHtml(r.provider)}</td>
                    <td>${formatNumber(r.promptTokens)}</td>
                    <td>${formatNumber(r.completionTokens)}</td>
                    <td>${formatNumber(r.cachedTokens || 0)}</td>
                    <td>${r.latencyMs}ms</td>
                    <td>${r.tokensPerSecond}</td>
                    <td>$${r.estimatedCost?.toFixed(4) || '0'}</td>
                    <td><span class="status-badge ${r.isSuccess ? 'success' : 'error'}">${r.isSuccess ? '✓' : '✗'}</span></td>
                </tr>
            `).join('');
        }

        renderPagination(data.pagination);
    } catch (err) {
        console.error('Failed to load request log:', err);
    }
}

function renderPagination(pagination) {
    const container = document.getElementById('logPagination');
    if (!container) return;

    if (!pagination || pagination.totalPages <= 1) {
        container.innerHTML = '';
        return;
    }

    let html = '';
    html += `<button onclick="loadRequestLog(${currentPage - 1})" ${currentPage === 1 ? 'disabled' : ''}>← 上一页</button>`;

    for (let i = 1; i <= pagination.totalPages; i++) {
        if (i === 1 || i === pagination.totalPages || Math.abs(i - currentPage) <= 2) {
            html += `<button onclick="loadRequestLog(${i})" class="${i === currentPage ? 'active' : ''}">${i}</button>`;
        } else if (Math.abs(i - currentPage) === 3) {
            html += '<span>...</span>';
        }
    }

    html += `<button onclick="loadRequestLog(${currentPage + 1})" ${currentPage === pagination.totalPages ? 'disabled' : ''}>下一页 →</button>`;
    container.innerHTML = html;
}

function refreshMetrics() {
    loadMetrics();
}

async function exportCsv() {
    try {
        const response = await fetch(`/api/metrics/export`);
        const blob = await response.blob();
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `metrics-export-${new Date().toISOString().slice(0,10)}.csv`;
        a.click();
        URL.revokeObjectURL(url);
    } catch (err) {
        console.error('Failed to export CSV:', err);
    }
}

function formatNumber(num) {
    if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
    if (num >= 1000) return (num / 1000).toFixed(1) + 'K';
    return num?.toString() || '0';
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// Add status badge styles dynamically
const style = document.createElement('style');
style.textContent = `
    .status-badge {
        display: inline-block;
        padding: 0.25rem 0.5rem;
        border-radius: 4px;
        font-weight: bold;
        font-size: 12px;
    }
    .status-badge.success {
        background: #d1fae5;
        color: #065f46;
    }
    .status-badge.error {
        background: #fee2e2;
        color: #991b1b;
    }
`;
document.head.appendChild(style);
