// Simple Markdown Renderer - Streaming-safe
class MarkdownRenderer {
    constructor() {
        this.styles = `
            .md-strong { font-weight: 600; display: block; margin: 0.5rem 0; }
            .md-em { font-style: italic; }
            .md-code { background: rgba(100, 116, 139, 0.2); padding: 0.2rem 0.4rem; border-radius: 0.25rem; font-family: monospace; }
            .md-list { margin: 0.5rem 0; padding-left: 1.5rem; list-style-type: disc; }
            .md-list li { margin: 0.25rem 0; }
            .md-text { display: block; margin: 0.25rem 0; }
        `;
        this.addStyles();
    }

    addStyles() {
        if (!document.getElementById('md-styles')) {
            const style = document.createElement('style');
            style.id = 'md-styles';
            style.textContent = this.styles;
            document.head.appendChild(style);
        }
    }

    render(text) {
        if (!text) return '';

        // CRITICAL: Process complete lines only
        // Split by actual newlines, not just \n in string
        const lines = text.split('\n');
        const result = [];
        let inList = false;
        let listItems = [];

        for (let i = 0; i < lines.length; i++) {
            let line = lines[i];
            const trimmed = line.trim();

            // Skip completely empty lines
            if (!trimmed) {
                if (inList) {
                    result.push(this.wrapList(listItems));
                    listItems = [];
                    inList = false;
                }
                result.push('<br>');
                continue;
            }

            // Check for headers - MUST be at start of line
            if (trimmed.startsWith('### ')) {
                if (inList) {
                    result.push(this.wrapList(listItems));
                    listItems = [];
                    inList = false;
                }
                const headerText = trimmed.substring(4).trim();
                result.push(`<strong class="md-strong">${this.escapeHtml(headerText)}</strong>`);
                continue;
            }
            if (trimmed.startsWith('## ')) {
                if (inList) {
                    result.push(this.wrapList(listItems));
                    listItems = [];
                    inList = false;
                }
                const headerText = trimmed.substring(3).trim();
                result.push(`<strong class="md-strong">${this.escapeHtml(headerText)}</strong>`);
                continue;
            }
            if (trimmed.startsWith('# ')) {
                if (inList) {
                    result.push(this.wrapList(listItems));
                    listItems = [];
                    inList = false;
                }
                const headerText = trimmed.substring(2).trim();
                result.push(`<strong class="md-strong">${this.escapeHtml(headerText)}</strong>`);
                continue;
            }

            // List items
            if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
                inList = true;
                const itemText = trimmed.substring(2).trim();
                listItems.push(this.processInline(itemText));
                continue;
            }

            // Ordered lists
            const orderedMatch = trimmed.match(/^(\d+)\.\s+(.+)$/);
            if (orderedMatch) {
                inList = true;
                listItems.push(this.processInline(orderedMatch[2]));
                continue;
            }

            // Regular text line
            if (inList) {
                result.push(this.wrapList(listItems));
                listItems = [];
                inList = false;
            }

            // Process as regular text with inline formatting
            const processed = this.processInline(trimmed);
            result.push(`<div class="md-text">${processed}</div>`);
        }

        // Close any remaining list
        if (inList && listItems.length > 0) {
            result.push(this.wrapList(listItems));
        }

        return result.join('');
    }

    wrapList(items) {
        if (items.length === 0) return '';
        return `<ul class="md-list">${items.map(item => `<li>${item}</li>`).join('')}</ul>`;
    }

    processInline(text) {
        if (!text) return '';

        // Protect code blocks BEFORE escaping - use null character markers that won't be affected
        const codeBlocks = [];
        text = text.replace(/`([^`]+)`/g, (match, code) => {
            const index = codeBlocks.length;
            codeBlocks.push(code);
            return `\x00CODE${index}\x00`;
        });

        // Now escape HTML
        text = this.escapeHtml(text);

        // Bold + Italic combined
        text = text.replace(/\*\*\*(.+?)\*\*\*/g, '<strong><em>$1</em></strong>');
        text = text.replace(/___(.+?)___/g, '<strong><em>$1</em></strong>');

        // Bold
        text = text.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>');
        text = text.replace(/__([^_]+)__/g, '<strong>$1</strong>');

        // Italic - be more careful with underscore to avoid matching flags like --soft
        text = text.replace(/\*([^*]+)\*/g, '<em>$1</em>');
        text = text.replace(/(?<![a-zA-Z0-9-])_([^_]+)_(?![a-zA-Z0-9-])/g, '<em>$1</em>');

        // Links
        text = text.replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" target="_blank" rel="noopener">$1</a>');

        // Restore code blocks with proper escaping
        codeBlocks.forEach((code, index) => {
            const escapedCode = this.escapeHtml(code);
            text = text.replace(`\x00CODE${index}\x00`, `<code class="md-code">${escapedCode}</code>`);
        });

        return text;
    }

    escapeHtml(text) {
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Extract clean plain text without markdown symbols
    extractPlainText(html, originalRawText) {
        // Use the original raw text and strip markdown
        if (!originalRawText) return '';

        let plain = originalRawText;

        // Remove markdown headers (### ## #)
        plain = plain.replace(/^###\s+/gm, '');
        plain = plain.replace(/^##\s+/gm, '');
        plain = plain.replace(/^#\s+/gm, '');

        // Remove markdown list markers (- *)
        plain = plain.replace(/^[-*]\s+/gm, '� ');

        // Remove ordered list numbers
        plain = plain.replace(/^\d+\.\s+/gm, '� ');

        // Remove bold/italic markers
        plain = plain.replace(/\*\*\*(.+?)\*\*\*/g, '$1');
        plain = plain.replace(/\*\*(.+?)\*\*/g, '$1');
        plain = plain.replace(/\*(.+?)\*/g, '$1');
        plain = plain.replace(/___(.+?)___/g, '$1');
        plain = plain.replace(/__(.+?)__/g, '$1');
        plain = plain.replace(/_(.+?)_/g, '$1');

        // Remove inline code backticks
        plain = plain.replace(/`([^`]+)`/g, '$1');

        // Remove links but keep text
        plain = plain.replace(/\[([^\]]+)\]\([^)]+\)/g, '$1');

        // Clean up excessive newlines
        // Replace 3+ newlines with just 2 (one blank line)
        plain = plain.replace(/\n{3,}/g, '\n\n');

        // Trim leading/trailing whitespace
        plain = plain.trim();

        return plain;
    }
}

// Make available globally
window.MarkdownRenderer = MarkdownRenderer;