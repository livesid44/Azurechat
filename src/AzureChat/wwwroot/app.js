'use strict';

// ── State ──────────────────────────────────────────────────────────────────
let conversationHistory = [];
let ragEnabled = true;
let isBusy = false;

// ── Initialisation ─────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', () => {
  loadConfig();
  loadRagStatus();

  // RAG toggle handler
  document.getElementById('ragToggle').addEventListener('change', toggleRag);

  // Focus the input on load
  document.getElementById('messageInput').focus();
});

// ── Configuration ──────────────────────────────────────────────────────────
async function loadConfig() {
  try {
    const resp = await fetch('/api/config');
    const cfg = await resp.json();
    renderConfig(cfg);
  } catch (e) {
    document.getElementById('configBody').innerHTML =
      `<div class="alert alert-danger">Failed to load config: ${escapeHtml(e.message)}</div>`;
  }
}

// Cached config for pre-populating the settings form
let _lastConfig = null;

function renderConfig(cfg) {
  _lastConfig = cfg;
  const configuredBadge = (ok) =>
    `<span class="badge ms-1 ${ok ? 'bg-success' : 'bg-danger'}">${ok ? 'Configured' : 'Not set'}</span>`;

  const row = (label, value, ok) => `
    <div class="d-flex justify-content-between align-items-start mb-2">
      <span class="text-muted">${label}</span>
      <div class="text-end">
        ${value ? `<span class="text-break">${escapeHtml(value)}</span>` : '<em class="text-muted">—</em>'}
        ${ok !== undefined ? configuredBadge(ok) : ''}
      </div>
    </div>`;

  document.getElementById('configBody').innerHTML = `
    <h6 class="text-uppercase text-muted letter-spacing mb-2">Azure OpenAI</h6>
    ${row('Endpoint', cfg.openAI.endpoint, cfg.openAI.configured)}
    ${row('Deployment', cfg.openAI.deploymentName)}
    <hr class="my-2"/>

    <h6 class="text-uppercase text-muted mb-2">Azure AI Search</h6>
    ${row('Endpoint', cfg.search.endpoint, cfg.search.configured)}
    ${row('Index', cfg.search.indexName)}
    ${row('Key field', cfg.search.keyField)}
    ${row('Content field', cfg.search.contentField)}
    ${row('Title field', cfg.search.titleField)}
    <div class="mt-2 mb-1">
      <button class="btn btn-sm btn-outline-info w-100" onclick="discoverIndexFields()"
              id="discoverBtn" ${cfg.search.configured ? '' : 'disabled'}>
        <i class="bi bi-binoculars me-1"></i>Discover index fields…
      </button>
    </div>
    <div id="indexFieldsResult"></div>
    <hr class="my-2"/>

    <h6 class="text-uppercase text-muted mb-2">RAG</h6>
    ${row('Status', cfg.rag.enabled ? '🟢 Enabled' : '🔴 Disabled')}
    <hr class="my-2"/>

    <h6 class="text-uppercase text-muted mb-2">Blob Storage</h6>
    ${row('Container', cfg.blob.containerName, cfg.blob.configured)}
    <hr class="my-2"/>

    <h6 class="text-uppercase text-muted mb-2">Cosmos DB</h6>
    ${row('Database', cfg.cosmos.databaseName, cfg.cosmos.configured)}
    ${row('Container', cfg.cosmos.containerName)}
    <hr class="my-2"/>

    <button class="btn btn-sm btn-outline-warning w-100 mb-1"
            onclick="toggleSettingsEditor()">
      <i class="bi bi-pencil-square me-1"></i>Edit credentials…
    </button>
    <div id="settingsEditor" class="d-none mt-2">${buildSettingsForm(cfg)}</div>`;
}

// ── Settings editor ────────────────────────────────────────────────────────
function field(id, label, placeholder, value = '') {
  return `
    <div class="mb-2">
      <label class="form-label mb-1 text-muted" for="${id}" style="font-size:.78rem;">${label}</label>
      <input type="text" class="form-control form-control-sm bg-dark text-white border-secondary"
             id="${id}" placeholder="${placeholder}" value="${escapeHtml(value)}" autocomplete="off" />
    </div>`;
}

