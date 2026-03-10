/**
 * Prompt Builder Unit Tests
 */

const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const { buildTranslationPrompt, parseTranslationResponse, mapTranslationsToCues } = require('../src/prompt-builder');

describe('buildTranslationPrompt', () => {
    it('should build numbered prompt from cues', () => {
        const cues = [
            { index: 1, text: 'Hello world' },
            { index: 2, text: 'How are you' },
            { index: 3, text: 'Goodbye' },
        ];

        const prompt = buildTranslationPrompt(cues);

        assert.ok(prompt.includes('1. Hello world'));
        assert.ok(prompt.includes('2. How are you'));
        assert.ok(prompt.includes('3. Goodbye'));
        assert.ok(prompt.includes('tiếng Việt'));
    });

    it('should use custom target language', () => {
        const cues = [{ index: 1, text: 'Hello' }];
        const prompt = buildTranslationPrompt(cues, 'tiếng Nhật');
        assert.ok(prompt.includes('tiếng Nhật'));
    });

    it('should include instruction', () => {
        const cues = [{ index: 1, text: 'Hello' }];
        const prompt = buildTranslationPrompt(cues);
        assert.ok(prompt.includes('Dịch'));
        assert.ok(prompt.includes('phụ đề'));
    });
});

describe('parseTranslationResponse', () => {
    it('should parse numbered response with dots', () => {
        const response = `1. Xin chào thế giới
2. Bạn khỏe không
3. Tạm biệt`;

        const { translations, parsed, missing } = parseTranslationResponse(response, 3);

        assert.equal(parsed, 3);
        assert.equal(missing.length, 0);
        assert.equal(translations.get(1), 'Xin chào thế giới');
        assert.equal(translations.get(2), 'Bạn khỏe không');
        assert.equal(translations.get(3), 'Tạm biệt');
    });

    it('should parse numbered response with parentheses', () => {
        const response = `1) Xin chào
2) Tạm biệt`;

        const { translations, parsed } = parseTranslationResponse(response, 2);
        assert.equal(parsed, 2);
        assert.equal(translations.get(1), 'Xin chào');
    });

    it('should handle extra text/explanation in response', () => {
        const response = `Dưới đây là bản dịch:

1. Xin chào thế giới
2. Bạn khỏe không

Hy vọng bản dịch hữu ích!`;

        const { translations, parsed } = parseTranslationResponse(response, 2);
        assert.equal(parsed, 2);
        assert.equal(translations.get(1), 'Xin chào thế giới');
    });

    it('should report missing translations', () => {
        const response = `1. Xin chào
3. Tạm biệt`;

        const { parsed, missing } = parseTranslationResponse(response, 3);
        assert.equal(parsed, 2);
        assert.deepEqual(missing, [2]);
    });

    it('should ignore out-of-range numbers', () => {
        const response = `0. Không hợp lệ
1. Hợp lệ
99. Ngoài phạm vi`;

        const { parsed } = parseTranslationResponse(response, 2);
        assert.equal(parsed, 1);
    });

    it('should handle empty response', () => {
        const { parsed, missing } = parseTranslationResponse('', 3);
        assert.equal(parsed, 0);
        assert.deepEqual(missing, [1, 2, 3]);
    });
});

describe('mapTranslationsToCues', () => {
    it('should map 1-based translations to original cue indices', () => {
        const parsedTranslations = new Map([
            [1, 'Xin chào'],
            [2, 'Tạm biệt'],
        ]);
        const originalCues = [
            { index: 15, text: 'Hello' },
            { index: 16, text: 'Goodbye' },
        ];

        const result = mapTranslationsToCues(parsedTranslations, originalCues);

        assert.equal(result.get(15), 'Xin chào');
        assert.equal(result.get(16), 'Tạm biệt');
    });

    it('should handle partial translations', () => {
        const parsedTranslations = new Map([
            [1, 'Dịch'],
        ]);
        const originalCues = [
            { index: 5, text: 'A' },
            { index: 6, text: 'B' },
        ];

        const result = mapTranslationsToCues(parsedTranslations, originalCues);

        assert.equal(result.size, 1);
        assert.equal(result.get(5), 'Dịch');
        assert.equal(result.has(6), false);
    });
});
