'use strict';

/* ---------------------------------------------------------------------------
 * CNC Maestro Remote - local-network PWA client.
 * Talks to one or more shop PCs (each running the Maestro plugin server).
 * The machine list + tokens live on this device; one machine is active at a time.
 * ------------------------------------------------------------------------- */

const LS = {
  machines: 'maestro.machines',
  active: 'maestro.activeMachine',
  prefs: 'maestro.prefs',
};

const state = {
  machines: [],
  activeId: null,
  prefs: { jogMode: 'cont', step: 1, stepIn: 0.1, feed: 1500, feedIn: 60, spindle: 12000, deviceName: '' },
  snapshot: null,
  conn: 'bad',        // ok | connecting | bad
  es: null,           // EventSource
  screen: 'jog',
};

/* ---------- storage ---------- */
function loadAll() {
  try { state.machines = JSON.parse(localStorage.getItem(LS.machines)) || []; } catch { state.machines = []; }
  state.activeId = localStorage.getItem(LS.active);
  try { Object.assign(state.prefs, JSON.parse(localStorage.getItem(LS.prefs)) || {}); } catch {}
}
function saveMachines() { localStorage.setItem(LS.machines, JSON.stringify(state.machines)); }
function savePrefs() { localStorage.setItem(LS.prefs, JSON.stringify(state.prefs)); }
function setActive(id) { state.activeId = id; localStorage.setItem(LS.active, id || ''); }
function activeMachine() { return state.machines.find(m => m.id === state.activeId) || null; }

/* This device's name (shown to other clients as the control holder). */
function guessDeviceName() {
  const ua = navigator.userAgent || '';
  if (/iPad/.test(ua)) return 'iPad';
  if (/iPhone/.test(ua)) return 'iPhone';
  if (/Android/.test(ua)) return 'Android phone';
  return 'My device';
}
function deviceName() { return (state.prefs.deviceName || '').trim() || guessDeviceName(); }

/* ---------- units (per machine) ---------- */
function machineUnits() { const m = activeMachine(); return (m && m.units === 'in') ? 'in' : 'mm'; }
function isInch() { return machineUnits() === 'in'; }
function feedCfg() {
  return isInch()
    ? { min: 5, max: 1000, step: 5, unit: 'in/min', key: 'feedIn' }
    : { min: 100, max: 25000, step: 100, unit: 'mm/min', key: 'feed' };
}
function currentFeed() { return state.prefs[feedCfg().key]; }
function currentStep() { return isInch() ? state.prefs.stepIn : state.prefs.step; }

/* ---------- api ---------- */
function apiBase() { const m = activeMachine(); return m ? m.host.replace(/\/$/, '') : ''; }

async function api(path, method = 'GET', body = null) {
  const m = activeMachine();
  if (!m) throw new Error('no machine');
  const opt = { method, headers: { 'Authorization': 'Bearer ' + m.token } };
  if (body) { opt.headers['Content-Type'] = 'application/json'; opt.body = JSON.stringify(body); }
  const res = await fetch(apiBase() + path, opt);
  if (!res.ok) {
    let msg = res.statusText;
    try { const j = await res.json(); msg = j.message || j.error || msg; } catch {}
    const e = new Error(msg); e.status = res.status; throw e;
  }
  const ct = res.headers.get('content-type') || '';
  return ct.includes('json') ? res.json() : res.text();
}

function cmd(path, body) {
  return api(path, 'POST', body || {}).catch(err => toast(err.message || 'Command failed'));
}

/* ---------- connection / SSE ---------- */
function connect() {
  if (state.es) { try { state.es.close(); } catch {} state.es = null; }
  const m = activeMachine();
  if (!m) { setConn('bad'); render(); return; }
  setConn('connecting');

  fetchServerInfo();
  const es = new EventSource(apiBase() + '/api/events?token=' + encodeURIComponent(m.token));
  state.es = es;
  es.addEventListener('status', (e) => {
    try { state.snapshot = JSON.parse(e.data); } catch { return; }
    setConn('ok');
    render();
  });
  es.onerror = () => { setConn(state.snapshot ? 'connecting' : 'bad'); renderTopbar(); };
}

