// Vite plugin for `npm run dev:mock` and `npm run preview:mock`: answers
// WebSocket upgrades on /ui/bridge with the canned mock bridge (src/Mock,
// compiled by Fable to output-mock/ by `npm run fable:mock`). In preview it
// serves the built single-file page, the same bytes the weld embeds. Vite's
// own HMR socket is untouched. It also serves the tab icons from icons/, as
// the daemon serves them beside the page.
import { readFileSync } from 'node:fs';
import { WebSocketServer } from 'ws';

const icons = {
  '/favicon.svg': ['image/svg+xml', 'favicon.svg'],
  '/favicon-dark.svg': ['image/svg+xml', 'favicon-dark.svg'],
  '/favicon.ico': ['image/x-icon', 'favicon.ico'],
};

async function attach(server) {
  const { connect } = await import('../output-mock/Bridge.js');
  server.middlewares.use((request, response, next) => {
    const icon = icons[(request.url ?? '').split('?')[0]];
    if (!icon) return next();
    response.setHeader('Content-Type', icon[0]);
    response.end(readFileSync(new URL(`../icons/${icon[1]}`, import.meta.url)));
  });
  const sockets = new WebSocketServer({ noServer: true });
  server.httpServer?.on('upgrade', (request, socket, head) => {
    if ((request.url ?? '').split('?')[0] !== '/ui/bridge') return;
    sockets.handleUpgrade(request, socket, head, (ws) => {
      const connection = connect((frame) => {
        if (ws.readyState === ws.OPEN) ws.send(frame);
      });
      ws.on('message', (data, isBinary) => {
        if (!isBinary) connection.receive(data.toString('utf8'));
      });
      ws.on('close', () => connection.close());
    });
  });
  server.config.logger.info('  bozzetto: mock bridge on /ui/bridge');
}

export function mockBridge() {
  return {
    name: 'bozzetto-mock-bridge',
    configureServer: attach,
    configurePreviewServer: attach,
  };
}
