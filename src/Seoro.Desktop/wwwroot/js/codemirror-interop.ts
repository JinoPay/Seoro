import { EditorView, lineNumbers, drawSelection, highlightActiveLine, highlightSpecialChars } from '@codemirror/view';
import { EditorState, Compartment } from '@codemirror/state';
import { syntaxHighlighting, LanguageSupport, Language } from '@codemirror/language';
import { classHighlighter, highlightTree } from '@lezer/highlight';

// Language imports
import { javascript } from '@codemirror/lang-javascript';
import { python } from '@codemirror/lang-python';
import { rust } from '@codemirror/lang-rust';
import { java } from '@codemirror/lang-java';
import { html } from '@codemirror/lang-html';
import { css } from '@codemirror/lang-css';
import { json } from '@codemirror/lang-json';
import { xml } from '@codemirror/lang-xml';
import { sql } from '@codemirror/lang-sql';
import { php } from '@codemirror/lang-php';
import { cpp } from '@codemirror/lang-cpp';
import { markdown } from '@codemirror/lang-markdown';
import { yaml } from '@codemirror/lang-yaml';
import { go } from '@codemirror/lang-go';

// --- Language Registry ---

const languages: Record<string, () => LanguageSupport> = {
  'javascript': () => javascript({ jsx: true }),
  'typescript': () => javascript({ jsx: true, typescript: true }),
  'python': () => python(),
  'rust': () => rust(),
  'java': () => java(),
  'html': () => html(),
  'xml': () => xml(),
  'css': () => css(),
  'scss': () => css(),
  'json': () => json(),
  'sql': () => sql(),
  'php': () => php(),
  'cpp': () => cpp(),
  'c': () => cpp(),
  'objectivec': () => cpp(),
  'markdown': () => markdown(),
  'yaml': () => yaml(),
  'go': () => go(),
  'csharp': () => java(), // Java grammar is a reasonable approximation for C#
  'bash': () => null as any, // No official package — falls back to plain text
  'shell': () => null as any,
  'dockerfile': () => null as any,
  'ini': () => null as any,
  'toml': () => null as any,
  'ruby': () => null as any,
  'swift': () => null as any,
  'kotlin': () => java(), // Kotlin is close enough to Java grammar
  'dart': () => java(),
  'lua': () => null as any,
  'r': () => null as any,
  'perl': () => null as any,
  'makefile': () => null as any,
  'graphql': () => null as any,
  'protobuf': () => null as any,
  'powershell': () => null as any,
  'diff': () => null as any,
  'plaintext': () => null as any,
  'text': () => null as any,
};

function getLanguageSupport(lang: string): LanguageSupport | null {
  const factory = languages[lang?.toLowerCase()];
  if (!factory) return null;
  try {
    const result = factory();
    return result || null;
  } catch {
    return null;
  }
}

function getParser(lang: string) {
  const support = getLanguageSupport(lang);
  if (!support) return null;
  return support.language.parser;
}

// --- Escape HTML ---

function escapeHtml(text: string): string {
  return text
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;');
}

// --- Programmatic Highlighting (Mode B) ---

function highlightToHtml(code: string, lang: string): string | null {
  const parser = getParser(lang);
  if (!parser) return null;

  const tree = parser.parse(code);
  let html = '';
  let pos = 0;

  highlightTree(tree, classHighlighter, (from: number, to: number, classes: string) => {
    if (from > pos) html += escapeHtml(code.slice(pos, from));
    html += `<span class="${classes}">${escapeHtml(code.slice(from, to))}</span>`;
    pos = to;
  });

  if (pos < code.length) html += escapeHtml(code.slice(pos));
  return html;
}

// --- EditorView Instance Management (Mode A) ---

const editorInstances = new Map<string, EditorView>();
const themeCompartments = new Map<string, Compartment>();

