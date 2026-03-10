/**
 * Entry Point - Khởi động Bridge Server + CDP Connection
 */

const config = require('./config');
const logger = require('./logger');
const antigravity = require('./antigravity');
const { createServer } = require('./server');

async function main() {
    logger.info('========================================');
    logger.info(' Subtitle Translator Bridge Server');
    logger.info('========================================');
    logger.info(`CDP URL: ${config.cdpUrl}`);
    logger.info(`Bridge Port: ${config.bridgePort}`);
    logger.info(`Response Timeout: ${config.responseTimeout / 1000}s`);
    logger.info(`Chunk Size: ${config.chunkSize} cues`);
    logger.info('');

    // 1. Kết nối CDP
    logger.info('Đang kết nối tới Antigravity IDE...');
    const connected = await antigravity.connect();

    if (!connected) {
        logger.error('❌ Không thể kết nối Antigravity IDE!');
        logger.error('Đảm bảo Antigravity đang chạy với --remote-debugging-port=9222');
        logger.error('Chạy: OPEN_ANTIGRAVITY.bat');
        // Vẫn start server để extension có thể kiểm tra status
        logger.warn('Server sẽ chạy nhưng translation sẽ không hoạt động');
    } else {
        logger.info('✅ Đã kết nối Antigravity IDE!');
    }

    // 2. Start HTTP Server
    const app = createServer();

    app.listen(config.bridgePort, () => {
        logger.info('');
        logger.info(`✅ Bridge Server đang chạy tại http://localhost:${config.bridgePort}`);
        logger.info('');
        logger.info('Endpoints:');
        logger.info(`  GET  http://localhost:${config.bridgePort}/api/status`);
        logger.info(`  POST http://localhost:${config.bridgePort}/api/translate-vtt`);
        logger.info(`  POST http://localhost:${config.bridgePort}/api/cancel`);
        logger.info('');
        logger.info('Sẵn sàng nhận request từ Chrome Extension!');
    });

    // Graceful shutdown
    process.on('SIGINT', async () => {
        logger.info('\nĐang tắt...');
        await antigravity.disconnect();
        process.exit(0);
    });

    process.on('SIGTERM', async () => {
        await antigravity.disconnect();
        process.exit(0);
    });
}

main().catch(err => {
    logger.error('Fatal error:', err.message);
    process.exit(1);
});