function passwordField(id, label, placeholder) {
  return `
    <div class="mb-2">
      <label class="form-label mb-1 text-muted" for="${id}" style="font-size:.78rem;">${label}</label>
      <input type="password" class="form-control form-control-sm bg-dark text-white border-secondary"
             id="${id}" placeholder="${placeholder}" autocomplete="new-password" />
    </div>`;
}

function buildSettingsForm(cfg) {
  return `
    <div class="alert alert-info py-2 small mb-2">
      <i class="bi bi-info-circle me-1"></i>
      Paste values from <strong>Azure portal</strong>. Leave a field blank to keep the existing value.
      Credentials are saved to <code>appsettings.local.json</code> (gitignored).
    </div>

    <h6 class="text-uppercase text-muted mb-2" style="font-size:.7rem;">
      <i class="bi bi-cpu me-1"></i>Azure OpenAI
      <small class="text-muted fw-normal">(portal → OpenAI resource → Keys and Endpoint)</small>
    </h6>
    ${field('se-oai-endpoint', 'Endpoint', 'https://xxx.openai.azure.com/', cfg.openAI.endpoint || '')}
    ${passwordField('se-oai-key', 'API Key (Key 1)', 'paste key from portal')}
    ${field('se-oai-deploy', 'Deployment Name', 'gpt-4o', cfg.openAI.deploymentName || '')}

    <h6 class="text-uppercase text-muted mt-3 mb-2" style="font-size:.7rem;">
      <i class="bi bi-search me-1"></i>Azure AI Search
      <small class="text-muted fw-normal">(portal → Search resource → Keys)</small>
    </h6>
    ${field('se-srch-endpoint', 'Endpoint', 'https://xxx.search.windows.net', cfg.search.endpoint || '')}
    ${passwordField('se-srch-key', 'Admin / Query Key', 'paste key from portal')}
    ${field('se-srch-index', 'Index Name', 'rag-index', cfg.search.indexName || '')}
    ${field('se-srch-content', 'Content Field', 'chunk', cfg.search.contentField || '')}
    ${field('se-srch-title', 'Title Field', 'title', cfg.search.titleField || '')}
    ${field('se-srch-key-field', 'Key Field', 'id', cfg.search.keyField || '')}

    <h6 class="text-uppercase text-muted mt-3 mb-2" style="font-size:.7rem;">
      <i class="bi bi-cloud me-1"></i>Blob Storage
      <small class="text-muted fw-normal">(portal → Storage account → Access keys)</small>
    </h6>
    ${passwordField('se-blob-connstr', 'Connection String (preferred)', 'DefaultEndpointsProtocol=https;AccountName=...')}
    <div class="text-muted small text-center mb-1">— or account name + key —</div>
    ${field('se-blob-acct', 'Account Name', 'mystorageaccount', cfg.blob.accountName || '')}
    ${passwordField('se-blob-acctkey', 'Account Key', 'paste key from portal')}
    ${field('se-blob-container', 'Container Name', 'documents', cfg.blob.containerName || '')}

    <h6 class="text-uppercase text-muted mt-3 mb-2" style="font-size:.7rem;">
      <i class="bi bi-database me-1"></i>Cosmos DB
      <small class="text-muted fw-normal">(portal → Cosmos DB → Keys)</small>
    </h6>
    ${field('se-cos-endpoint', 'URI', 'https://xxx.documents.azure.com:443/', cfg.cosmos.endpoint || '')}
    ${passwordField('se-cos-key', 'Primary Key', 'paste primary key from portal')}
    ${field('se-cos-db', 'Database Name', 'rag-db', cfg.cosmos.databaseName || '')}
    ${field('se-cos-container', 'Container Name', 'documents', cfg.cosmos.containerName || '')}

    <div id="settingsFeedback"></div>
    <button class="btn btn-warning btn-sm w-100 mt-2" onclick="saveSettings()">
      <i class="bi bi-floppy me-1"></i>Save credentials
    </button>`;
}

function toggleSettingsEditor() {
  const el = document.getElementById('settingsEditor');
  if (!el) return;
  el.classList.toggle('d-none');
}