const baseThemeDark = EditorView.theme({
  '&': {
    backgroundColor: 'transparent',
    color: '#d0d0d0',
    fontSize: '13px',
    fontFamily: "'JetBrains Mono', var(--font-mono)",
    fontWeight: '500',
  },
  '.cm-content': {
    caretColor: '#f0f0f0',
    padding: '8px 0',
    fontFamily: "'JetBrains Mono', var(--font-mono)",
    fontSize: '13px',
    fontWeight: '500',
  },
  '.cm-gutters': {
    backgroundColor: 'transparent',
    color: '#808080',
    border: 'none',
    minWidth: '40px',
  },
  '.cm-lineNumbers .cm-gutterElement': {
    padding: '0 8px 0 4px',
    minWidth: '32px',
    fontSize: '13px',
  },
  '.cm-activeLine': {
    backgroundColor: 'transparent',
  },
  '.cm-cursor': {
    borderLeftColor: 'var(--text-primary, #c9d1d9)',
  },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: 'rgba(56, 139, 253, 0.2)',
  },
  '.cm-line': {
    padding: '0 8px',
  },
}, { dark: true });

const baseThemeLight = EditorView.theme({
  '&': {
    backgroundColor: 'transparent',
    color: '#202020',
    fontSize: '13px',
    fontFamily: "'JetBrains Mono', var(--font-mono)",
    fontWeight: '500',
  },
  '.cm-content': {
    caretColor: '#202020',
    padding: '8px 0',
    fontFamily: "'JetBrains Mono', var(--font-mono)",
    fontSize: '13px',
    fontWeight: '500',
  },
  '.cm-gutters': {
    backgroundColor: 'transparent',
    color: '#888888',
    border: 'none',
    minWidth: '40px',
  },
  '.cm-lineNumbers .cm-gutterElement': {
    padding: '0 8px 0 4px',
    minWidth: '32px',
    fontSize: '13px',
  },
  '.cm-activeLine': {
    backgroundColor: 'transparent',
  },
  '.cm-cursor': {
    borderLeftColor: 'var(--text-primary, #1f2328)',
  },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground': {
    backgroundColor: 'rgba(56, 139, 253, 0.15)',
  },
  '.cm-line': {
    padding: '0 8px',
  },
}, { dark: false });

function getCurrentTheme() {
  const theme = document.documentElement.getAttribute('data-theme');
  return theme === 'light' ? baseThemeLight : baseThemeDark;
}

// --- Window Functions ---

(window as any).createCodeMirrorView = function (elementId: string, content: string, language: string, readOnly: boolean) {
  // Destroy existing instance if any
  const existing = editorInstances.get(elementId);
  if (existing) {
    existing.destroy();
    editorInstances.delete(elementId);
    themeCompartments.delete(elementId);
  }

  const el = document.getElementById(elementId);
  if (!el) return;

  // Clear container
  el.innerHTML = '';

  const themeCompartment = new Compartment();
  themeCompartments.set(elementId, themeCompartment);

  const extensions = [
    lineNumbers(),
    highlightSpecialChars(),
    drawSelection(),
    syntaxHighlighting(classHighlighter),
    themeCompartment.of(getCurrentTheme()),
    EditorView.editable.of(!readOnly),
    EditorState.readOnly.of(readOnly),
    EditorView.lineWrapping,
  ];

  const langSupport = getLanguageSupport(language);
  if (langSupport) {
    extensions.push(langSupport);
  }

  const state = EditorState.create({
    doc: content,
    extensions,
  });

  const view = new EditorView({
    state,
    parent: el,
  });

  editorInstances.set(elementId, view);
};

(window as any).destroyCodeMirrorView = function (elementId: string) {
  const view = editorInstances.get(elementId);
  if (view) {
    view.destroy();
    editorInstances.delete(elementId);
    themeCompartments.delete(elementId);
  }
};

