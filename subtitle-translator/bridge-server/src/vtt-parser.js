/**
 * VTT Parser - Parse và tái tạo WebVTT files
 */

/**
 * Parse WebVTT string → danh sách cue
 * @param {string} vttString - Nội dung file VTT
 * @returns {{ cues: Array<{index: number, startTime: string, endTime: string, text: string}>, header: string }}
 */
function parseVtt(vttString) {
    const lines = vttString.replace(/\r\n/g, '\n').split('\n');
    const cues = [];
    let header = '';
    let i = 0;

    // Đọc header WEBVTT
    if (lines[0] && lines[0].trim().startsWith('WEBVTT')) {
        header = lines[0].trim();
        i = 1;
        // Skip header metadata lines cho đến khi gặp dòng trống
        while (i < lines.length && lines[i].trim() !== '') {
            header += '\n' + lines[i];
            i++;
        }
    }

    // Skip STYLE/NOTE blocks và parse cues
    let cueIndex = 0;
    while (i < lines.length) {
        // Bỏ qua dòng trống
        while (i < lines.length && lines[i].trim() === '') i++;
        if (i >= lines.length) break;

        // Bỏ qua NOTE blocks
        if (lines[i].trim().startsWith('NOTE')) {
            while (i < lines.length && lines[i].trim() !== '') i++;
            continue;
        }

        // Bỏ qua STYLE blocks
        if (lines[i].trim().startsWith('STYLE')) {
            while (i < lines.length && lines[i].trim() !== '') i++;
            continue;
        }

        // Kiểm tra có phải cue number (tùy chọn trong VTT)
        let cueNumber = null;
        if (lines[i].trim().match(/^\d+$/) && i + 1 < lines.length && lines[i + 1].includes('-->')) {
            cueNumber = parseInt(lines[i].trim());
            i++;
        }

        // Kiểm tra timestamp line
        const timestampMatch = lines[i]?.trim().match(/^(\d{2}:\d{2}:\d{2}\.\d{3})\s*-->\s*(\d{2}:\d{2}:\d{2}\.\d{3})/);
        if (!timestampMatch) {
            i++;
            continue;
        }

        const startTime = timestampMatch[1];
        const endTime = timestampMatch[2];
        i++;

        // Đọc text lines cho đến dòng trống hoặc hết file
        const textLines = [];
        while (i < lines.length && lines[i].trim() !== '') {
            textLines.push(lines[i].trim());
            i++;
        }

        cueIndex++;
        cues.push({
            index: cueNumber || cueIndex,
            startTime,
            endTime,
            text: textLines.join(' '), // Nối nhiều dòng bằng dấu cách
        });
    }

    return { cues, header: header || 'WEBVTT' };
}

/**
 * Phân chunk danh sách cue
 * @param {Array} cues - Danh sách cue
 * @param {number} chunkSize - Số cue mỗi chunk
 * @returns {Array<Array>} Mảng các chunk
 */
function chunkCues(cues, chunkSize = 20) {
    const chunks = [];
    for (let i = 0; i < cues.length; i += chunkSize) {
        chunks.push(cues.slice(i, i + chunkSize));
    }
    return chunks;
}

/**
 * Tái tạo VTT từ cue gốc + bản dịch
 * @param {string} header - VTT header
 * @param {Array} originalCues - Cue gốc
 * @param {Map<number, string>} translations - Map index → translated text
 * @returns {string} VTT string đã dịch
 */
function reconstructVtt(header, originalCues, translations) {
    let vtt = header + '\n\n';

    for (const cue of originalCues) {
        const translatedText = translations.get(cue.index) || cue.text;
        vtt += `${cue.index}\n`;
        vtt += `${cue.startTime} --> ${cue.endTime}\n`;
        vtt += `${translatedText}\n\n`;
    }

    return vtt.trim() + '\n';
}

/**
 * Parse timestamp VTT → giây
 * @param {string} timestamp - "00:01:23.456"
 * @returns {number} Số giây
 */
function timestampToSeconds(timestamp) {
    const parts = timestamp.split(':');
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    const secondsParts = parts[2].split('.');
    const seconds = parseInt(secondsParts[0]);
    const ms = parseInt(secondsParts[1]);
    return hours * 3600 + minutes * 60 + seconds + ms / 1000;
}

module.exports = { parseVtt, chunkCues, reconstructVtt, timestampToSeconds };