async function saveSettings() {
  const get = (id) => document.getElementById(id)?.value?.trim() || null;
  const fb = document.getElementById('settingsFeedback');
  if (fb) fb.innerHTML = '<div class="alert alert-secondary py-1 small mt-1">Saving…</div>';

  const body = {
    openAI: {
      endpoint:       get('se-oai-endpoint'),
      apiKey:         get('se-oai-key'),
      deploymentName: get('se-oai-deploy'),
    },
    search: {
      endpoint:     get('se-srch-endpoint'),
      apiKey:       get('se-srch-key'),
      indexName:    get('se-srch-index'),
      contentField: get('se-srch-content'),
      titleField:   get('se-srch-title'),
      keyField:     get('se-srch-key-field'),
      vectorField:  null,
    },
    blob: {
      connectionString: get('se-blob-connstr'),
      accountName:      get('se-blob-acct'),
      accountKey:       get('se-blob-acctkey'),
      containerName:    get('se-blob-container'),
    },
    cosmos: {
      endpoint:        get('se-cos-endpoint'),
      accountKey:      get('se-cos-key'),
      databaseName:    get('se-cos-db'),
      containerName:   get('se-cos-container'),
      partitionKeyPath:  null,
      partitionKeyValue: null,
    },
  };

  try {
    const resp = await fetch('/api/settings', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    const result = await resp.json();

    if (!resp.ok) {
      if (fb) fb.innerHTML = `<div class="alert alert-danger py-1 small mt-1">
        ❌ ${escapeHtml(result.detail || result.title || 'Save failed')}</div>`;
      return;
    }

    if (fb) fb.innerHTML = `<div class="alert alert-success py-1 small mt-1">
      ✅ ${escapeHtml(result.message)}</div>`;

    // Refresh the config summary panel so it shows the new values.
    await loadConfig();
  } catch (err) {
    if (fb) fb.innerHTML = `<div class="alert alert-danger py-1 small mt-1">
      ❌ ${escapeHtml(err.message)}</div>`;
  }
}

// ── Index field discovery ──────────────────────────────────────────────────
async function discoverIndexFields() {
  const btn = document.getElementById('discoverBtn');
  const resultDiv = document.getElementById('indexFieldsResult');
  if (!btn || !resultDiv) return;

  btn.disabled = true;
  btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status"></span>Fetching…';
  resultDiv.innerHTML = '';

  try {
    const resp = await fetch('/api/search/fields');
    const data = await resp.json();

    if (!resp.ok) {
      resultDiv.innerHTML = `<div class="alert alert-danger mt-2 py-2 small">${escapeHtml(data.detail || data.title || 'Error fetching fields')}</div>`;
      return;
    }

    const fields = data.fields || [];
    if (fields.length === 0) {
      resultDiv.innerHTML = `<p class="text-muted small mt-2">No fields found in index.</p>`;
      return;
    }

    const roleBadge = (role) => {
      if (role === 'content') return `<span class="badge bg-primary ms-1">→ ContentField</span>`;
      if (role === 'title')   return `<span class="badge bg-success ms-1">→ TitleField</span>`;
      return '';
    };

    const rows = fields.map(f => `
      <tr>
        <td class="fw-semibold text-break">${escapeHtml(f.name)}${roleBadge(f.suggestedRole)}</td>
        <td class="text-muted">${escapeHtml(f.type.replace('Edm.', ''))}</td>
        <td class="text-center">${f.isSearchable ? '✔' : ''}</td>
        <td class="text-center">${f.isRetrievable ? '✔' : ''}</td>
      </tr>`).join('');

    const content = fields.find(f => f.suggestedRole === 'content');
    const title   = fields.find(f => f.suggestedRole === 'title');
    const hint = (content || title) ? `
      <div class="alert alert-info py-2 small mt-2">
        <strong>Suggested settings:</strong><br/>
        ${content ? `<code>AzureSearch__ContentField=${escapeHtml(content.name)}</code><br/>` : ''}
        ${title   ? `<code>AzureSearch__TitleField=${escapeHtml(title.name)}</code>` : ''}
        <br/><span class="text-muted">Or update <code>appsettings.json</code> and restart.</span>
      </div>` : '';

    resultDiv.innerHTML = `
      ${hint}
      <div class="table-responsive mt-1">
        <table class="table table-sm table-dark table-bordered" style="font-size:.78rem;">
          <thead><tr>
            <th>Field name</th><th>Type</th>
            <th title="Full-text searchable">🔍</th>
            <th title="Retrievable in $select">📤</th>
          </tr></thead>
          <tbody>${rows}</tbody>
        </table>
      </div>`;
  } catch (err) {
    resultDiv.innerHTML = `<div class="alert alert-danger mt-2 py-2 small">Error: ${escapeHtml(err.message)}</div>`;
  } finally {
    btn.disabled = false;
    btn.innerHTML = '<i class="bi bi-binoculars me-1"></i>Discover index fields…';
  }
}

// ── RAG toggle ─────────────────────────────────────────────────────────────
async function loadRagStatus() {
  try {
    const resp = await fetch('/api/rag');
    const status = await resp.json();
    ragEnabled = status.enabled;
    document.getElementById('ragToggle').checked = ragEnabled;
  } catch (e) {
    console.warn('Could not load RAG status:', e);
  }
}

async function toggleRag(e) {
  const desired = e.target.checked;
  try {
    const resp = await fetch('/api/rag', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ enabled: desired }),
    });
    const status = await resp.json();
    ragEnabled = status.enabled;
    setStatus(ragEnabled ? '🔍 RAG enabled' : '💬 RAG disabled (model knowledge only)');
  } catch (err) {
    e.target.checked = ragEnabled; // revert
    setStatus(`Error toggling RAG: ${err.message}`);
  }
}

