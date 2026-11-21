// WAV audio encoding utilities
// Adapted from https://www.npmjs.com/package/audiobuffer-to-wav

export function encodeWAV(samples) {
    const sampleRate = 16000;
    const buffer = new ArrayBuffer(44 + samples.length * 4);
    const view = new DataView(buffer);

    // RIFF identifier
    writeString(view, 0, 'RIFF');
    // RIFF chunk length
    view.setUint32(4, 36 + samples.length * 4, true);
    // RIFF type
    writeString(view, 8, 'WAVE');
    // format chunk identifier
    writeString(view, 12, 'fmt ');
    // format chunk length
    view.setUint32(16, 16, true);
    // sample format (float32)
    view.setUint16(20, 3, true);
    // channel count (mono)
    view.setUint16(22, 1, true);
    // sample rate
    view.setUint32(24, sampleRate, true);
    // byte rate (sample rate * block align)
    view.setUint32(28, sampleRate * 4, true);
    // block align (channel count * bytes per sample)
    view.setUint16(32, 4, true);
    // bits per sample
    view.setUint16(34, 32, true);
    // data chunk identifier
    writeString(view, 36, 'data');
    // data chunk length
    view.setUint32(40, samples.length * 4, true);

    // Write samples
    let offset = 44;
    for (let i = 0; i < samples.length; ++i, offset += 4) {
        view.setFloat32(offset, samples[i], true);
    }

    return buffer;
}

function writeString(view, offset, string) {
    for (let i = 0; i < string.length; ++i) {
        view.setUint8(offset + i, string.charCodeAt(i));
    }
}