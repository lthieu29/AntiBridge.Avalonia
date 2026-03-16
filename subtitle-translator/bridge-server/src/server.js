/**
 * HTTP Server - Express API cho Bridge Server
 */

const express = require('express');
const cors = require('cors');
const config = require('./config');
const logger = require('./logger');
const translator = require('./translator');
const antigravity = require('./antigravity');
const cache = require('./cache');

function createServer() {
    const app = express();

    // CORS cho Chrome Extension
    app.use(cors({
        origin: (origin, callback) => {
            // Cho phép Chrome Extension, localhost, Udemy, và requests không có origin (curl, etc.)
            if (!origin ||
                origin.startsWith('chrome-extension://') ||
                origin.startsWith('http://localhost') ||
                origin.startsWith('http://127.0.0.1') ||
                origin.startsWith('https://www.udemy.com')) {
                callback(null, true);
            } else {
                logger.warn(`[CORS] Blocked origin: ${origin}`);
                callback(new Error('Not allowed by CORS'));
            }
        },
        methods: ['GET', 'POST'],
        allowedHeaders: ['Content-Type'],
    }));

    app.use(express.json({ limit: '10mb' }));

    // === GET /api/status ===
    app.get('/api/status', (req, res) => {
        const status = translator.getStatus();
        logger.debug(`[API] GET /api/status → ${JSON.stringify(status)}`);
        res.json(status);
    });

    // === POST /api/translate-vtt (SSE streaming) ===
    app.post('/api/translate-vtt', async (req, res) => {
        const { vttContent, sourceLang } = req.body;

        if (!vttContent || typeof vttContent !== 'string') {
            return res.status(400).json({ error: 'vttContent is required (string)' });
        }

        if (translator.isTranslating) {
            return res.status(409).json({ error: 'Đang dịch, vui lòng chờ hoặc hủy' });
        }

        logger.info(`[API] POST /api/translate-vtt — ${vttContent.length} chars`);

        // Setup SSE
        res.writeHead(200, {
            'Content-Type': 'text/event-stream',
            'Cache-Control': 'no-cache',
            'Connection': 'keep-alive',
        });

        function sendSSE(event, data) {
            res.write(`event: ${event}\ndata: ${JSON.stringify(data)}\n\n`);
        }

        // Lắng nghe events từ translator
        const onProgress = (data) => sendSSE('progress', data);
        const onComplete = (data) => {
            sendSSE('complete', data);
            cleanup();
            res.end();
        };
        const onCancelled = () => {
            sendSSE('cancelled', { message: 'Đã hủy' });
            cleanup();
            res.end();
        };

        function cleanup() {
            translator.removeListener('progress', onProgress);
            translator.removeListener('complete', onComplete);
            translator.removeListener('cancelled', onCancelled);
        }

        translator.on('progress', onProgress);
        translator.on('complete', onComplete);
        translator.on('cancelled', onCancelled);

        // Client disconnect
        req.on('close', () => {
            logger.info('[API] Client disconnected');
            cleanup();
        });

        // Bắt đầu dịch
        try {
            translator.resetCancel();
            await translator.translateVtt(vttContent);
        } catch (err) {
            logger.error(`[API] Translation error: ${err.message}`);
            sendSSE('error', { message: err.message });
            cleanup();
            res.end();
        }
    });

    // === POST /api/cancel ===
    app.post('/api/cancel', (req, res) => {
        logger.info('[API] POST /api/cancel');
        translator.cancel();
        res.json({ success: true });
    });

    // === GET / (health check) ===
    app.get('/', (req, res) => {
        res.json({
            name: 'Subtitle Translator Bridge Server',
            version: '1.0.0',
            status: translator.getStatus(),
        });
    });

    // === POST /api/cache/get ===
    app.post('/api/cache/get', (req, res) => {
        const { vttContent } = req.body;
        if (!vttContent) {
            return res.status(400).json({ error: 'vttContent is required' });
        }
        const cached = cache.get(vttContent);
        if (cached) {
            logger.info(`[API] Cache HIT: ${cached.translatedVtt.length} chars`);
            res.json({ hit: true, translatedVtt: cached.translatedVtt });
        } else {
            res.json({ hit: false });
        }
    });

    // === POST /api/cache/set ===
    app.post('/api/cache/set', (req, res) => {
        const { vttContent, translatedVtt } = req.body;
        if (!vttContent || !translatedVtt) {
            return res.status(400).json({ error: 'vttContent and translatedVtt are required' });
        }
        cache.set(vttContent, 'vi', translatedVtt);
        logger.info(`[API] Cache SET: ${translatedVtt.length} chars`);
        res.json({ success: true });
    });

    return app;
}

module.exports = { createServer };
