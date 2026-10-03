import { defineConfig } from 'vite';
import solidPlugin from 'vite-plugin-solid';
import { viteSingleFile } from 'vite-plugin-singlefile';
import { mockBridge } from './scripts/mock-bridge.js';

// The dev daemon whose /ui/bridge `npm run dev` proxies to. A dedicated dev
// daemon, never the installed one on 47749/47750.
const daemon = process.env.BOZZETTO_DAEMON ?? 'http://127.0.0.1:47759';

// Only the Vite page itself may borrow the daemon's origin. WebSockets are not
// subject to CORS, so a blanket rewrite would let any website the developer
// visits reach the daemon through this proxy. Require a loopback Host (no DNS
// rebinding) and an Origin equal to that Host; anything else is forwarded
// untouched and the daemon's origin guard refuses it.
const loopback = /^(localhost|127\.0\.0\.1|\[::1\])(:\d+)?$/i;
function ownPageOrigin(request) {
  const host = request.headers.host ?? '';
  const origin = request.headers.origin ?? '';
  return loopback.test(host) && (origin === `http://${host}` || origin === `https://${host}`);
}

const bridgeProxy = {
  // The tab icons, served by the daemon beside the page.
  '/favicon.svg': { target: daemon, changeOrigin: true },
  '/favicon-dark.svg': { target: daemon, changeOrigin: true },
  '/favicon.ico': { target: daemon, changeOrigin: true },
  '/ui/bridge': {
    target: daemon,
    ws: true,
    // Host becomes the daemon's own (loopback) authority...
    changeOrigin: true,
    // ...and so must Origin: the daemon's origin guard refuses any page
    // origin that is not its own, which the Vite page's origin is not.
    configure: (proxy) => {
      proxy.on('proxyReqWs', (proxyReq, request) => {
        if (ownPageOrigin(request)) proxyReq.setHeader('origin', new URL(daemon).origin);
      });
    },
  },
};

export default defineConfig(({ mode }) => ({
  // `vite build` must produce ONE self-contained index.html: scripts, styles
  // and assets all inlined, so scripts/weld.js can embed it in the daemon.
  plugins: [solidPlugin(), viteSingleFile(), ...(mode === 'mock' ? [mockBridge()] : [])],
  // `--mode mock` answers /ui/bridge from the in-process mock instead.
  // `vite` serves the source page; `vite preview` serves the built dist/.
  server: { proxy: mode === 'mock' ? undefined : bridgeProxy },
  preview: { proxy: mode === 'mock' ? undefined : bridgeProxy },
  build: {
    target: 'esnext',
    assetsInlineLimit: 100000000,
    chunkSizeWarningLimit: 100000000,
    cssCodeSplit: false,
    modulePreload: { polyfill: false },
    rollupOptions: {
      output: {
        inlineDynamicImports: true,
      },
    },
  },
}));
