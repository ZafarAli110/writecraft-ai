using Microsoft.AspNetCore.Mvc;
using WriteCraftAI.Services;
using System.Text.Json;

namespace WriteCraftAI.Controllers;

public class AsrController : Controller
{
    private readonly WhisperService _whisperService;
    private readonly ILogger<AsrController> _logger;

    public AsrController(WhisperService whisperService, ILogger<AsrController> logger)
    {
        _whisperService = whisperService;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpGet("/api/asr/health")]
public IActionResult Health()
{
    // ✅ Check file directly, not service state
    var modelPath = Path.Combine("AIModels", "ggml-small.bin");
    var modelExists = System.IO.File.Exists(modelPath);
    var fullPath = Path.GetFullPath(modelPath);
    
    // Check FFmpeg
    var ffmpegPath = GetFfmpegPath();
    var ffmpegExists = System.IO.File.Exists(ffmpegPath);

    _logger.LogInformation($"ASR Health: Model exists={modelExists}, FFmpeg={ffmpegExists}");

    return Ok(new { 
        ready = modelExists,
        modelPath = fullPath,
        ffmpeg = ffmpegExists
    });
}

private string GetFfmpegPath()
{
    var localPath = Path.Combine(Directory.GetCurrentDirectory(), "External", "ffmpeg.exe");
    return localPath;
}



    /// <summary>
    /// SSE streaming endpoint for transcription with progress updates
    /// </summary>
    [HttpPost("/api/asr/transcribe-stream")]
    public async Task TranscribeStream(IFormFile audio)
    {
        // Set up SSE response
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        if (audio == null || audio.Length == 0)
        {
            await SendEventAsync(new { stage = "error", error = "No audio file uploaded" });
            return;
        }

        var tempWebM = Path.GetTempFileName() + ".webm";
        var tempWav = string.Empty;
        double conversionTime = 0;
        double transcriptionTime = 0;

        try
        {
            // Save uploaded file
            using (var stream = new FileStream(tempWebM, FileMode.Create))
            {
                await audio.CopyToAsync(stream);
            }

            // Stage 1: Audio conversion
            await SendEventAsync(new { 
                stage = "converting", 
                progress = 30, 
                status = "Converting audio format..." 
            });
            
            var (success, wavPath, convTime, error) = await _whisperService.ConvertToWavAsync(tempWebM);
            tempWav = wavPath;
            conversionTime = convTime;

            if (!success)
            {
                await SendEventAsync(new { stage = "error", error = error ?? "Audio conversion failed" });
                return;
            }

            // Stage 2: Transcription (send conversion time so UI can update)
            await SendEventAsync(new { 
                stage = "transcribing", 
                progress = 60, 
                status = "Transcribing audio with Whisper...",
                timing = new { conversionTime }
            });
            
            using var audioStream = new FileStream(tempWav, FileMode.Open, FileAccess.Read);
            var (text, transTime) = await _whisperService.TranscribeAsync(audioStream);
            transcriptionTime = transTime;

            // Send complete with all timing first (so UI updates)
            await SendEventAsync(new { 
                stage = "complete", 
                progress = 100, 
                timing = new { conversionTime, transcriptionTime }
            });

            // Send result last
            await SendEventAsync(new { 
                stage = "result", 
                text
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed");
            await SendEventAsync(new { stage = "error", error = ex.Message });
        }
        finally
        {
            // Cleanup temp files
            try
            {
                if (System.IO.File.Exists(tempWebM)) System.IO.File.Delete(tempWebM);
                if (!string.IsNullOrEmpty(tempWav) && System.IO.File.Exists(tempWav)) System.IO.File.Delete(tempWav);
            }
            catch { /* ignore cleanup errors */ }
        }
    }

    /// <summary>
    /// Original non-streaming endpoint (kept for backwards compatibility)
    /// </summary>
    [HttpPost("/api/asr/transcribe")]
    public async Task<IActionResult> Transcribe(IFormFile audio)
    {
        if (audio == null || audio.Length == 0)
        {
            return BadRequest(new { error = "No audio file uploaded" });
        }

        var tempWebM = Path.GetTempFileName() + ".webm";
        var tempWav = string.Empty;

        try
        {
            // Save uploaded file
            using (var stream = new FileStream(tempWebM, FileMode.Create))
            {
                await audio.CopyToAsync(stream);
            }

            // Convert to WAV
            var (success, wavPath, conversionTime, error) = await _whisperService.ConvertToWavAsync(tempWebM);
            tempWav = wavPath;

            if (!success)
            {
                return BadRequest(new { error = error ?? "Audio conversion failed" });
            }

            // Transcribe
            using var audioStream = new FileStream(tempWav, FileMode.Open, FileAccess.Read);
            var (text, transcriptionTime) = await _whisperService.TranscribeAsync(audioStream);

            return Ok(new { 
                rawText = text, 
                conversionTime = $"{conversionTime:F0}ms",
                transcriptionTime = $"{transcriptionTime:F0}ms"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription failed");
            return StatusCode(500, new { error = ex.Message });
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempWebM)) System.IO.File.Delete(tempWebM);
                if (!string.IsNullOrEmpty(tempWav) && System.IO.File.Exists(tempWav)) System.IO.File.Delete(tempWav);
            }
            catch { }
        }
    }

    private async Task SendEventAsync(object data)
    {
        var json = JsonSerializer.Serialize(data);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }
}