function setConn(c) { state.conn = c; }

async function fetchServerInfo() {
  state.serverInfo = null;
  try { state.serverInfo = await fetch(apiBase() + '/api/info').then(r => r.json()); }
  catch { state.serverInfo = null; }
  if (state.screen === 'machines') renderMachines();
}

/* ---------- rendering ---------- */
const $ = sel => document.querySelector(sel);

function render() {
  renderTopbar();
  if (state.screen === 'jog') renderJog();
  else if (state.screen === 'status') renderStatus();
  else if (state.screen === 'projects') renderProjects();
  else if (state.screen === 'machines') renderMachines();
}

function renderTopbar() {
  const m = activeMachine();
  $('#machineName').textContent = m ? (state.snapshot?.machineName || m.name) : 'No machine';
  const dot = $('#connDot');
  dot.className = 'dot ' + (state.conn === 'ok' ? 'ok' : state.conn === 'connecting' ? 'connecting' : 'bad');

  const banner = $('#banner');
  if (!m) { banner.classList.add('hidden'); }
  else if (state.conn === 'bad') { banner.textContent = 'Disconnected - check the machine and WiFi'; banner.classList.remove('hidden'); }
  else if (state.conn === 'connecting' && !state.snapshot) { banner.textContent = 'Connecting...'; banner.classList.remove('hidden'); }
  else if (state.snapshot && !state.snapshot.controller.youHoldControl && state.snapshot.controller.heldBy) {
    banner.textContent = 'View-only \u2014 ' + state.snapshot.controller.heldBy + ' is in control'; banner.classList.remove('hidden');
  } else banner.classList.add('hidden');
}

function fmtNum(v) { return (v >= 0 ? ' ' : '') + Number(v).toFixed(4); }

function renderJog() {
  const s = state.snapshot;
  const dro = $('#dro');
  const p = s ? s.machine.pos : { x: 0, y: 0, z: 0, a: 0 };
  const homed = s ? s.machine.homed : { x: true, y: true, z: true, a: true };
  const units = machineUnits();
  const axes = [['x', 'X', p.x], ['y', 'Y', p.y], ['z', 'Z', p.z], ['a', 'A', p.a]];
  dro.innerHTML = axes.map(([cls, name, val]) => `
    <div class="dcell ${cls}">
      <span class="ax">${name}</span>
      <span class="dval">${fmtNum(val)}</span>
      <span class="unit">${homed[cls] ? units : '<span class="warn">&#9888;</span>'}</span>
    </div>`).join('');

  renderMode();
  renderStepChips();
  $('#stepUnit').textContent = units;

  const fc = feedCfg();
  const fs = $('#feedSlider');
  fs.min = fc.min; fs.max = fc.max; fs.step = fc.step; fs.value = currentFeed();
  $('#feedVal').textContent = currentFeed() + ' ' + fc.unit;

  renderSpindle();
  renderCamera('#camWrap', '#camImg');
}

function renderMode() {
  const mode = state.prefs.jogMode;
  $('#modeSeg').querySelectorAll('button').forEach(b =>
    b.classList.toggle('active', b.dataset.mode === mode));
  $('#stepBlock').classList.toggle('disabled', mode === 'cont');
}

function renderSpindle() {
  const on = !!(state.snapshot && state.snapshot.machine && state.snapshot.machine.spindleOn);
  $('#spindleSlider').value = state.prefs.spindle;
  $('#spindleVal').textContent = state.prefs.spindle + ' rpm' + (on ? ' \u2022 ON' : '');
  const btn = $('#spindleToggle');
  btn.textContent = on ? 'ON' : 'OFF';
  btn.classList.toggle('on', on);
}

