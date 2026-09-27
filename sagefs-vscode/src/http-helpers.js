// HTTP helpers for SageFs VS Code extension
// Extracted from SageFsClient.fs [<Emit>] blocks for debuggability

const http = require('http');

/**
 * @param {string} url
 * @param {number} timeout
 * @returns {Promise<{statusCode: number, body: string}>}
 */
function httpGet(url, timeout) {
  return new Promise((resolve, reject) => {
    const req = http.get(url, { timeout }, (res) => {
      let data = '';
      res.on('data', (chunk) => data += chunk);
      res.on('end', () => resolve({ statusCode: res.statusCode || 0, body: data }));
    });
    req.on('error', reject);
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
  });
}

/**
 * @param {string} url
 * @param {string} body
 * @param {number} timeout
 * @returns {Promise<{statusCode: number, body: string}>}
 */
function httpPost(url, body, timeout) {
  return new Promise((resolve, reject) => {
    const parsed = new URL(url);
    const req = http.request({
      hostname: parsed.hostname,
      port: parsed.port,
      path: parsed.pathname,
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      timeout
    }, (res) => {
      let data = '';
      res.on('data', (chunk) => data += chunk);
      res.on('end', () => resolve({ statusCode: res.statusCode || 0, body: data }));
    });
    req.on('error', reject);
    req.on('timeout', () => { req.destroy(); reject(new Error('timeout')); });
    req.write(body);
    req.end();
  });
}

module.exports = { httpGet, httpPost };
