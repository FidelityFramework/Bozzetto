// Browser regression for the route split, using the built bundle and existing
// mock bridge. Requires Chromium (CHROMIUM overrides its executable), no new
// dependency. Owns only a short-lived preview server and browser, never a daemon.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, writeFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { mkdir } from 'node:fs/promises';
import WebSocket from 'ws';

const cache = join(process.env.XDG_CACHE_HOME ?? join(homedir(), '.cache'), 'bozzetto', 'page-checks');
await mkdir(cache, { recursive: true });
const evidence = await mkdtemp(join(cache, 'split-'));
const children = [];
const logs = [];
let socket;
let sequence = 0;
const pending = new Map();
const browserErrors = [];
const pageRequests = [];
const pageFrames = [];

function launch(command, args) {
  const child = spawn(command, args, { detached: true, stdio: ['ignore', 'pipe', 'pipe'] });
  children.push(child);
  child.stdout.on('data', data => logs.push(data.toString()));
  child.stderr.on('data', data => logs.push(data.toString()));
  return child;
}
function announced(child, pattern) {
  return new Promise((resolve, reject) => {
    let text = '';
    const read = data => {
      text += data.toString();
      const match = text.match(pattern);
      if (match) { clean(); resolve(match[0]); }
    };
    const failed = error => { clean(); reject(error); };
    const exited = code => failed(new Error(`Child exited before ready (${code}): ${text}`));
    const clean = () => {
      child.stdout.off('data', read); child.stderr.off('data', read);
      child.off('error', failed); child.off('exit', exited);
    };
    child.stdout.on('data', read); child.stderr.on('data', read);
    child.once('error', failed); child.once('exit', exited);
  });
}
function call(method, params = {}, sessionId) {
  const id = ++sequence;
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    socket.send(JSON.stringify({ id, method, params, sessionId }));
  });
}
async function evaluate(session, expression) {
  const reply = await call('Runtime.evaluate', { expression, returnByValue: true }, session);
  assert.equal(reply.exceptionDetails, undefined, JSON.stringify(reply.exceptionDetails));
  return reply.result.value;
}
async function waitFor(session, expression) {
  const end = Date.now() + 10000;
  while (Date.now() < end) {
    if (await evaluate(session, expression)) return;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw new Error(`Page did not become ready: ${expression}`);
}
async function tab(origin, route, width) {
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
  await call('Runtime.enable', {}, sessionId);
  await call('Network.enable', {}, sessionId);
  await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false }, sessionId);
  await call('Page.navigate', { url: origin + route }, sessionId);
  const facts = route.startsWith('/dashboard')
    ? "document.querySelector('.daemon-health')?.innerText.includes('Healthy') && document.body.innerText.includes('Mock implementer') && document.body.innerText.includes('Earlier passes (2)')"
    : "document.body.innerText.includes('Composer sessions') || document.body.innerText.includes('COMPOSER SESSIONS')";
  await waitFor(sessionId, `[...document.querySelectorAll('span')].some(s => s.innerText === 'connected') && (${facts})`);
  return sessionId;
}
async function check() {
  const preview = launch(process.execPath, ['node_modules/vite/bin/vite.js', 'preview', '--mode', 'mock', '--host', '127.0.0.1', '--port', '0']);
  const origin = await announced(preview, /http:\/\/127\.0\.0\.1:\d+/);
  const browser = launch(process.env.CHROMIUM ?? 'chromium', [
    '--headless', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    '--remote-debugging-port=0', `--user-data-dir=${join(evidence, 'profile')}`, 'about:blank',
  ]);
  const endpoint = await announced(browser, /ws:\/\/127\.0\.0\.1:\d+\/devtools\/browser\/[\w-]+/);
  socket = new WebSocket(endpoint);
  socket.on('message', data => {
    const reply = JSON.parse(data.toString());
    if (reply.method === 'Runtime.exceptionThrown') browserErrors.push(reply.params.exceptionDetails);
    if (reply.method === 'Network.requestWillBeSent' && ['Fetch', 'XHR'].includes(reply.params.type)) pageRequests.push({session: reply.sessionId, type: reply.params.type});
    if (reply.method === 'Network.webSocketFrameSent') pageFrames.push({session: reply.sessionId});
    const waiter = pending.get(reply.id);
    if (waiter) {
      pending.delete(reply.id);
      if (reply.error) waiter.reject(new Error(JSON.stringify(reply.error)));
      else waiter.resolve(reply.result);
    }
  });
  await new Promise((resolve, reject) => { socket.once('open', resolve); socket.once('error', reject); });
  const dashboard = await tab(origin, '/dashboard', 900);
  const composer = await tab(origin, '/composer', 900);
  const inspect = session => evaluate(session, `({
    title: document.title, text: document.body.innerText,
    links: [...document.querySelectorAll('a')].map(a => ({path: a.getAttribute('href'), active: a.classList.contains('btn-active')})),
    overflow: document.documentElement.scrollWidth > innerWidth
  })`);
  const d = await inspect(dashboard);
  const c = await inspect(composer);
  await writeFile(join(evidence, 'views.json'), JSON.stringify({dashboard: d, composer: c}, null, 2));
  assert.equal(d.title, 'Dashboard · Bozzetto');
  assert.equal(c.title, 'Composer · Bozzetto');
  for (const text of ['Whole-machine resources', 'Work leases', 'Observe for 2 minutes', 'Agent work', 'Healthy', 'memory Normal']) assert.ok(d.text.toLowerCase().includes(text.toLowerCase()), text);
  const agentPanel = "[...document.querySelectorAll('h2')].find(h => h.textContent === 'Agent work').closest('.card-body')";
  const historyDetails = `${agentPanel}.querySelector(':scope > details')`;
  const historyNames = () => evaluate(dashboard, `[...${historyDetails}.querySelectorAll(':scope > details > summary')].map(s => s.textContent.split(' · ')[0])`);
  const currentNames = () => evaluate(dashboard, `[...${agentPanel}.querySelectorAll(':scope > div.border-t > .font-semibold')].map(s => s.textContent.split(' · ')[0])`);
  const trafficBeforeExpansion = {fetches: pageRequests.length, commands: pageFrames.length};
  for (const text of ['Mock implementer', 'Implementer · reported running', 'Mock uncertain', 'Observer · reported unknown', 'Updated ', 'Earlier passes (2)']) assert.ok(d.text.includes(text), `current work: ${text}`);
  for (const text of ['Mock verifier', 'Mock observer', '250 tokens (cumulative)', '$0.125 Pi estimate']) assert.ok(!d.text.includes(text), `details start hidden: ${text}`);
  assert.deepEqual(await currentNames(), ['Mock uncertain', 'Mock implementer'], 'current and uncertain work stay visible, newest first');
  assert.deepEqual(await historyNames(), ['Mock observer', 'Mock verifier'], 'earlier passes are newest first, not input/id order');
  assert.equal(await evaluate(dashboard, `${historyDetails}.open`), false, 'earlier passes start collapsed');
  assert.equal(await evaluate(dashboard, `${agentPanel}.querySelectorAll('details[open]').length`), 0, 'per-pass details start collapsed too');
  assert.equal(await evaluate(dashboard, `(() => { const cards = [...document.querySelectorAll('h2')].map(h => h.textContent); return cards.indexOf('Agent work') < cards.indexOf('Work leases') && cards.indexOf('Work leases') < cards.indexOf('Whole-machine resources'); })()`), true, 'work and leases precede resources');
  assert.equal(await evaluate(dashboard, "[...document.querySelectorAll('h2')].find(h => h.textContent === 'Work leases').getBoundingClientRect().bottom < innerHeight"), true, 'leases are visible without scrolling past history');
  // Native disclosures retain every supplied detail without dumping them into
  // the default view. Opening them must not fetch or issue bridge commands.
  await evaluate(dashboard, `${historyDetails}.querySelector(':scope > summary').click()`);
  let expanded = (await inspect(dashboard)).text;
  assert.ok(expanded.includes('Mock verifier') && expanded.includes('Mock observer'), 'opening earlier passes reveals compact summaries');
  assert.ok(!expanded.includes('0 tokens (cumulative)'), 'per-pass accounting remains collapsed');
  await evaluate(dashboard, `${agentPanel}.querySelectorAll('details').forEach(d => { d.open = true; })`);
  expanded = (await inspect(dashboard)).text;
  for (const text of ['Mock verifier', 'Verifier · reported completed', 'Mock observer', 'Observer · reported disconnected', '250 tokens (cumulative)', '0 tokens (cumulative)', 'usage unavailable', '$0.125 Pi estimate', 'price unavailable']) assert.ok(expanded.includes(text), `retained expanded detail: ${text}`);
  await evaluate(dashboard, `${agentPanel}.querySelectorAll('details').forEach(d => { d.open = false; })`);
  assert.deepEqual({fetches: pageRequests.length, commands: pageFrames.length}, trafficBeforeExpansion, 'accordion expansion requires no frontend fetch or command');
  // The same ordering assertion must fail when rendered pass order is reversed.
  const assertHistoryOrder = () => historyNames().then(names => assert.deepEqual(names, ['Mock observer', 'Mock verifier'], 'earlier passes must be newest first'));
  await evaluate(dashboard, `(() => { const d = ${historyDetails}; d.append(...[...d.querySelectorAll(':scope > details')].reverse()); })()`);
  await assert.rejects(assertHistoryOrder, {code:'ERR_ASSERTION'}, 'latest-first assertion catches reversed history');
  await evaluate(dashboard, `(() => { const d = ${historyDetails}; d.append(...[...d.querySelectorAll(':scope > details')].reverse()); })()`);
  await assertHistoryOrder();
  await writeFile(join(evidence, 'agent-accordion.json'), JSON.stringify({current:await currentNames(), earlier:await historyNames(), initiallyCollapsed:true, fullDetailsRetained:true, noExpansionTraffic:true, reverseOrderMutationCaught:true}, null, 2));
  for (const text of ['Open project', 'Compiler worker', 'Run output', 'Composer sessions']) assert.ok(!d.text.toLowerCase().includes(text.toLowerCase()), `dashboard excludes ${text}`);
  for (const text of ['Composer sessions', 'Compiler worker', 'Run output', 'Open project', 'No open projects.']) assert.ok(c.text.toLowerCase().includes(text.toLowerCase()), text);
  for (const text of ['Whole-machine resources', 'Observe for 2 minutes', 'Work leases', 'Agent work']) assert.ok(!c.text.toLowerCase().includes(text.toLowerCase()), `composer excludes ${text}`);
  for (const [name, page] of [['dashboard', dashboard], ['composer', composer]]) {
    const visible = (await inspect(page)).text;
    for (const editorial of ['Agent-driven view:', 'Live admission, not build/test results.', 'Explicit Linux counter window:', 'No live worker; an agent', 'Runs appear here:', 'CPU is normalized by processor count.']) {
      assert.ok(!visible.includes(editorial), `${name}: no editorial paragraph ${editorial}`);
    }
    assert.equal(await evaluate(page, "[...document.querySelectorAll('dialog')].every(d => !d.open && getComputedStyle(d).display === 'none')"), true, `${name}: closed dialogs are not rendered`);
    const detail = name === 'dashboard' ? 'Process CPU uses 100%' : 'Retirement closes its sessions.';
    assert.ok(!visible.includes(detail), `${name}: details are absent before opening`);
    const ax = await call('Accessibility.getFullAXTree', {}, page);
    assert.equal(ax.nodes.some(n => !n.ignored && n.role?.value === 'button' && n.name?.value === 'Close'), false, `${name}: closed Close buttons are absent from accessibility tree`);
    const label = name === 'dashboard' ? 'Resource readings information' : 'Compiler worker information';
    await evaluate(page, `(() => { const button = document.querySelector('button[aria-label="${label}"]'); button.focus(); button.click(); })()`);
    assert.equal(await evaluate(page, "document.querySelector('dialog[open]') !== null"), true, `${name}: info opens a native modal`);
    assert.ok((await inspect(page)).text.includes(detail), `${name}: details become available on request`);
    await call('Input.dispatchKeyEvent', {type:'keyDown', key:'Escape', code:'Escape', windowsVirtualKeyCode:27}, page);
    await call('Input.dispatchKeyEvent', {type:'keyUp', key:'Escape', code:'Escape', windowsVirtualKeyCode:27}, page);
    assert.equal(await evaluate(page, "document.querySelector('dialog[open]') === null"), true, `${name}: Escape closes info`);
    assert.ok(!(await inspect(page)).text.includes(detail), `${name}: Escape hides details again`);
    assert.equal(await evaluate(page, `document.activeElement?.getAttribute('aria-label') === '${label}'`), true, `${name}: Escape restores focus`);
    await evaluate(page, `document.querySelector('button[aria-label="${label}"]').click(); document.querySelector('dialog[open] button').click()`);
    assert.equal(await evaluate(page, "document.querySelector('dialog[open]') === null"), true, `${name}: Close dismisses info`);
    assert.ok(!(await inspect(page)).text.includes(detail), `${name}: Close hides details again`);
  }
  // A caught stylesheet mutation reproduces the closed-modal regression.
  await evaluate(dashboard, "(() => { const s = document.createElement('style'); s.id = 'modal-regression-control'; s.textContent = 'dialog.modal:not([open]) { display: grid !important; }'; document.head.append(s); })()");
  const mutatedHidden = await evaluate(dashboard, "[...document.querySelectorAll('dialog')].every(d => !d.open && getComputedStyle(d).display === 'none')");
  let mutationCaught = false;
  try { assert.equal(mutatedHidden, true, 'closed dialogs must not be rendered'); } catch (error) { mutationCaught = error.code === 'ERR_ASSERTION'; }
  assert.equal(mutationCaught, true, 'closed-dialog assertion catches the display-grid mutation');
  await evaluate(dashboard, "document.getElementById('modal-regression-control').remove()");
  assert.equal(await evaluate(dashboard, "[...document.querySelectorAll('dialog')].every(d => !d.open && getComputedStyle(d).display === 'none')"), true, 'restored stylesheet hides closed dialogs');
  await writeFile(join(evidence, 'modal-mutation.json'), JSON.stringify({mutation: 'closed dialogs forced to display:grid', caught: mutationCaught, restored: true}, null, 2));
  for (const page of [dashboard, composer]) {
    assert.equal(await evaluate(page, "[...document.querySelectorAll('button')].some(b => b.innerText.includes('Refresh'))"), false, 'no manual refresh chore');
  }
  assert.equal(await evaluate(composer, "document.querySelector('input[placeholder=\"/absolute/path/Project.fidproj\"]').closest('details').open"), false, 'manual opener is not the primary view');
  assert.deepEqual(d.links, [{path: '/dashboard', active: true}, {path: '/composer', active: false}]);
  assert.deepEqual(c.links, [{path: '/dashboard', active: false}, {path: '/composer', active: true}]);
  assert.equal(d.overflow, false, 'dashboard fits a side-by-side viewport');
  assert.equal(c.overflow, false, 'composer fits a side-by-side viewport');
  // Closed presentation must remain bound to pushed state, not a frozen copy.
  const snapshotDetails = "[...document.querySelectorAll('details')].find(d => d.querySelector(':scope > summary')?.innerText === 'Daemon snapshot')";
  const initialCollapsed = await evaluate(dashboard, `(() => { const d = ${snapshotDetails}; return {open:d.open, rows:[...d.querySelectorAll('tbody tr')].map(r => r.firstElementChild.textContent)}; })()`);
  assert.equal(initialCollapsed.open, false, 'daemon details start collapsed');
  assert.deepEqual(initialCollapsed.rows, ['daemon'], 'initial collapsed snapshot has no worker');
  const trafficBeforePush = {fetches: pageRequests.length, commands: pageFrames.length};
  // An independent client opens two projects against the shared mock owner.
  // The human page issues no command: both projects must arrive by push.
  // This is a bridge regression, not a claim of live MCP/compiler execution.
  const agent = new WebSocket(origin.replace(/^http/, 'ws') + '/ui/bridge', {origin});
  try {
    await new Promise((resolve, reject) => { agent.once('open', resolve); agent.once('error', reject); });
    const openProject = (name, correlation) => new Promise((resolve, reject) => {
      const receive = data => {
        const frame = JSON.parse(data);
        if (frame.correlation !== correlation) return;
        if (frame.event === 'accepted' || frame.event === 'refused') {
          agent.off('message', receive);
          frame.event === 'accepted' ? resolve() : reject(new Error(frame.message));
        }
      };
      agent.on('message', receive);
      agent.send(JSON.stringify({correlation, command:'open_project', project:`/mock/${name}.fidproj`}));
    });
    await openProject('SideBySide', 101);
    await waitFor(dashboard, "document.body.innerText.includes('mock read progressed')");
    assert.deepEqual(await currentNames(), ['Mock implementer', 'Mock uncertain'], 'a pushed update moves current work to the top');
    assert.equal(await evaluate(dashboard, `${historyDetails}.open`), false, 'progress does not expand history');
    // Preserve a chosen per-pass expansion even when new completed work enters
    // ahead of it. The outer history disclosure stays closed throughout.
    await evaluate(dashboard, `${historyDetails}.querySelector(':scope > details').open = true`);
    await openProject('SecondAgentProject', 102);
    await waitFor(composer, "document.body.innerText.includes('SideBySide.fidproj') && document.body.innerText.includes('SecondAgentProject.fidproj')");
    await waitFor(dashboard, "document.body.innerText.includes('Earlier passes (3)')");
    assert.deepEqual(await currentNames(), ['Mock uncertain'], 'completion removes the pass from current work');
    assert.deepEqual(await historyNames(), ['Mock implementer', 'Mock observer', 'Mock verifier'], 'completed work enters history newest first');
    assert.equal(await evaluate(dashboard, `${historyDetails}.open`), false, 'completion does not expand history');
    assert.equal(await evaluate(dashboard, `${historyDetails}.querySelectorAll(':scope > details')[1].open`), true, 'existing per-pass disclosure state survives reorder');
    assert.ok(!(await inspect(dashboard)).text.includes('Mock implementer'), 'completed work no longer fills the current view');
    await evaluate(dashboard, `${historyDetails}.querySelector(':scope > summary').click(); ${historyDetails}.querySelector(':scope > details > summary').click()`);
    assert.ok((await inspect(dashboard)).text.includes('mock read completed'), 'expansion immediately exposes the pushed final result');
    await evaluate(dashboard, `${historyDetails}.querySelectorAll('details').forEach(d => { d.open = false; }); ${historyDetails}.open = false`);
    await writeFile(join(evidence, 'agent-push.json'), JSON.stringify({progressReordered:true, completedMovedToHistory:true, historyStayedClosed:true, nestedStateRetained:true, finalDetailAvailable:true}, null, 2));
  } finally { agent.terminate(); }
  assert.equal(await evaluate(composer, "document.querySelector('input[placeholder=\"/absolute/path/Project.fidproj\"]').closest('details').open"), false, 'no manual opening was needed');
  assert.ok(!(await inspect(dashboard)).text.includes('SideBySide.fidproj'));
  await waitFor(dashboard, `(() => { const d = ${snapshotDetails}; return !d.open && [...d.querySelectorAll('tbody tr')].some(r => r.firstElementChild.textContent === 'composer-worker'); })()`);
  const pushedWhileCollapsed = await evaluate(dashboard, `(() => { const d = ${snapshotDetails}; return {open:d.open, rows:[...d.querySelectorAll('tbody tr')].map(r => r.firstElementChild.textContent)}; })()`);
  assert.deepEqual(pushedWhileCollapsed, {open:false, rows:['daemon','composer-worker']}, 'hidden Solid-bound rows update from owner push');
  await evaluate(dashboard, `${snapshotDetails}.querySelector(':scope > summary').click()`);
  assert.equal(await evaluate(dashboard, `(() => { const d = ${snapshotDetails}; return d.open && d.querySelector('table').getClientRects().length > 0 && d.querySelector('table').innerText.includes('composer-worker'); })()`), true, 'opening immediately reveals latest pushed state');
  assert.deepEqual({fetches: pageRequests.length, commands: pageFrames.length}, trafficBeforePush, 'owner push and expanding details require no frontend fetch or command');
  await evaluate(dashboard, `${snapshotDetails}.querySelector(':scope > summary').click()`);
  await writeFile(join(evidence, 'collapsed-store.json'), JSON.stringify({initial:initialCollapsed, pushedWhileCollapsed, visibleOnOpen:true, fetchedOnOpen:false, commandOnOpen:false}, null, 2));
  // Reload and trailing-slash bookmarks select the right view as well.
  await call('Page.navigate', { url: origin + '/composer/' }, composer);
  await waitFor(composer, "document.title === 'Composer · Bozzetto' && document.body.innerText.includes('SideBySide.fidproj')");
  for (const [name, session] of [['dashboard', dashboard], ['composer', composer]]) {
    const { data } = await call('Page.captureScreenshot', { format: 'png' }, session);
    await writeFile(join(evidence, name + '.png'), Buffer.from(data, 'base64'));
    await call('Emulation.setDeviceMetricsOverride', { width: 480, height: 900, deviceScaleFactor: 1, mobile: false }, session);
    assert.equal((await inspect(session)).overflow, false, `${name} fits a narrow viewport`);
  }
  assert.deepEqual(browserErrors, [], 'no browser runtime errors');
  console.log(`Page checks passed: latest-first agent accordions, pushed progress/completion, retained disclosure state, isolated views, live collapsed store, native information controls, navigation, shared-owner command, reload, 900/480 px layouts. Evidence: ${evidence}`);
}
let timeout;
try {
  await Promise.race([check(), new Promise((_, reject) => {
    timeout = setTimeout(() => reject(new Error('Page checks exceeded 45 seconds')), 45000);
  })]);
} finally {
  clearTimeout(timeout);
  socket?.terminate();
  for (const child of children) {
    try { process.kill(-child.pid, 'SIGKILL'); } catch { /* Already exited. */ }
  }
  await writeFile(join(evidence, 'process.log'), logs.join(''));
}