(window as any).enhanceMarkdownCodeBlocks = function (containerId: string, isStreaming: boolean) {
  const container = document.getElementById(containerId);
  if (!container) return;

  const codeBlocks = container.querySelectorAll('.markdown-body pre > code');
  const total = codeBlocks.length;

  codeBlocks.forEach(function (codeEl, index) {
    // During streaming, skip the last block (may be incomplete)
    if (isStreaming && index === total - 1) return;
    // Skip already-enhanced blocks
    if ((codeEl as HTMLElement).dataset.enhanced === 'true') return;

    const pre = codeEl.parentElement;
    if (!pre || pre.parentElement?.classList.contains('code-block-wrapper')) return;

    // Detect language from class (Markdig outputs "language-xxx")
    let lang = '';
    let langDisplay = '';
    codeEl.classList.forEach(function (cls: string) {
      if (cls.startsWith('language-')) {
        lang = cls.replace('language-', '');
      }
    });
    if (lang) {
      langDisplay = (_mdLangNames as any)[lang.toLowerCase()] || lang;
    }

    // Create wrapper
    const wrapper = document.createElement('div');
    wrapper.className = 'code-block-wrapper';

    // Create header
    const header = document.createElement('div');
    header.className = langDisplay ? 'code-block-header' : 'code-block-header code-block-header-no-lang';

    const langLabel = document.createElement('span');
    langLabel.className = 'code-lang-label';
    langLabel.textContent = langDisplay;

    const copyBtn = document.createElement('button');
    copyBtn.className = 'code-copy-btn';
    copyBtn.innerHTML = _copyIconSvg + '<span>복사</span>';
    copyBtn.addEventListener('click', function (e: Event) {
      e.stopPropagation();
      (window as any).copyCodeToClipboard(copyBtn, (codeEl as HTMLElement).innerText);
    });

    header.appendChild(langLabel);
    header.appendChild(copyBtn);

    // Wrap pre in wrapper
    pre.parentElement!.insertBefore(wrapper, pre);
    wrapper.appendChild(header);
    wrapper.appendChild(pre);

    // Apply syntax highlighting via CodeMirror/Lezer
    try {
      const text = codeEl.textContent || '';
      const highlighted = highlightToHtml(text, lang);
      if (highlighted) {
        (codeEl as HTMLElement).innerHTML = highlighted;
      }
    } catch (e) { /* ignore */ }

    (codeEl as HTMLElement).dataset.enhanced = 'true';
  });
};

(window as any).highlightDiffBlock = function (elementId: string, language: string) {
  const el = document.getElementById(elementId);
  if (!el) return;

  const codeSpans = el.querySelectorAll('.diff-code');
  codeSpans.forEach(function (span) {
    if ((span as HTMLElement).dataset.highlighted) return;
    const text = span.textContent;
    if (!text) return;
    try {
      const highlighted = highlightToHtml(text, language);
      if (highlighted) {
        (span as HTMLElement).innerHTML = highlighted;
      }
      (span as HTMLElement).dataset.highlighted = 'true';
    } catch (e) { /* ignore */ }
  });
};

// Keep setTheme — remove hljs switching, add CM theme updates
(window as any).setTheme = function (theme: string) {
  document.documentElement.setAttribute('data-theme', theme);

  // Update all live CodeMirror editor instances
  const newTheme = theme === 'light' ? baseThemeLight : baseThemeDark;
  editorInstances.forEach((view, id) => {
    const compartment = themeCompartments.get(id);
    if (compartment) {
      view.dispatch({
        effects: compartment.reconfigure(newTheme),
      });
    }
  });
};

// --- Preserved utilities (not hljs-related) ---

const _mdLangNames: Record<string, string> = {
  'csharp': 'C#', 'javascript': 'JavaScript', 'typescript': 'TypeScript',
  'python': 'Python', 'rust': 'Rust', 'go': 'Go', 'java': 'Java',
  'cpp': 'C++', 'c': 'C', 'ruby': 'Ruby', 'bash': 'Bash', 'sh': 'Shell',
  'shell': 'Shell', 'powershell': 'PowerShell', 'json': 'JSON',
  'yaml': 'YAML', 'xml': 'XML', 'html': 'HTML', 'css': 'CSS',
  'scss': 'SCSS', 'sql': 'SQL', 'swift': 'Swift', 'kotlin': 'Kotlin',
  'dart': 'Dart', 'php': 'PHP', 'lua': 'Lua', 'r': 'R',
  'dockerfile': 'Dockerfile', 'makefile': 'Makefile', 'toml': 'TOML',
  'ini': 'INI', 'graphql': 'GraphQL', 'markdown': 'Markdown',
  'diff': 'Diff', 'plaintext': 'Text', 'text': 'Text'
};

const _copyIconSvg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>';
const _checkIconSvg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>';

(window as any).copyCodeToClipboard = function (btn: HTMLElement, text: string) {
  navigator.clipboard.writeText(text).then(function () {
    const origHtml = btn.innerHTML;
    btn.innerHTML = _checkIconSvg + '<span>복사됨</span>';
    btn.classList.add('copied');
    setTimeout(function () {
      btn.innerHTML = origHtml;
      btn.classList.remove('copied');
    }, 1500);
  }).catch(function (err) {
    console.error('[Seoro] Copy failed:', err);
  });
};
