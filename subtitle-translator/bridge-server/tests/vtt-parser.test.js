/**
 * VTT Parser Unit Tests
 */

const { describe, it } = require('node:test');
const assert = require('node:assert/strict');
const { parseVtt, chunkCues, reconstructVtt, timestampToSeconds } = require('../src/vtt-parser');

// ==== Sample VTT Data ====

const SAMPLE_VTT = `WEBVTT

1
00:00:01.000 --> 00:00:04.000
Hello and welcome to this course

2
00:00:04.500 --> 00:00:08.000
Today we will learn about React

3
00:00:08.500 --> 00:00:12.000
Let's start with the basics
`;

const MULTILINE_VTT = `WEBVTT

1
00:00:01.000 --> 00:00:05.000
This is line one
and this is line two

2
00:00:06.000 --> 00:00:10.000
Single line here
`;

const VTT_WITH_STYLE = `WEBVTT

STYLE
::cue {
  color: white;
}

NOTE This is a comment

1
00:00:01.000 --> 00:00:04.000
First subtitle

2
00:00:05.000 --> 00:00:08.000
Second subtitle
`;

const NO_CUE_NUMBER_VTT = `WEBVTT

00:00:01.000 --> 00:00:04.000
Hello world

00:00:05.000 --> 00:00:08.000
Goodbye world
`;

// ==== Tests ====

describe('parseVtt', () => {
    it('should parse basic VTT with header', () => {
        const { cues, header } = parseVtt(SAMPLE_VTT);
        assert.equal(header, 'WEBVTT');
        assert.equal(cues.length, 3);
    });

    it('should extract correct timestamps', () => {
        const { cues } = parseVtt(SAMPLE_VTT);
        assert.equal(cues[0].startTime, '00:00:01.000');
        assert.equal(cues[0].endTime, '00:00:04.000');
        assert.equal(cues[2].startTime, '00:00:08.500');
        assert.equal(cues[2].endTime, '00:00:12.000');
    });

    it('should extract correct text', () => {
        const { cues } = parseVtt(SAMPLE_VTT);
        assert.equal(cues[0].text, 'Hello and welcome to this course');
        assert.equal(cues[1].text, 'Today we will learn about React');
    });

    it('should join multi-line cue text with space', () => {
        const { cues } = parseVtt(MULTILINE_VTT);
        assert.equal(cues[0].text, 'This is line one and this is line two');
        assert.equal(cues[1].text, 'Single line here');
    });

    it('should skip STYLE and NOTE blocks', () => {
        const { cues } = parseVtt(VTT_WITH_STYLE);
        assert.equal(cues.length, 2);
        assert.equal(cues[0].text, 'First subtitle');
    });

    it('should handle VTT without cue numbers', () => {
        const { cues } = parseVtt(NO_CUE_NUMBER_VTT);
        assert.equal(cues.length, 2);
        assert.equal(cues[0].index, 1);
        assert.equal(cues[1].index, 2);
    });

    it('should handle empty input', () => {
        const { cues } = parseVtt('');
        assert.equal(cues.length, 0);
    });

    it('should handle WEBVTT header only', () => {
        const { cues } = parseVtt('WEBVTT\n\n');
        assert.equal(cues.length, 0);
    });
});

describe('chunkCues', () => {
    it('should chunk correctly with exact division', () => {
        const cues = Array.from({ length: 40 }, (_, i) => ({ index: i + 1, text: `cue ${i + 1}` }));
        const chunks = chunkCues(cues, 20);
        assert.equal(chunks.length, 2);
        assert.equal(chunks[0].length, 20);
        assert.equal(chunks[1].length, 20);
    });

    it('should handle remainder in last chunk', () => {
        const cues = Array.from({ length: 50 }, (_, i) => ({ index: i + 1, text: `cue ${i + 1}` }));
        const chunks = chunkCues(cues, 20);
        assert.equal(chunks.length, 3);
        assert.equal(chunks[0].length, 20);
        assert.equal(chunks[1].length, 20);
        assert.equal(chunks[2].length, 10);
    });

    it('should handle fewer cues than chunk size', () => {
        const cues = [{ index: 1, text: 'hello' }];
        const chunks = chunkCues(cues, 20);
        assert.equal(chunks.length, 1);
        assert.equal(chunks[0].length, 1);
    });

    it('should handle empty array', () => {
        const chunks = chunkCues([], 20);
        assert.equal(chunks.length, 0);
    });
});

describe('reconstructVtt', () => {
    it('should reconstruct with translations', () => {
        const { cues, header } = parseVtt(SAMPLE_VTT);
        const translations = new Map([
            [1, 'Xin chào và chào mừng đến với khóa học này'],
            [2, 'Hôm nay chúng ta sẽ học về React'],
            [3, 'Hãy bắt đầu với những điều cơ bản'],
        ]);

        const result = reconstructVtt(header, cues, translations);

        assert.ok(result.startsWith('WEBVTT'));
        assert.ok(result.includes('Xin chào và chào mừng'));
        assert.ok(result.includes('00:00:01.000 --> 00:00:04.000'));
        assert.ok(result.includes('Hôm nay chúng ta'));
    });

    it('should keep original text for missing translations', () => {
        const { cues, header } = parseVtt(SAMPLE_VTT);
        const translations = new Map([
            [1, 'Translated line 1'],
        ]);

        const result = reconstructVtt(header, cues, translations);

        assert.ok(result.includes('Translated line 1'));
        assert.ok(result.includes('Today we will learn about React')); // Giữ nguyên
    });
});

describe('timestampToSeconds', () => {
    it('should convert timestamps correctly', () => {
        assert.equal(timestampToSeconds('00:00:01.000'), 1);
        assert.equal(timestampToSeconds('00:01:00.000'), 60);
        assert.equal(timestampToSeconds('01:00:00.000'), 3600);
        assert.equal(timestampToSeconds('00:00:01.500'), 1.5);
        assert.equal(timestampToSeconds('01:23:45.678'), 5025.678);
    });
});