// ── Chat ───────────────────────────────────────────────────────────────────
function handleKeyDown(e) {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendMessage();
  }
}

async function sendMessage() {
  if (isBusy) return;

  const input = document.getElementById('messageInput');
  const message = input.value.trim();
  if (!message) return;

  // Remove welcome card on first message
  const welcome = document.getElementById('welcomeCard');
  if (welcome) welcome.remove();

  input.value = '';
  setBusy(true);
  appendUserMessage(message);
  const thinkingEl = appendThinkingIndicator();
  setStatus('');

  try {
    const resp = await fetch('/api/chat', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message, history: conversationHistory }),
    });

    if (!resp.ok) {
      const body = await resp.text();
      // 400 = user-fixable configuration error (e.g. wrong deployment name)
      if (resp.status === 400) {
        let parsed;
        try { parsed = JSON.parse(body); } catch { parsed = null; }
        const detail = parsed?.detail ?? parsed?.title ?? body;
        thinkingEl.remove();
        appendSystemMessage(
          `⚙️ Configuration error:\n${detail}\n\nOpen the ⚙ Config panel to verify your Azure OpenAI Deployment name.`,
          'warning');
        setBusy(false);
        input.focus();
        return;
      }
      throw new Error(`HTTP ${resp.status}: ${body}`);
    }

    const result = await resp.json();

    // Persist turn in history
    conversationHistory.push({ role: 'user', content: message });
    conversationHistory.push({ role: 'assistant', content: result.answer });

    thinkingEl.remove();
    appendAssistantMessage(result.answer, result.sources || []);
  } catch (err) {
    thinkingEl.remove();
    appendSystemMessage(`Error: ${err.message}`, 'danger');
  } finally {
    setBusy(false);
    input.focus();
  }
}

// ── Clear history ──────────────────────────────────────────────────────────
function clearHistory() {
  conversationHistory = [];
  const container = document.getElementById('chatMessages');
  container.innerHTML = '';
  appendSystemMessage('Conversation cleared.', 'secondary');
  setStatus('');
}

// ── Ingestion ──────────────────────────────────────────────────────────────
async function runIngest() {
  if (isBusy) return;

  const btn = document.getElementById('ingestBtn');
  const origHtml = btn.innerHTML;
  setBusy(true);
  btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status"></span>Ingesting…';
  setStatus('Running ingestion pipeline…');

  try {
    const resp = await fetch('/api/ingest', { method: 'POST' });
    const result = await resp.json();

    if (!resp.ok) {
      appendSystemMessage(`Ingestion failed: ${result.detail || result.title || 'Unknown error'}`, 'danger');
    } else {
      const errLines = (result.errors || []).map(e => `• ${e}`).join('\n');
      const msg = `Ingestion complete — ✔ ${result.succeeded} succeeded, ✘ ${result.failed} failed` +
                  (errLines ? `\n${errLines}` : '');
      appendSystemMessage(msg, result.failed > 0 ? 'warning' : 'success');
    }
  } catch (err) {
    appendSystemMessage(`Ingestion error: ${err.message}`, 'danger');
  } finally {
    setBusy(false);
    btn.innerHTML = origHtml;
    setStatus('');
  }
}

