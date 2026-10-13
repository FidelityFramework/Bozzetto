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
import { encodeCommand, decodeCommand, decodeEvent } from '../output-test/src/Shared/BinaryCodec.js';
import { Command, CommandFrame } from '../output-test/src/Shared/Protocol.js';

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
const pageReplies = [];

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
  await writeFile(join(evidence, 'wait-timeout.json'), JSON.stringify({expression, text:await evaluate(session, 'document.body.innerText'), buttons:await evaluate(session, '[...document.querySelectorAll("h3")].filter(x=>["one","two","three"].includes(x.textContent)).map(x=>({name:x.textContent,button:x.closest(".card").querySelector("button")?.outerHTML}))'), sent:pageFrames, errors:browserErrors},null,2));
  throw new Error(`Page did not become ready: ${expression}`);
}
async function tab(origin, route, width) {
  const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
  await call('Runtime.enable', {}, sessionId);
  await call('Network.enable', {}, sessionId);
  await call('Page.enable', {}, sessionId);
  await call('Page.addScriptToEvaluateOnNewDocument', {source: `
    window.__firstWorkAges = {};
    new MutationObserver(() => {
      for (const sample of document.querySelectorAll('.work-sample-age')) {
        const node = sample.closest('.card')?.querySelector('h3')?.textContent;
        if (node && sample.textContent.includes('Work sample ') && !(node in window.__firstWorkAges))
          window.__firstWorkAges[node] = sample.textContent;
      }
    }).observe(document, {childList:true, subtree:true, characterData:true});
  `}, sessionId);
  await call('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false }, sessionId);
  await call('Page.navigate', { url: origin + route }, sessionId);
  const facts = route.startsWith('/dashboard')
    ? "document.querySelector('.daemon-health')?.innerText.includes('Healthy') && document.body.innerText.includes('Mock implementer') && document.body.innerText.includes('Earlier passes (2)')"
    : "document.body.innerText.includes('Work across nodes')";
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
    if (reply.method === 'Network.webSocketFrameSent') {
      const wire = reply.params.response;
      const decoded = wire.opcode === 2 ? decodeCommand(new Uint8Array(Buffer.from(wire.payloadData, 'base64'))) : undefined;
      const command = decoded?.tag === 0 ? decoded.fields[0].Command : undefined;
      const resource = command?.tag === 2 ? command.fields[0] : undefined;
      pageFrames.push({session: reply.sessionId, opcode:wire.opcode, correlation:decoded?.tag === 0 ? decoded.fields[0].Correlation : null, command:command?.tag ?? null, node:resource?.Node, seconds:resource?.Seconds});
    }
    if (reply.method === 'Network.webSocketFrameReceived' && reply.params.response.opcode === 2) {
      const decoded = decodeEvent(new Uint8Array(Buffer.from(reply.params.response.payloadData, 'base64')));
      if (decoded.tag === 0 && (decoded.fields[0].tag === 2 || decoded.fields[0].tag === 3))
        pageReplies.push({session:reply.sessionId, correlation:decoded.fields[0].fields[0].Correlation});
    }
    const waiter = pending.get(reply.id);
    if (waiter) {
      pending.delete(reply.id);
      if (reply.error) waiter.reject(new Error(JSON.stringify(reply.error)));
      else waiter.resolve(reply.result);
    }
  });
  await new Promise((resolve, reject) => { socket.once('open', resolve); socket.once('error', reject); });
  const dashboard = await tab(origin, '/dashboard', 900);
  const work = await tab(origin, '/work', 900);
  const inspect = session => evaluate(session, `({
    title: document.title, text: document.body.innerText,
    headings: [...document.querySelectorAll('h2')].map(h => h.textContent),
    links: [...document.querySelectorAll('a')].map(a => ({path: a.getAttribute('href'), title: a.getAttribute('title'), active: a.classList.contains('btn-active')})),
    overflow: document.documentElement.scrollWidth > innerWidth
  })`);
  const d = await inspect(dashboard);
  const c = await inspect(work);
  await writeFile(join(evidence, 'views.json'), JSON.stringify({dashboard: d, work: c}, null, 2));
  assert.equal(d.title, 'Dashboard · Bozzetto');
  assert.equal(c.title, 'Work · Bozzetto');
  const initialNode = name => `[...document.querySelectorAll('h3')].find(h => h.textContent === '${name}').closest('.card')`;
  const initialOne = await evaluate(work, `${initialNode('one')}.innerText`);
  for (const text of ['/mock/clef', 'test_suite_run · running', 'PID 6543', '/mock/dotnet', 'fixture-lease', 'fixture-test-run', 'fixture queued build · /mock/Farscape', 'Compiler worker · running · PID 7654']) assert.ok(initialOne.includes(text), `cross-node running work: ${text}`);
  assert.ok(initialOne.includes('0 compiler sessions · 1 active leases · 1 queued'), 'leased command does not manufacture a compiler session');
  assert.ok(!initialOne.includes('Exit 0'), 'running command does not invent a successful result');
  const firstWorkAges = await evaluate(work, 'window.__firstWorkAges');
  assert.ok(firstWorkAges.one.includes('age unavailable (awaiting clock)'), 'unknown initial clock must not manufacture a zero sample age');
  const oneAge = "[...document.querySelectorAll('h3')].find(h=>h.textContent === 'one').closest('.card').querySelector('.work-sample-age').textContent";
  await waitFor(work, `!(${oneAge}).includes('age unavailable')`);
  const knownAge = await evaluate(work, oneAge);
  assert.equal(knownAge.split(' · ')[0], firstWorkAges.one.split(' · ')[0], 'clock push changes age without changing the retained sample timestamp');
  assert.match(knownAge, / · .*\d+s ago$/, 'known pushed clock supplies the relative age');
  assert.equal(pageFrames.filter(f=>f.command === 2).length, 0, 'initial node selection does not silently start observation');
  const resourcePicker = "document.querySelector('select[aria-label=\"Resource node\"]')";
  assert.equal(await evaluate(work, `${resourcePicker} === null`), true, 'Work has no resource selector');
  for (const page of [dashboard]) {
    assert.deepEqual((await evaluate(page, `[...${resourcePicker}.options].map(o=>o.textContent)`)).sort(), ['Workstation','ONE','TWO','THREE'].sort(), 'resource panel exposes every admitted node');
    assert.equal(await evaluate(page, `${resourcePicker}.value`), 'controller', 'resource selection defaults to the observed controller identity');
  }
  for (const text of ['Whole-machine resources', 'Observe for 2 minutes', 'Agent work', 'Healthy', 'memory Normal']) assert.ok(d.text.toLowerCase().includes(text.toLowerCase()), text);
  const agentPanel = "[...document.querySelectorAll('h2')].find(h => h.textContent === 'Agent work').closest('.card-body')";
  const historyDetails = `${agentPanel}.querySelector(':scope > details')`;
  const historyNames = () => evaluate(dashboard, `[...${historyDetails}.querySelectorAll(':scope > details > summary')].map(s => s.textContent.split(' · ')[0])`);
  const currentNames = () => evaluate(dashboard, `[...${agentPanel}.querySelectorAll(':scope > div.border-t:not(.presence) > .font-semibold')].map(s => s.textContent.split(' · ')[0])`);
  const presenceRows = () => evaluate(dashboard, `[...${agentPanel}.querySelectorAll(':scope > div.presence > .font-semibold')].map(s => s.textContent)`);
  const trafficBeforeExpansion = {fetches: pageRequests.length, commands: pageFrames.length};
  for (const text of ['Mock implementer', 'Implementer · reported running', 'Mock uncertain', 'Observer · reported unknown', 'Updated ', 'Earlier passes (2)']) assert.ok(d.text.includes(text), `current work: ${text}`);
  for (const text of ['Mock verifier', 'Mock observer', '250 tokens (cumulative)', '$0.125 Pi estimate']) assert.ok(!d.text.includes(text), `details start hidden: ${text}`);
  assert.deepEqual(await currentNames(), ['Mock uncertain', 'Mock implementer'], 'current and uncertain work stay visible, newest first');
  // Presence is derived from leases and membership the daemon owns: shown without a report, newest first.
  assert.deepEqual(await presenceRows(), ['Mock builder · holds full_build', 'mcp:quiet · Observer member · no report yet'], 'derived presence rows follow the reported work, newest first');
  for (const text of ['lease since', 'member renewed']) assert.ok(d.text.includes(text), `presence source: ${text}`);
  assert.deepEqual(await historyNames(), ['Mock observer', 'Mock verifier'], 'earlier passes are newest first, not input/id order');
  assert.equal(await evaluate(dashboard, `${historyDetails}.open`), false, 'earlier passes start collapsed');
  assert.equal(await evaluate(dashboard, `${agentPanel}.querySelectorAll('details[open]').length`), 0, 'per-pass details start collapsed too');
  assert.equal(await evaluate(dashboard, `(() => { const cards = [...document.querySelectorAll('h2')].map(h => h.textContent); return cards.indexOf('Agent work') < cards.indexOf('Whole-machine resources'); })()`), true, 'agent work precedes resources');
  // Each node groups its actual worker and leases ahead of collapsed history.
  assert.equal(await evaluate(work, `(() => { const card = ${initialNode('one')}; return !!(card.querySelector('.node-worker').compareDocumentPosition(card.querySelector('.node-lease')) & Node.DOCUMENT_POSITION_FOLLOWING); })()`), true, 'remote leases follow their own node worker');
  assert.equal(await evaluate(work, `(() => { const card = ${initialNode('one')}; return !!(card.querySelector('.node-lease').compareDocumentPosition(card.querySelector('.node-operations')) & Node.DOCUMENT_POSITION_FOLLOWING) && !card.querySelector('.node-operations').open; })()`), true, 'remote active leases precede folded history');
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
  const assertDashboardHeadings = page => {
    for (const title of ['Open project', 'Compiler worker', 'Run output', 'Composer sessions', 'Work leases']) {
      assert.ok(!page.headings.includes(title), `dashboard excludes the ${title} panel`);
    }
  };
  assertDashboardHeadings(d);
  for (const text of ['Work across nodes', 'Controller compiler controls']) assert.ok(c.text.includes(text), text);
  assert.ok(c.text.includes('No open projects.'), 'visible controller sessions report their actual empty state');
  for (const text of ['Controller projects', 'No runs yet:']) assert.ok(!c.text.includes(text), `empty controller state does not replace cross-node work: ${text}`);
  assert.equal(await evaluate(work, "document.querySelector('.controller-compiler').open"), true, 'controller sessions and worker start visible');
  assert.equal(await evaluate(work, "!!(document.querySelector('.controller-compiler').compareDocumentPosition(document.querySelector('.work-nodes')) & Node.DOCUMENT_POSITION_FOLLOWING)"), true, 'compiler controls precede completed history');
  await evaluate(work, "document.querySelector('.controller-compiler > summary').click()");
  assert.equal(await evaluate(work, "document.querySelector('.controller-compiler').open"), false, 'compiler controls can be folded');
  await evaluate(work, "document.querySelector('.controller-compiler > summary').click()");
  const compilerControls = await inspect(work);
  for (const text of ['Composer sessions', 'Compiler worker', 'Open project', 'No open projects.']) assert.ok(compilerControls.text.toLowerCase().includes(text.toLowerCase()), text);
  for (const text of ['Whole-machine resources', 'Observe for 2 minutes', 'Observe resources', 'Restart resource observation', 'Resources ·']) assert.ok(!c.text.includes(text), `Work excludes ${text}`);
  assert.ok(!c.text.includes('Agent work'), 'Work keeps the separate Agent work panel on Dashboard');
  for (const [name, page] of [['dashboard', dashboard], ['work', work]]) {
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
  for (const page of [dashboard, work]) {
    assert.equal(await evaluate(page, "[...document.querySelectorAll('button')].some(b => b.innerText.includes('Refresh'))"), false, 'no manual refresh chore');
  }
  assert.equal(await evaluate(work, "document.querySelector('input[placeholder=\"/absolute/path/Project.fidproj\"]').closest('details').open"), false, 'manual opener is not the primary view');
  assert.deepEqual(d.links, [
    {path: '/dashboard', title: 'Daemon resources and health', active: true},
    {path: '/work', title: 'Compiler worker, project sessions, build/test leases and run output', active: false},
  ]);
  assert.deepEqual(c.links, [
    {path: '/dashboard', title: 'Daemon resources and health', active: false},
    {path: '/work', title: 'Compiler worker, project sessions, build/test leases and run output', active: true},
  ]);
  assert.equal(d.overflow, false, 'dashboard fits a side-by-side viewport');
  assert.equal(c.overflow, false, 'work fits a side-by-side viewport');
  // Retirement is bounded, browser-local and never removes active work.
  const history = `${initialNode('one')}.querySelector('.node-operations')`;
  assert.equal(await evaluate(work, `${history}.open`), false, 'completed node history starts folded');
  assert.equal(await evaluate(work, `${history}.querySelectorAll('details.node-operation').length`), 20, 'completed rows are capped at twenty');
  assert.equal(await evaluate(work, `${history}.textContent.includes('/mock/retired-by-age')`), false, 'entries older than a day retire from this view');
  assert.equal(await evaluate(work, `${initialNode('one')}.textContent.includes('/mock/expired-lease')`), false, 'released leases with no run do not pin aged history as active');
  assert.equal(await evaluate(work, `${initialNode('one')}.querySelector('.node-active pre').textContent.includes('/mock/clef/tests.dll')`), true, 'running command arguments remain available inside their disclosure');
  assert.equal(await evaluate(work, `${initialNode('one')}.querySelectorAll('.node-active').length`), 1, 'active command stays visible outside retired history');
  const retirementTraffic = {fetches: pageRequests.length, commands: pageFrames.length};
  await evaluate(work, `${history}.querySelector(':scope > summary').click()`);
  await evaluate(work, `${history}.querySelector('details.node-operation > summary').click()`);
  await evaluate(work, `[...${history}.querySelectorAll('button')].find(b=>b.textContent === 'Retire entry').click()`);
  assert.equal(await evaluate(work, `${history}.querySelectorAll('details.node-operation').length`), 20, 'retiring one row reveals the next retained recent row');
  assert.equal(await evaluate(work, "JSON.parse(localStorage.getItem('bozzetto-work-retired')).length"), 1, 'manual retirement persists across a page reload');
  await evaluate(work, `[...document.querySelectorAll('button')].find(b=>b.textContent === 'Restore retired entries').click()`);
  assert.equal(await evaluate(work, "JSON.parse(localStorage.getItem('bozzetto-work-retired')).length"), 0, 'retirement can be undone');
  assert.deepEqual({fetches: pageRequests.length, commands: pageFrames.length}, retirementTraffic, 'retiring a row does not alter the remote journal or issue commands');
  await writeFile(join(evidence, 'work-retirement.json'), JSON.stringify({cap:20, ageHours:24, collapsed:true, activePreserved:true, browserOnly:true, reversible:true}, null, 2));
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
        const decoded = decodeEvent(new Uint8Array(data));
        if (decoded.tag !== 0) return reject(new Error(decoded.fields[0]));
        const event = decoded.fields[0];
        if (event.tag !== 2 && event.tag !== 3) return;
        const frame = event.fields[0];
        if (frame.Correlation !== correlation) return;
        agent.off('message', receive);
        event.tag === 2 ? resolve() : reject(new Error(frame.Message));
      };
      agent.on('message', receive);
      agent.send(encodeCommand(new CommandFrame(correlation, new Command(3, [`/mock/${name}.fidproj`]))));
    });
    await openProject('SideBySide', 101);
    await waitFor(dashboard, "document.body.innerText.includes('mock read progressed')");
    assert.deepEqual(await currentNames(), ['Mock implementer', 'Mock uncertain'], 'a pushed update moves current work to the top');
    assert.equal(await evaluate(dashboard, `${historyDetails}.open`), false, 'progress does not expand history');
    // Preserve a chosen per-pass expansion even when new completed work enters
    // ahead of it. The outer history disclosure stays closed throughout.
    await evaluate(dashboard, `${historyDetails}.querySelector(':scope > details').open = true`);
    await openProject('SecondAgentProject', 102);
    await waitFor(work, "document.body.innerText.includes('SideBySide.fidproj') && document.body.innerText.includes('SecondAgentProject.fidproj')");
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
  assert.equal(await evaluate(work, "document.querySelector('input[placeholder=\"/absolute/path/Project.fidproj\"]').closest('details').open"), false, 'no manual opening was needed');
  assert.ok(!(await inspect(dashboard)).text.includes('SideBySide.fidproj'));
  await waitFor(dashboard, `(() => { const d = ${snapshotDetails}; return !d.open && [...d.querySelectorAll('tbody tr')].some(r => r.firstElementChild.textContent === 'Compiler worker'); })()`);
  const pushedWhileCollapsed = await evaluate(dashboard, `(() => { const d = ${snapshotDetails}; return {open:d.open, rows:[...d.querySelectorAll('tbody tr')].map(r => r.firstElementChild.textContent)}; })()`);
  assert.deepEqual(pushedWhileCollapsed, {open:false, rows:['daemon','Compiler worker']}, 'hidden Solid-bound rows update from owner push');
  await evaluate(dashboard, `${snapshotDetails}.querySelector(':scope > summary').click()`);
  assert.equal(await evaluate(dashboard, `(() => { const d = ${snapshotDetails}; return d.open && d.querySelector('table').getClientRects().length > 0 && d.querySelector('table').innerText.includes('Compiler worker'); })()`), true, 'opening immediately reveals latest pushed state');
  const dashboardWithWorker = await inspect(dashboard);
  assertDashboardHeadings(dashboardWithWorker);
  assert.deepEqual({fetches: pageRequests.length, commands: pageFrames.length}, trafficBeforePush, 'owner push and expanding details require no frontend fetch or command');
  await evaluate(dashboard, `${snapshotDetails}.querySelector(':scope > summary').click()`);
  await writeFile(join(evidence, 'collapsed-store.json'), JSON.stringify({initial:initialCollapsed, pushedWhileCollapsed, headingsWithWorker:dashboardWithWorker.headings, visibleOnOpen:true, fetchedOnOpen:false, commandOnOpen:false}, null, 2));
  // Controller Work renders authoritative node-scoped observations; agent labels
  // never manufacture leases or make unavailable observations look idle.
  const nodeCard = name => `[...document.querySelectorAll('h3')].find(h => h.textContent === '${name}').closest('.card')`;
  for (const name of ['one', 'two', 'three']) {
    assert.ok(await evaluate(work, `${nodeCard(name)}.innerText.includes('remote · read-only')`), name + ': remote scope is explicit');
    assert.equal(await evaluate(work, `[...${nodeCard(name)}.querySelectorAll('button')].every(b=>['Load stdout','Load stderr','Retire entry','Retire shown entries'].includes(b.textContent))`), true, name + ': only remote readback and browser-local retirement actions');
  }
  const settledRow = `[...${nodeCard('one')}.querySelectorAll('details.node-operation')].find(row=>row.textContent.includes('fixture-test-run'))`;
  await evaluate(work, `${nodeCard('one')}.querySelector('.node-operations').open = true; ${settledRow}.open = true`);
  const settledOne = await evaluate(work, `${nodeCard('one')}.innerText`);
  for (const text of ['/mock/clef', 'command_failed: dotnet exited 1', 'PID 6543', 'fixture-lease', 'fixture-test-run', 'exit 1']) assert.ok(settledOne.includes(text), `settled command remains after lease release: ${text}`);
  assert.ok(settledOne.includes('0 active leases · 0 queued'), 'released admission is distinct from retained history');
  assert.equal(await evaluate(work, `[...${nodeCard('one')}.querySelectorAll('.node-operation')].filter(row=>row.textContent.includes('fixture-test-run')).length`), 1, 'one run keeps one row through settlement among bounded history');
  assert.ok(await evaluate(work, `${nodeCard('two')}.innerText.includes('0 active leases')`), 'observed zero is shown');
  assert.ok(await evaluate(work, `${nodeCard('three')}.innerText.includes('Work state unavailable; no idle state inferred.')`), 'unobserved state is not zero');
  const resourceMetric = "[...document.querySelectorAll('div')].find(e=>e.className === 'text-xs opacity-70' && e.textContent === 'Host CPU busy')?.nextElementSibling?.textContent";
  const resourceStatus = "[...document.querySelectorAll('h2')].find(e=>e.textContent === 'Whole-machine resources').parentElement.querySelector('.badge').textContent";
  async function resourceAction(label, expression) {
    const before = pageFrames.length;
    await call('Page.bringToFront', {}, dashboard);
    await evaluate(dashboard, expression);
    const end = Date.now() + 3000;
    while (Date.now() < end) {
      const frame = pageFrames.slice(before).filter(f=>f.session === dashboard).at(-1);
      if (frame && pageReplies.some(r=>r.session === dashboard && r.correlation === frame.correlation)) {
        assert.equal(frame.opcode, 2, 'browser commands must be binary WebSocket frames');
        // The mock acknowledgement follows every adversarial frame. Let the
        // browser finish that event batch before examining the rendered fact.
        await call('Runtime.evaluate', {expression:'new Promise(requestAnimationFrame)', awaitPromise:true}, dashboard);
        return;
      }
      await new Promise(resolve=>setTimeout(resolve, 20));
    }
    throw new Error('Binary resource observation acknowledgement missing: ' + label);
  }
  const observe = name => resourceAction(name, `${resourcePicker}.value = '${name}'; ${resourcePicker}.dispatchEvent(new Event('change', {bubbles:true}))`);
  await observe('one');
  assert.equal(await evaluate(dashboard, `${resourcePicker}.value`), 'one', 'native node picker displays its selected source');
  assert.equal(await evaluate(dashboard, resourceMetric), '17.0%', 'foreign incarnation/node and older sequence cannot overwrite selected node');
  const switchStart = pageFrames.length;
  await observe('two');
  assert.deepEqual(pageFrames.slice(switchStart).filter(f=>f.session === dashboard && f.command === 2).map(f=>[f.node,f.seconds]), [['one',0],['two',120]], 'node picker stops the previous scope before starting the selected scope');
  assert.equal(await evaluate(dashboard, resourceMetric), '23.0%', 'second node has its own resource fact');
  assert.equal(await evaluate(dashboard, `${resourcePicker}.value`), 'two', 'focus switches to the requested node');
  await observe('one');
  assert.equal(await evaluate(dashboard, resourceMetric), '17.0%', 'A→B→A rejects the old A observation correlation with the same source and newer sequence');
  assert.equal(await evaluate(dashboard, resourceStatus), 'observing', 'an accepted observation start is not a stop acknowledgement');
  await resourceAction('stop', "[...document.querySelectorAll('button')].find(b=>b.textContent === 'Stop').click()");
  assert.equal(await evaluate(dashboard, resourceStatus), 'stopped', 'only the exact typed stop acknowledgement changes view status');
  assert.equal(await evaluate(dashboard, resourceMetric), '17.0%', 'stop acknowledgement preserves the last resource values without requiring a new sample');
  const readback = "document.querySelector('.transcript-readback')";
  const readsBefore = pageFrames.filter(f=>f.session === work && f.command === 10).length;
  await evaluate(work, `[...${settledRow}.querySelectorAll('button')].find(b=>b.textContent === 'Load stdout').click()`);
  await waitFor(work, `${readback}?.innerText.includes('more available')`);
  assert.ok((await evaluate(work, `${readback}.innerText`)).includes('32768 stored bytes loaded'), 'first page byte cursor is exact');
  assert.equal(await evaluate(work, `${readback}.querySelector('pre') === null`), true, 'incomplete UTF8 is not independently decoded or displayed');
  assert.equal(pageFrames.filter(f=>f.session === work && f.command === 10).length, readsBefore + 1, 'next page is not fetched without selection');
  await evaluate(work, `${readback}.querySelector('button').click()`);
  await waitFor(work, `${readback}.querySelector('pre')?.textContent.endsWith('🎼 stored tail')`);
  const stored = await evaluate(work, `${readback}.querySelector('pre').textContent`);
  assert.equal(stored, 'x'.repeat(32767) + '🎼 stored tail', 'split UTF8 pages reconstruct the exact stored stream');
  assert.ok((await evaluate(work, `${readback}.innerText`)).includes('Truncated at the source'), 'stored source truncation is distinct from live summary omission');
  assert.ok(!(await evaluate(work, `${readback}.innerText`)).includes('must be ignored'), 'foreign daemon incarnation cannot replace selected readback');
  await evaluate(work, `[...${settledRow}.querySelectorAll('button')].find(b=>b.textContent === 'Load stderr').click()`);
  await waitFor(work, `${readback}.innerText.includes('storage_failure: Synthetic transcript store refusal.')`);
  assert.equal(await evaluate(work, `${readback}.querySelector('pre') === null`), true, 'failed new selection never shows the previous stream as its transcript');
  await observe('three');
  assert.equal(await evaluate(dashboard, `${resourcePicker}.value`), 'three', 'unavailable node remains an explicit selection');
  assert.ok((await evaluate(dashboard, "document.querySelector('.resource-node-availability').textContent")).includes('THREE work observation: unavailable'), 'unavailable source is not reported as idle');
  assert.equal(await evaluate(dashboard, resourceMetric), undefined, 'switching to an unavailable node never relabels another node’s measurements');
  const { data: resourceImage } = await call('Page.captureScreenshot', { format: 'png' }, dashboard);
  await writeFile(join(evidence, 'dashboard-resources.png'), Buffer.from(resourceImage, 'base64'));
  // Native page navigation leaves no resource UI or new observation on Work.
  // Server disconnect cleanup remains source/accepted-gate-backed; CDP does
  // not reliably report the retired socket across document replacement.
  await observe('one');
  const navigationStart = pageFrames.length;
  await evaluate(dashboard, "document.querySelector('a[href=\"/work\"]').click()");
  await waitFor(dashboard, "document.title === 'Work · Bozzetto' && document.body.innerText.includes('Work across nodes')");
  assert.equal(await evaluate(dashboard, `${resourcePicker} === null`), true, 'navigation leaves no hidden resource selector on Work');
  assert.equal(pageFrames.slice(navigationStart).some(f=>f.session === dashboard && f.command === 2), false, 'new Work page starts no resource observation');
  await evaluate(dashboard, "document.querySelector('a[href=\"/dashboard\"]').click()");
  await waitFor(dashboard, `document.title === 'Dashboard · Bozzetto' && ${resourcePicker}?.options.length === 4`);
  const { data: nodeImage } = await call('Page.captureScreenshot', { format: 'png' }, work);
  await writeFile(join(evidence, 'work-nodes.png'), Buffer.from(nodeImage, 'base64'));
  await writeFile(join(evidence, 'node-controls.json'), JSON.stringify({remoteReadOnly:true, knownZeroSeparateFromUnavailable:true, activeLeaseShown:true, focusSwitched:true, resourceNodePicker:true, resourcesDashboardOnly:true, navigationStartsNoResources:true, stopsPreviousSelection:true, unavailableSelectionExplicit:true, bootAgeUnknown:true, pushedClockUpdatesAge:true, foreignNodeIncarnationAndOlderSequenceRejected:true, previousSameSourceCorrelationRejected:true, stoppedObservationAccepted:true, browserCommandsBinary:true, selectedTranscriptReadback:true, exactSplitUtf8Bytes:true, explicitSourceTruncation:true, foreignTranscriptIncarnationRejected:true, storageRefusalVisible:true},null,2));
  // Reload and trailing-slash bookmarks select the right view as well.
  await call('Page.navigate', { url: origin + '/work/' }, work);
  await waitFor(work, "document.title === 'Work · Bozzetto' && document.body.innerText.includes('SideBySide.fidproj')");
  for (const [name, session] of [['dashboard', dashboard], ['work', work]]) {
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
