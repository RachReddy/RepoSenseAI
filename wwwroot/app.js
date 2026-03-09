mermaid.initialize({ startOnLoad: false, theme: 'dark' });

async function analyze() {
  const url = document.getElementById('repoUrl').value.trim();

  if (!url) {
    showError('Please enter a GitHub repository URL.');
    return;
  }

  if (!url.includes('github.com')) {
    showError('Only GitHub repository URLs are supported.');
    return;
  }

  hideAll();
  document.getElementById('loadingSection').classList.remove('hidden');
  document.getElementById('analyzeBtn').disabled = true;

  // After 15s, update loader text so users know it's still working
  const slowTimer = setTimeout(() => {
    const loaderText = document.getElementById('loaderText');
    const loaderSub = document.getElementById('loaderSub');
    if (loaderText) loaderText.innerHTML = 'Still working — almost there<span class="dots">...</span>';
    if (loaderSub) loaderSub.textContent = 'Large repositories take a bit longer. Hang tight.';
  }, 15000);

  try {
    const response = await fetch('/api/analysis/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ repoUrl: url })
    });

    const data = await response.json();

    if (!response.ok) {
      showError(data.error || 'An unexpected error occurred.');
      return;
    }

    renderResults(data);

  } catch (err) {
    showError('Could not reach the server. Please try again.');
  } finally {
    clearTimeout(slowTimer);
    document.getElementById('analyzeBtn').disabled = false;
  }
}

function renderResults(data) {
  hideAll();

  document.getElementById('repoName').textContent = data.repoName;
  document.getElementById('repoLink').href = data.repoUrl;

  // Summary — split into sentences and render as bullet points
  const summaryEl = document.getElementById('summary');
  const summarySentences = data.summary
    .split(/(?<=\.)\s+/)
    .map(s => s.trim())
    .filter(s => s.length > 10);

  if (summarySentences.length > 1) {
    summaryEl.innerHTML = '<ul class="summary-list">' +
      summarySentences.map(s => `<li>${s}</li>`).join('') +
      '</ul>';
  } else {
    summaryEl.innerHTML = `<p class="summary-text">${data.summary}</p>`;
  }

  // Architecture — split into sentences and render as bullet points
  const archEl = document.getElementById('architecture');
  const sentences = data.architecture
    .split(/(?<=\.)\s+/)
    .map(s => s.trim())
    .filter(s => s.length > 10);

  if (sentences.length > 1) {
    archEl.innerHTML = '<ul class="arch-list">' +
      sentences.map(s => `<li>${s}</li>`).join('') +
      '</ul>';
  } else {
    archEl.innerHTML = `<p class="summary-text">${data.architecture}</p>`;
  }

  // Tech Stack — render as badges
  const techEl = document.getElementById('techStack');
  const techs = data.techStack.split(',').map(t => t.trim()).filter(Boolean);
  techEl.innerHTML = techs.map(t => `<span class="badge">${t}</span>`).join('');

  // What's Done Well — render as checkmark list
  const wellEl = document.getElementById('whatsDoneWell');
  const wellLines = (data.whatsDoneWell || '').split('\n').map(l => l.trim()).filter(Boolean);
  if (wellLines.length > 0) {
    wellEl.innerHTML = '<ul class="well-list">' +
      wellLines.map(l => `<li>&#10003; ${l}</li>`).join('') +
      '</ul>';
  }

  // Improvements — render as numbered list
  const impEl = document.getElementById('improvements');
  const lines = data.improvements
    .split('\n')
    .map(l => l.trim())
    .filter(Boolean);

  if (lines.length > 0) {
    impEl.innerHTML = '<ol>' + lines.map(l => {
      // Strip leading "1. " "2. " etc if AI added them
      const clean = l.replace(/^\d+\.\s*/, '');
      return `<li>${clean}</li>`;
    }).join('') + '</ol>';
  } else {
    impEl.textContent = data.improvements;
  }

  // Mermaid diagram
  renderDiagram(data.mermaidDiagram);

  document.getElementById('resultsSection').classList.remove('hidden');
}

async function renderDiagram(diagramCode) {
  const diagramEl = document.getElementById('diagram');

  if (!diagramCode || diagramCode.trim() === '') {
    diagramEl.textContent = 'No diagram available.';
    return;
  }

  try {
    // Unescape \n sequences back to real newlines for Mermaid
    let code = diagramCode.replace(/\\n/g, '\n').trim();

    // Fix common AI Mermaid syntax mistakes
    code = code.replace(/\|([^|]*)\|>/g, '|$1|');           // Fix -->|text|> to -->|text|
    code = code.replace(/;/g, '');                           // Remove stray semicolons
    code = code.replace(/\(([^)]*)\)/g, '[$1]');             // Fix (label) to [label]
    // Sanitize node label text — strip chars Mermaid can't handle inside []
    code = code.replace(/\[([^\]]*)\]/g, (match, label) => {
      const clean = label.replace(/[\/\\:'"<>{}|]/g, ' ').replace(/\s+/g, ' ').trim();
      return `[${clean}]`;
    });
    // Sanitize arrow label text — strip same bad chars inside ||
    code = code.replace(/\|([^|]*)\|/g, (match, label) => {
      const clean = label.replace(/[\/\\:'"<>{}]/g, ' ').replace(/\s+/g, ' ').trim();
      return `|${clean}|`;
    });
    diagramEl.removeAttribute('data-processed');
    diagramEl.innerHTML = '';
    const { svg } = await mermaid.render('mermaid-diagram', code);
    diagramEl.innerHTML = svg;
  } catch (err) {
    diagramEl.textContent = 'Could not render architecture diagram.';
    console.error('Mermaid error:', err);
  }
}

function showError(message) {
  hideAll();
  document.getElementById('errorMessage').textContent = message;
  document.getElementById('errorSection').classList.remove('hidden');
}

function hideAll() {
  document.getElementById('loadingSection').classList.add('hidden');
  document.getElementById('errorSection').classList.add('hidden');
  document.getElementById('resultsSection').classList.add('hidden');
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('repoUrl').addEventListener('keydown', (e) => {
    if (e.key === 'Enter') analyze();
  });
});