// ── DOM helpers ────────────────────────────────────────────────────────────
function appendUserMessage(text) {
  const container = document.getElementById('chatMessages');
  const div = document.createElement('div');
  div.className = 'chat-row user';
  div.innerHTML = `
    <div class="chat-bubble user">${escapeHtml(text)}</div>
    <div class="chat-avatar user-avatar"><i class="bi bi-person-fill"></i></div>`;
  container.appendChild(div);
  scrollToBottom();
}

function appendAssistantMessage(text, sources) {
  const container = document.getElementById('chatMessages');
  const div = document.createElement('div');
  div.className = 'chat-row assistant';

  // Render markdown safely
  const html = marked.parse(text, { breaks: true });

  let sourcesHtml = '';
  if (sources.length > 0) {
    const badges = sources.map(s => {
      const scoreStr = s.score?.toFixed(3) ?? '';
      const label = `${escapeHtml(s.title || 'Untitled')}${scoreStr ? ` <small class="opacity-75">${scoreStr}</small>` : ''}`;
      const titleAttr = `Score: ${s.score?.toFixed(3) ?? '—'}`;
      if (s.sourcePath) {
        const href = `/api/blob/download?path=${encodeURIComponent(s.sourcePath)}`;
        return `<a class="badge bg-secondary fw-normal me-1 mb-1 text-decoration-none"
                   href="${href}" target="_blank" rel="noopener noreferrer"
                   title="${escapeHtml(titleAttr)} — click to open file"
                 >${label} <i class="bi bi-box-arrow-up-right ms-1" style="font-size:.7em;"></i></a>`;
      }
      return `<span class="badge bg-secondary fw-normal me-1 mb-1"
                    title="${escapeHtml(titleAttr)}">${label}</span>`;
    }).join('');
    sourcesHtml = `<div class="chat-sources mt-2">
      <small class="text-muted d-block mb-1"><i class="bi bi-search me-1"></i>Sources used:</small>
      ${badges}
    </div>`;
  }

  div.innerHTML = `
    <div class="chat-avatar assistant-avatar"><i class="bi bi-robot"></i></div>
    <div class="chat-bubble assistant">
      <div class="markdown-body">${html}</div>
      ${sourcesHtml}
    </div>`;
  container.appendChild(div);
  scrollToBottom();
}

function appendThinkingIndicator() {
  const container = document.getElementById('chatMessages');
  const div = document.createElement('div');
  div.className = 'chat-row assistant';
  div.innerHTML = `
    <div class="chat-avatar assistant-avatar"><i class="bi bi-robot"></i></div>
    <div class="chat-bubble assistant thinking">
      <span class="dot"></span><span class="dot"></span><span class="dot"></span>
    </div>`;
  container.appendChild(div);
  scrollToBottom();
  return div;
}

function appendSystemMessage(msg, type = 'secondary') {
  const container = document.getElementById('chatMessages');
  const div = document.createElement('div');
  div.className = 'd-flex justify-content-center px-3 py-1';
  div.innerHTML = `
    <div class="alert alert-${type} py-2 px-3 mb-2 small" style="max-width:720px; white-space:pre-wrap;">
      ${escapeHtml(msg)}
    </div>`;
  container.appendChild(div);
  scrollToBottom();
}

function scrollToBottom() {
  const container = document.getElementById('chatMessages');
  container.scrollTop = container.scrollHeight;
}

function setStatus(msg) {
  document.getElementById('statusText').textContent = msg;
}

function setBusy(busy) {
  isBusy = busy;
  document.getElementById('sendBtn').disabled = busy;
  document.getElementById('ingestBtn').disabled = busy;
  document.getElementById('messageInput').disabled = busy;
}

// ── Utilities ──────────────────────────────────────────────────────────────
function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#039;');
}