function jogStepSizes() {
  if (isInch()) return [0.001, 0.01, 0.1, 1];
  return (state.snapshot && state.snapshot.jogStepSizes) || [0.01, 0.1, 1, 10];
}

function setStep(v) {
  if (isInch()) state.prefs.stepIn = v; else state.prefs.step = v;
  savePrefs();
  renderStepChips();
}

function renderStepChips() {
  const sizes = jogStepSizes();
  let cur = currentStep();
  if (!sizes.includes(cur)) { cur = sizes[Math.floor(sizes.length / 2)]; setStep(cur); return; }
  const wrap = $('#stepChips');
  wrap.innerHTML = sizes.map(sz => `<button data-step="${sz}" class="${sz == cur ? 'active' : ''}">${sz}</button>`).join('');
  wrap.querySelectorAll('button').forEach(b => b.onclick = () => setStep(parseFloat(b.dataset.step)));
}

function renderCamera(wrapSel, imgSel) {
  const url = state.snapshot && state.snapshot.cameraUrl;
  const wrap = $(wrapSel);
  if (url) { $(imgSel).src = url; wrap.classList.remove('hidden'); }
  else wrap.classList.add('hidden');
}

function fmtClock(sec) {
  sec = Math.max(0, Math.round(sec));
  const h = Math.floor(sec / 3600), m = Math.floor((sec % 3600) / 60), s = sec % 60;
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}` : `${m}:${String(s).padStart(2, '0')}`;
}
function fmtTimeOfDay(date) {
  let h = date.getHours(), m = date.getMinutes();
  const ap = h >= 12 ? 'PM' : 'AM'; h = h % 12 || 12;
  return `${h}:${String(m).padStart(2, '0')} ${ap}`;
}

function renderStatus() {
  const s = state.snapshot;
  const card = $('#statusCard');
  if (!s) { card.innerHTML = '<div class="sub">No data.</div>'; return; }
  const mc = s.machine, mo = s.maestro;

  let stateChip = 'idle', stateTxt = 'Idle';
  if (mc.estopped) { stateChip = 'alarm'; stateTxt = 'E-STOP'; }
  else if (mc.feedHold) { stateChip = 'hold'; stateTxt = 'Feed Hold'; }
  else if (mc.cycleRunning || mo.running) { stateChip = 'run'; stateTxt = 'Running'; }

  const total = mo.fileTotalLines || 0;
  let frac = 0;
  if (total > 0) frac = mo.fileCurrentLine / total;
  else if (mo.estimateSeconds > 0) frac = mo.elapsedSeconds / mo.estimateSeconds;
  frac = Math.max(0, Math.min(1, frac));

  const remaining = mo.remainingSeconds || Math.max(0, mo.estimateSeconds - mo.elapsedSeconds);
  const showClock = mo.running && mo.activeStepIndex >= 0;
  const eta = (showClock && remaining > 0) ? ('done ~' + fmtTimeOfDay(new Date(Date.now() + remaining * 1000))) : '';
  const activeStep = (mo.steps && mo.activeStepIndex >= 0) ? mo.steps[mo.activeStepIndex] : null;

  card.innerHTML = `
    <div style="display:flex;justify-content:space-between;align-items:center">
      <h3>${escapeHtml(projectName(mo.activeProjectId))}</h3>
      <span class="chip ${stateChip === 'run' ? 'run' : stateChip === 'hold' ? 'hold' : stateChip === 'alarm' ? 'alarm' : 'idle'}">${stateTxt}</span>
    </div>
    <div class="sub">${activeStep ? ('Step ' + (mo.activeStepIndex + 1) + ': ' + escapeHtml(activeStep.label)) : escapeHtml(mo.statusText || 'Ready')}</div>
    ${showClock ? `
      <div class="bigclock">${fmtClock(remaining)}</div>
      <div class="clock-caption">${remaining > 0 ? 'ESTIMATED TIME REMAINING' : 'ELAPSED ' + fmtClock(mo.elapsedSeconds)}</div>
      <div class="clock-caption">${eta}</div>` : ''}
    <div class="progress"><div style="width:${(frac * 100).toFixed(1)}%"></div></div>
    <div class="sub">${total > 0 ? ('Line ' + mo.fileCurrentLine + ' / ' + total) : 'Line ' + mc.gcodeLine}</div>
    <div class="statgrid">
      <div class="s"><div class="k">SPINDLE</div><div class="v">${Math.round(mc.spindleRpm)}</div></div>
      <div class="s"><div class="k">FEED OVR</div><div class="v">${mc.feedOverride}%</div></div>
      <div class="s"><div class="k">ELAPSED</div><div class="v">${fmtClock(mo.elapsedSeconds)}</div></div>
    </div>`;

  renderPrompt(mo);
  renderCamera('#camWrap2', '#camImg2');
}

function renderPrompt(mo) {
  const card = $('#promptCard');
  if (!mo.promptWaiting) { card.classList.add('hidden'); return; }
  card.classList.remove('hidden');
  const photo = mo.promptPhotoUrl ? `<img src="${apiBase() + mo.promptPhotoUrl}" alt="step" />` : '';
  card.innerHTML = `
    <h3>${mo.promptIsGateOnly ? 'Operator Action' : 'Tool Change'}</h3>
    <div class="sub">${escapeHtml(mo.promptText || '')}</div>
    ${photo}
    <div class="btns">
      <button class="act danger" id="promptCancel">Cancel</button>
      <button class="act primary" id="promptConfirm">Confirm</button>
    </div>`;
  $('#promptConfirm').onclick = () => cmd('/api/maestro/confirm');
  $('#promptCancel').onclick = () => cmd('/api/maestro/cancel');
}

function projectName(id) {
  if (!state.projects) return id || 'No project';
  const p = state.projects.find(p => p.id === id);
  return p ? p.name : (id || 'No project');
}

function renderProjects() {
  const s = state.snapshot;
  const sel = $('#projectSelect');
  if (state.projects) {
    const cur = s ? s.maestro.activeProjectId : '';
    sel.innerHTML = state.projects.map(p => `<option value="${p.id}" ${p.id === cur ? 'selected' : ''}>${escapeHtml(p.name)}</option>`).join('');
  }
  sel.onchange = () => cmd('/api/maestro/select', { projectId: sel.value });

  const list = $('#stepList');
  const steps = s ? s.maestro.steps : [];
  list.innerHTML = steps.map(st => {
    const meta = [st.type === 'gate' ? 'Gate' : 'Operation', st.toolLabel, st.lastRunSeconds ? ('~' + fmtClock(st.lastRunSeconds)) : '']
      .filter(Boolean).join('  \u00b7  ');
    return `<div class="step">
      <span class="idx">${st.index + 1}</span>
      <div class="info"><div class="lbl">${escapeHtml(st.label)}</div><div class="meta">${escapeHtml(meta)}</div></div>
      <span class="st ${st.status}">${st.status.toUpperCase()}</span>
      <button class="runbtn hold" data-runstep="${st.index}">Run</button>
    </div>`;
  }).join('');
  list.querySelectorAll('[data-runstep]').forEach(b =>
    attachHold(b, () => cmd('/api/maestro/run-step', { index: parseInt(b.dataset.runstep, 10) })));
}

function renderMachines() {
  const list = $('#machineList');
  if (state.machines.length === 0) { list.innerHTML = '<div class="sub">No machines yet. Add one below.</div>'; return; }
  list.innerHTML = state.machines.map(m => `
    <div class="mrow ${m.id === state.activeId ? 'active' : ''}">
      <div class="info"><div class="nm">${escapeHtml(m.name)}</div><div class="host">${escapeHtml(m.host)} &middot; ${m.units === 'in' ? 'SAE (in)' : 'Metric (mm)'}</div></div>
      <button class="mini" data-switch="${m.id}">${m.id === state.activeId ? 'Active' : 'Connect'}</button>
      <button class="mini" data-rename="${m.id}">Edit</button>
      <button class="mini" data-remove="${m.id}">Remove</button>
    </div>`).join('');
  list.querySelectorAll('[data-switch]').forEach(b => b.onclick = () => switchMachine(b.dataset.switch));
  list.querySelectorAll('[data-rename]').forEach(b => b.onclick = () => renameMachine(b.dataset.rename));
  list.querySelectorAll('[data-remove]').forEach(b => b.onclick = () => removeMachine(b.dataset.remove));

  const info = state.serverInfo;
  if (info && state.conn === 'ok') {
    const foot = document.createElement('div');
    foot.className = 'sub';
    foot.style.marginTop = '4px';
    foot.textContent = 'Active server: v' + (info.version || '?') + '  \u00b7  build ' + (info.build || '?');
    list.appendChild(foot);
  }
}

function escapeHtml(s) { return String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

/* ---------- LAN auto-discovery ---------- */
// Ask a known Maestro server (the active machine, or the one serving this PWA) for the
// other machines it has heard on the network, plus itself.
function discoverBase() {
  if (state.conn === 'ok') { const b = apiBase(); if (b) return b; }
  if (location.protocol.indexOf('http') === 0) return location.origin;
  return '';
}

async function renderDiscovered() {
  const box = $('#discoverList');
  if (!box) return;
  const base = discoverBase();
  if (!base) { box.innerHTML = '<div class="sub">Connect to one machine first to discover others.</div>'; return; }
  box.innerHTML = '<div class="sub">Scanning your network\u2026</div>';

  let data;
  try { data = await fetch(base + '/api/peers').then(r => r.json()); }
  catch { box.innerHTML = '<div class="sub">Auto-discovery unavailable on this server.</div>'; return; }

  const cands = [];
  if (data.self) {
    try {
      const u = new URL(base);
      cands.push({ name: data.self.machineName || u.hostname, host: u.hostname, port: parseInt(u.port || '8723', 10), machineId: data.self.machineId });
    } catch { }
  }
  (data.peers || []).forEach(p => cands.push({ name: p.machineName || p.host, host: p.host, port: p.port, machineId: p.machineId }));

  // Hide machines already in the list (matched by machineId).
  const known = new Set(state.machines.map(m => m.machineId).filter(Boolean));
  const list = cands.filter(c => !(c.machineId && known.has(c.machineId)));

  if (list.length === 0) { box.innerHTML = '<div class="sub">No new machines found. Tap Rescan after powering one on.</div>'; return; }

  box.innerHTML = list.map((c, i) => `
    <div class="mrow">
      <div class="info"><div class="nm">${escapeHtml(c.name)}</div><div class="host">${escapeHtml(c.host)}:${c.port}</div></div>
      <button class="mini" data-peer="${i}">Connect</button>
    </div>`).join('');
  box.querySelectorAll('[data-peer]').forEach(b =>
    b.onclick = () => { const c = list[parseInt(b.dataset.peer, 10)]; openAddMachine({ host: c.host, port: c.port, name: c.name }); });
}

/* ---------- catalog (projects/tools) ---------- */
async function loadCatalog() {
  try { const r = await api('/api/projects'); state.projects = r.projects || []; }
  catch { state.projects = null; }
  render();
}

/* ---------- machine management ---------- */
function switchMachine(id) {
  setActive(id);
  state.snapshot = null;
  connect();
  loadCatalog();
  showScreen('jog');
}
function renameMachine(id) {
  const m = state.machines.find(x => x.id === id); if (!m) return;
  const cur = m.units === 'in' ? 'in' : 'mm';
  openModal(`<h3>Edit machine</h3>
    <label>Name</label><input id="mName" value="${escapeHtml(m.name)}" />
    <label>Units (must match the machine's configuration)</label>
    <select id="mUnits" class="project-select">
      <option value="mm" ${cur === 'mm' ? 'selected' : ''}>Metric (mm)</option>
      <option value="in" ${cur === 'in' ? 'selected' : ''}>Imperial / SAE (in)</option>
    </select>
    <div class="actions"><button class="cancel" id="mCancel">Cancel</button><button class="ok" id="mOk">Save</button></div>`, () => {
    $('#mCancel').onclick = closeModal;
    $('#mOk').onclick = () => {
      m.name = $('#mName').value.trim() || m.name;
      m.units = $('#mUnits').value === 'in' ? 'in' : 'mm';
      saveMachines(); closeModal(); render();
    };
  });
}
function removeMachine(id) {
  state.machines = state.machines.filter(m => m.id !== id);
  saveMachines();
  if (state.activeId === id) { setActive(state.machines[0] ? state.machines[0].id : null); state.snapshot = null; connect(); loadCatalog(); }
  render();
}

function openAddMachine(prefill) {
  prefill = prefill || {};
  openModal(`<h3>Connect a machine</h3>
    <label>Address (IP or host)</label><input id="aHost" placeholder="192.168.1.50" inputmode="decimal" value="${escapeHtml(prefill.host || '')}" />
    <label>Port</label><input id="aPort" value="${escapeHtml(String(prefill.port || 8723))}" inputmode="numeric" />
    <label>Units (must match the machine's configuration)</label>
    <select id="aUnits" class="project-select">
      <option value="mm">Metric (mm)</option>
      <option value="in">Imperial / SAE (in)</option>
    </select>
    <label>This device's name (shown when it holds control)</label>
    <input id="aDevice" value="${escapeHtml(deviceName())}" maxlength="40" />
    <div class="err" id="aErr"></div>
    <div class="actions"><button class="cancel" id="aCancel">Cancel</button><button class="ok" id="aNext">Next</button></div>`, () => {
    $('#aCancel').onclick = closeModal;
    $('#aNext').onclick = async () => {
      const host = $('#aHost').value.trim();
      const port = $('#aPort').value.trim() || '8723';
      const units = $('#aUnits').value === 'in' ? 'in' : 'mm';
      const dev = $('#aDevice').value.trim();
      if (dev) { state.prefs.deviceName = dev; savePrefs(); }
      if (!host) { $('#aErr').textContent = 'Enter an address.'; return; }
      const base = (host.startsWith('http') ? host : 'http://' + host) + (host.includes(':') || host.startsWith('http') ? '' : ':' + port);
      try {
        const info = await fetch(base.replace(/\/$/, '') + '/api/info').then(r => r.json());
        pairStep(base.replace(/\/$/, ''), info, units);
      } catch { $('#aErr').textContent = 'Could not reach that machine.'; }
    };
  });
}

function pairStep(base, info, units) {
  openModal(`<h3>Pair "${escapeHtml(info.machineName)}"</h3>
    ${info.requiresPin ? '<label>Enter the PIN shown on the machine\'s Mobile tab</label><input id="pPin" class="pinbox" inputmode="numeric" maxlength="4" />' : '<div class="sub">No PIN required.</div>'}
    <div class="err" id="pErr"></div>
    <div class="actions"><button class="cancel" id="pCancel">Cancel</button><button class="ok" id="pOk">Pair</button></div>`, () => {
    $('#pCancel').onclick = closeModal;
    $('#pOk').onclick = async () => {
      const pin = info.requiresPin ? ($('#pPin').value.trim()) : '';
      try {
        const res = await fetch(base + '/api/pair', {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ pin, client: deviceName() })
        }).then(async r => { if (!r.ok) throw new Error((await r.json()).message || 'Pairing failed'); return r.json(); });
        const machine = { id: 'm_' + Date.now(), name: res.machineName || info.machineName, host: base, token: res.token, machineId: res.machineId, units: units === 'in' ? 'in' : 'mm' };
        state.machines.push(machine); saveMachines();
        setActive(machine.id); state.snapshot = null;
        closeModal(); connect(); loadCatalog(); showScreen('jog');
      } catch (e) { $('#pErr').textContent = e.message || 'Pairing failed'; }
    };
  });
}

/* ---------- modal ---------- */
function openModal(html, wire) {
  $('#modalCard').innerHTML = html;
  $('#modal').classList.remove('hidden');
  if (wire) wire();
}
function closeModal() { $('#modal').classList.add('hidden'); }

/* ---------- toast ---------- */
let toastTimer = null;
function toast(msg) {
  let t = $('#banner');
  t.textContent = msg; t.classList.remove('hidden');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(renderTopbar, 2500);
}

/* ---------- navigation ---------- */
function showScreen(name) {
  state.screen = name;
  ['jog', 'status', 'projects', 'machines'].forEach(s =>
    $('#screen-' + s).classList.toggle('hidden', s !== name));
  document.querySelectorAll('.tab').forEach(t => t.classList.toggle('active', t.dataset.screen === name));
  render();
  if (name === 'machines') renderDiscovered();
}

/* ---------- jog interaction ---------- */
/* Mode is chosen explicitly (CONT/STEP). Continuous = dead-man: hold to move,
   release to stop, with a keepalive so the server watchdog stops on disconnect. */
let jogState = { contActive: false, keepAlive: null, el: null };

function jogStart(el) {
  const axis = el.dataset.axis, dir = parseInt(el.dataset.dir, 10);
  jogState.el = el;
  jogState.contActive = false;
  if (state.prefs.jogMode === 'cont') {
    jogState.contActive = true;
    el.classList.add('jogging');
    cmd('/api/jog', { axis, dir, mode: 'cont', feed: currentFeed() });
    jogState.keepAlive = setInterval(() => api('/api/jog/keepalive', 'POST', {}).catch(() => {}), 250);
  } else {
    cmd('/api/jog', { axis, dir, mode: 'step', step: currentStep(), feed: currentFeed() });
  }
}

function jogEnd() {
  const el = jogState.el;
  if (jogState.contActive) {
    clearInterval(jogState.keepAlive);
    cmd('/api/jog/stop');
    if (el) el.classList.remove('jogging');
  }
  jogState = { contActive: false, keepAlive: null, el: null };
}

function setMode(mode) {
  state.prefs.jogMode = mode === 'step' ? 'step' : 'cont';
  savePrefs();
  renderMode();
}

/* ---------- hold-to-confirm ---------- */
function attachHold(el, action, ms = 800) {
  let timer = null, fill = el.querySelector('.holdfill');
  if (!fill && (el.classList.contains('act') || el.classList.contains('chip-act') || el.classList.contains('zbtn') || el.classList.contains('spin-btn'))) {
    fill = document.createElement('div'); fill.className = 'holdfill'; el.appendChild(fill);
  }

  const start = (ev) => {
    ev.preventDefault();
    if (fill) { fill.style.transition = `width ${ms}ms linear`; fill.style.width = '100%'; }
    timer = setTimeout(() => { if (fill) { fill.style.transition = 'none'; fill.style.width = '0%'; } if (navigator.vibrate) navigator.vibrate(30); action(); }, ms);
  };
  const cancel = () => { clearTimeout(timer); if (fill) { fill.style.transition = 'width 120ms'; fill.style.width = '0%'; } };
  el.addEventListener('pointerdown', start);
  el.addEventListener('pointerup', cancel);
  el.addEventListener('pointerleave', cancel);
  el.addEventListener('pointercancel', cancel);
}

/* ---------- action dispatch ---------- */
const ACTIONS = {
  'home-all': () => cmd('/api/home', { axis: 'all' }),
  'autozero': () => cmd('/api/autozero'),
  'park-custom': () => cmd('/api/park', { type: 'custom' }),
  'run-all': () => cmd('/api/maestro/run-all'),
  'reset': () => cmd('/api/maestro/reset'),
  'abort': () => cmd('/api/maestro/abort'),
};

/* ---------- wiring ---------- */
function wireStaticUi() {
  // tabs
  document.querySelectorAll('.tab').forEach(t => t.onclick = () => showScreen(t.dataset.screen));
  // machine switcher / add
  $('#machineBtn').onclick = () => showScreen('machines');
  $('#addMachineBtn').onclick = () => openAddMachine();
  $('#rescanBtn').onclick = renderDiscovered;

  // jog buttons (X/Y cross + Z column + A aux)
  document.querySelectorAll('.jbtn').forEach(b => {
    b.addEventListener('pointerdown', (e) => { e.preventDefault(); jogStart(b); });
    b.addEventListener('pointerup', jogEnd);
    b.addEventListener('pointerleave', jogEnd);
    b.addEventListener('pointercancel', jogEnd);
  });

  // jog mode (continuous / step)
  $('#modeSeg').querySelectorAll('button').forEach(b => b.onclick = () => setMode(b.dataset.mode));

  // feed slider (unit-aware)
  $('#feedSlider').addEventListener('input', (e) => {
    const fc = feedCfg();
    state.prefs[fc.key] = parseInt(e.target.value, 10);
    $('#feedVal').textContent = state.prefs[fc.key] + ' ' + fc.unit;
  });
  $('#feedSlider').addEventListener('change', savePrefs);

  // spindle slider: updates target rpm; sends live if the spindle is already on
  $('#spindleSlider').addEventListener('input', (e) => {
    state.prefs.spindle = parseInt(e.target.value, 10);
    const on = !!(state.snapshot && state.snapshot.machine && state.snapshot.machine.spindleOn);
    $('#spindleVal').textContent = state.prefs.spindle + ' rpm' + (on ? ' \u2022 ON' : '');
  });
  $('#spindleSlider').addEventListener('change', () => {
    savePrefs();
    if (state.snapshot && state.snapshot.machine && state.snapshot.machine.spindleOn)
      cmd('/api/spindle', { on: true, rpm: state.prefs.spindle });
  });
  // toggle: hold to turn ON (spinning tool), single tap to turn OFF
  const spinBtn = $('#spindleToggle');
  attachHold(spinBtn, () => {
    const on = !!(state.snapshot && state.snapshot.machine && state.snapshot.machine.spindleOn);
    if (!on) cmd('/api/spindle', { on: true, rpm: state.prefs.spindle });
  }, 700);
  spinBtn.addEventListener('click', () => {
    const on = !!(state.snapshot && state.snapshot.machine && state.snapshot.machine.spindleOn);
    if (on) cmd('/api/spindle', { on: false, rpm: 0 });
  });

  // hold-to-confirm action buttons (jog action chips + other screens)
  document.querySelectorAll('.hold[data-act]').forEach(b => attachHold(b, () => ACTIONS[b.dataset.act] && ACTIONS[b.dataset.act]()));

  // safety (one-tap)
  const tapEstop = () => cmd('/api/estop');
  $('#estopBtn').onclick = tapEstop;
  $('#estopBig').onclick = tapEstop;
  $('#feedHoldBtn').onclick = () => cmd('/api/feedhold');
  $('#resumeBtn').onclick = () => cmd('/api/resume');

  // keep screen awake during a job (best-effort)
  setupWakeLock();
}

let wakeLock = null;
async function setupWakeLock() {
  document.addEventListener('visibilitychange', async () => {
    if (document.visibilityState === 'visible' && state.snapshot?.maestro?.running) requestWake();
  });
}
async function requestWake() {
  try { if ('wakeLock' in navigator && !wakeLock) wakeLock = await navigator.wakeLock.request('screen'); } catch {}
}

/* ---------- service worker ---------- */
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => navigator.serviceWorker.register('sw.js').catch(() => {}));
}

/* ---------- boot ---------- */
function boot() {
  loadAll();
  wireStaticUi();
  if (state.machines.length && !state.activeId) setActive(state.machines[0].id);
  if (activeMachine()) { connect(); loadCatalog(); showScreen('jog'); }
  else showScreen('machines');
  render();
}
boot();
