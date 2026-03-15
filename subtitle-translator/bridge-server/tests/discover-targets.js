/**
 * Diagnostic: Kết nối trực tiếp vào từng page CDP và tìm chat elements
 * KHÔNG dùng puppeteer - dùng raw CDP WebSocket
 * 
 * Usage: node tests/discover-targets.js
 */

const WebSocket = require('ws');

const CDP_URL = 'http://127.0.0.1:9222';

async function evalOnPage(wsUrl, expression) {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(wsUrl);
        const timeout = setTimeout(() => {
            ws.close();
            reject(new Error('Timeout'));
        }, 5000);

        ws.on('open', () => {
            ws.send(JSON.stringify({
                id: 1,
                method: 'Runtime.evaluate',
                params: { expression, returnByValue: true }
            }));
        });

        ws.on('message', (data) => {
            const msg = JSON.parse(data.toString());
            if (msg.id === 1) {
                clearTimeout(timeout);
                ws.close();
                resolve(msg.result?.result?.value);
            }
        });

        ws.on('error', (err) => {
            clearTimeout(timeout);
            reject(err);
        });
    });
}

async function main() {
    console.log('=== CDP Chat Panel Discovery ===\n');

    // Get targets
    const res = await fetch(`${CDP_URL}/json`);
    const targets = await res.json();

    const pages = targets.filter(t => t.type === 'page');
    console.log(`Found ${pages.length} pages\n`);

    for (const page of pages) {
        console.log(`\n--- Page: "${page.title}" ---`);
        console.log(`URL: ${page.url}`);
        console.log(`WS: ${page.webSocketDebuggerUrl}\n`);

        try {
            // Check for chat-like elements
            const result = await evalOnPage(page.webSocketDebuggerUrl, `
                (() => {
                    const info = {
                        title: document.title,
                        url: location.href.substring(0, 200),
                        bodyClass: document.body?.className?.substring(0, 200) || '',
                        totalElements: document.querySelectorAll('*').length,
                        
                        // Chat input indicators
                        textareas: document.querySelectorAll('textarea').length,
                        contentEditables: document.querySelectorAll('[contenteditable="true"]').length,
                        roleTextbox: document.querySelectorAll('[role="textbox"]').length,
                        
                        // AI response indicators
                        proseElements: document.querySelectorAll('[class*="prose"]').length,
                        markdownElements: document.querySelectorAll('[class*="markdown"]').length,
                        
                        // Frames/iframes
                        iframes: document.querySelectorAll('iframe').length,
                        webviews: document.querySelectorAll('webview').length,
                        
                        // VS Code specific
                        webviewFrames: document.querySelectorAll('.webview.ready iframe').length,
                        
                        // Interesting class names
                        interestingClasses: [],
                        
                        // All iframe srcs
                        iframeSrcs: [],
                        
                        // All frames info
                        frameUrls: []
                    };
                    
                    // Collect interesting classes
                    const classSet = new Set();
                    document.querySelectorAll('*').forEach(el => {
                        const cls = el.className?.toString?.() || '';
                        if (cls && (
                            cls.includes('chat') || cls.includes('cascade') ||
                            cls.includes('agent') || cls.includes('panel') ||
                            cls.includes('webview') || cls.includes('message') ||
                            cls.includes('input') || cls.includes('prose') ||
                            cls.includes('markdown') || cls.includes('jetski')
                        )) {
                            classSet.add(cls.substring(0, 120));
                        }
                    });
                    info.interestingClasses = Array.from(classSet).slice(0, 50);
                    
                    // Collect iframe srcs
                    document.querySelectorAll('iframe').forEach(iframe => {
                        info.iframeSrcs.push((iframe.src || iframe.getAttribute('src') || 'no-src').substring(0, 200));
                    });
                    
                    // Collect webview info
                    document.querySelectorAll('webview').forEach(wv => {
                        info.iframeSrcs.push('WEBVIEW: ' + (wv.src || wv.getAttribute('src') || 'no-src').substring(0, 200));
                    });
                    
                    return info;
                })()
            `);

            console.log(JSON.stringify(result, null, 2));

            // If this page has iframes, try to access nested content
            if (result && (result.iframes > 0 || result.webviews > 0 || result.webviewFrames > 0)) {
                console.log(`\n  🔍 Page has ${result.iframes} iframes, ${result.webviews} webviews, ${result.webviewFrames} webviewFrames`);
                console.log(`  Iframe srcs: ${JSON.stringify(result.iframeSrcs)}`);
            }

            if (result && (result.textareas > 0 || result.contentEditables > 0 || result.roleTextbox > 0)) {
                console.log(`\n  🎯 FOUND CHAT ELEMENTS! textarea=${result.textareas} contentEditable=${result.contentEditables} textbox=${result.roleTextbox}`);
            }

            if (result && result.proseElements > 0) {
                console.log(`  🎯 FOUND PROSE! count=${result.proseElements}`);
            }

        } catch (err) {
            console.log(`  Error: ${err.message}`);
        }
    }

    console.log('\n=== Done ===');
}

main().catch(err => console.error('Fatal:', err.message));
