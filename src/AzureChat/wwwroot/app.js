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

function renderConfig(cfg) {
  const configuredBadge = (ok) =>
    `<span class="badge ms-1 ${ok ? 'bg-success' : 'bg-secondary'}">${ok ? 'Configured' : 'Not set'}</span>`;

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

    <p class="text-muted mt-2 mb-0">
      Edit <code>src/AzureChat/appsettings.json</code> or set environment variables
      (use <code>__</code> as the section separator), e.g.:<br/>
      <code>AzureSearch__ContentField=chunk</code><br/>
      <code>AzureSearch__TitleField=title</code><br/>
      <small>Use <strong>Discover index fields</strong> above to find the correct names for your index.</small>
    </p>`;
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

    if (!resp.ok) throw new Error(`HTTP ${resp.status}: ${await resp.text()}`);

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
    const badges = sources.map(s =>
      `<span class="badge bg-secondary fw-normal me-1 mb-1"
             title="Score: ${s.score?.toFixed(3) ?? '—'}">${escapeHtml(s.title || 'Untitled')} <small class="opacity-75">${s.score?.toFixed(3) ?? ''}</small></span>`
    ).join('');
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
