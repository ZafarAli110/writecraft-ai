class StreamHandler {
    constructor(endpoint, requestBody, callbacks) {
        this.callbacks = callbacks;
        this.buffer = '';
        this.isFirstToken = true;
        this.isComplete = false; // Prevent late tokens
        this.startTime = performance.now();
        this.init(endpoint, requestBody);
    }

    async init(endpoint, requestBody) {
        try {
            const response = await fetch(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'text/event-stream'
                },
                body: JSON.stringify(requestBody)
            });

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const reader = response.body.getReader();
            const decoder = new TextDecoder();

            while (true) {
                const { done, value } = await reader.read();
                if (done) break;

                this.buffer += decoder.decode(value, { stream: true });
                this.processBuffer();
            }

            // Final processing
            this.processBuffer(true);
        } catch (error) {
            console.error('Stream error:', error);
            this.callbacks.onError?.(`Connection failed: ${error.message}`);
        }
    }

    processBuffer(isFinal = false) {
        // SSE messages are separated by double newlines
        const messages = this.buffer.split('\n\n');

        // Keep last incomplete message in buffer
        this.buffer = isFinal ? '' : messages.pop() || '';

        for (const message of messages) {
            if (!message.trim()) continue;
            this.parseMessage(message.trim());
        }
    }

    parseMessage(message) {
        const lines = message.split('\n');
        let eventType = 'token'; // default
        let data = '';

        for (const line of lines) {
            if (line.startsWith('event: ')) {
                eventType = line.substring(7).trim();
            } else if (line.startsWith('data: ')) {
                data = line.substring(6);
            }
        }

        try {
            switch (eventType) {
                case 'token':
                    if (this.isComplete) {
                        return; // Ignore tokens after
                    }
                    if (data) {
                        // CRITICAL: Unescape newlines that were escaped for SSE transport
                        const unescapedData = data.replace(/\\n/g, '\n').replace(/\\r/g, '\r');

                        this.callbacks.onToken?.(unescapedData);

                        if (this.isFirstToken) {
                            this.isFirstToken = false;
                            const latency = (performance.now() - this.startTime) / 1000;
                            this.callbacks.onFirstToken?.(latency);
                        }
                    }
                    break;

                case 'status':
                    this.callbacks.onStatus?.(data);
                    break;

                case 'complete':
                    this.isComplete = true; // Prevent further token processing
                    this.callbacks.onComplete?.(data);
                    break;

                case 'error':
                    this.callbacks.onError?.(data);
                    break;

                case 'firsttoken':
                    if (this.isFirstToken) {
                        this.isFirstToken = false;
                        const latency = (performance.now() - this.startTime) / 1000;
                        this.callbacks.onFirstToken?.(latency);
                    }
                    break;
            }
        } catch (error) {
            console.error(`Error processing ${eventType} event:`, error);
            this.callbacks.onError?.(`Processing error: ${error.message}`);
        }
    }
}

// Make available globally
window.StreamHandler = StreamHandler;