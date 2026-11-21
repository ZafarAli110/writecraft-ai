# WriteCraft.AI

A private, 100% local AI writing, speech-to-text, and semantic bookmark studio built with **ASP.NET Core 8**, **LLamaSharp**, **Whisper.net**, **SQLite**, and **browser-side WASM/ONNX** models. No cloud API keys, no telemetry, no data leaving your machine.

## Key Features

- **Local LLM Writing Studio (`LLamaSharp` + GGUF):**
  - Grammar checking, email drafting, text cleanup, writing assistant, and an SSE-streaming Git Assistant powered by local quantized GGUF models (default: `Qwen2.5-7B-Instruct-Q4_K_M.gguf`).
- **Roman Urdu ↔ English Translation:**
  - Glossary-guided prompt injection (`urdu-vocabulary.json`) with smart relevant-word selection and few-shot example ranking.
- **Local Speech-to-Text (`Whisper.net`):**
  - Offline audio transcription using local `ggml` Whisper models.
- **Hybrid Semantic Bookmark Search:**
  - Parses Chrome, Edge, Firefox, and Safari bookmarks into a local SQLite vector store (`BookmarkVectorDb`), paired with browser-side `all-MiniLM-L6-v2` (384-dim) WASM/ONNX embeddings (`COOP`/`COEP` headers enabled).
- **Browser-Side Edge AI Utilities:**
  - Client-side TTS, background removal, image segmentation, and zero-shot classification views.

## Tech Stack

- **Backend:** ASP.NET Core 8 (`net8.0`), MVC + Razor Views, `LLamaSharp 0.25.0`, `Whisper.net 1.9.0`, `System.Data.SQLite`
- **Client / Edge AI:** Vanilla JS PWA, Transformers.js / ONNX Runtime Web (WASM/WebGPU)
- **Testing:** xUnit (`tests/WriteCraftAI.Tests`)

## Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer, e.g., .NET 10 SDK)
- A local GGUF model placed at `AIModels/Qwen2.5-7B-Instruct-Q4_K_M.gguf` (configurable in `config.json`)
- Optional: Whisper model `AIModels/ggml-small.bin` for speech-to-text

### Run Locally

```bash
git clone https://github.com/ZafarAli110/writecraft-ai.git
cd writecraft-ai
dotnet run
```

The app starts on `http://localhost:5555`.

### Run Tests

```bash
dotnet test tests/WriteCraftAI.Tests/WriteCraftAI.Tests.csproj
```

## License

[MIT](LICENSE)
