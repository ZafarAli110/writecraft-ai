// Shared output handler with markdown rendering
class OutputHandler {
    constructor(outputElement) {
        this.output = outputElement;
        this.rawText = '';
        this.mdRenderer = new MarkdownRenderer();
    }

    clear() {
        this.rawText = '';
        this.output.innerHTML = '';
    }

    appendToken(token) {
        // CRITICAL: Preserve newlines exactly as received
        this.rawText += token;

        // Debug: Log when we receive newlines
        if (token.includes('\n')) {
            console.log('[OutputHandler] Received newline in token. Token:', JSON.stringify(token));
            console.log('[OutputHandler] Current raw text length:', this.rawText.length);
            console.log('[OutputHandler] Last 50 chars:', JSON.stringify(this.rawText.slice(-50)));
        }

        // Render markdown from complete raw text
        const renderedHtml = this.mdRenderer.render(this.rawText);

        // Update display
        this.output.innerHTML = renderedHtml;

        // Auto-scroll to bottom
        this.output.scrollTop = this.output.scrollHeight;
    }

    setError(message) {
        this.rawText = message;
        this.output.innerHTML = `<div class="text-danger">${this.mdRenderer.escapeHtml(message)}</div>`;
    }

    getRawText() {
        // Return clean plain text without markdown symbols
        return this.mdRenderer.extractPlainText(this.output.innerHTML, this.rawText);
    }

    setText(text) {
        this.rawText = text;
        this.output.innerHTML = this.mdRenderer.render(text);
    }
}

// Make available globally
window.OutputHandler = OutputHandler;