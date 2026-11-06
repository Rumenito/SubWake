'use strict';
// The window talks to the C# host through WebView2 messages:
//   host -> page: {type:'state', ...}   on every change and every 2 s
//                 {type:'levels', levels:{output: dBFS}}   every 250 ms while the window is open
//                 {type:'apps', apps:[{name, title}]}       reply to 'list-apps'
//   page -> host: {type:'ready'|'set'|'startup'|'play-now'|'snooze'|'resume'|'list-apps'|'calibrate'|'open-log'|'check-updates'|'open-update'|'quit', ...}

const $ = id => document.getElementById(id);
const host = window.chrome?.webview;
const send = message => host?.postMessage(message);
let state = null;

const minus = text => String(text).replace('-', '−');
const pad = n => String(n).padStart(2, '0');
const clock = ms => { const d = new Date(ms); return `${pad(d.getHours())}:${pad(d.getMinutes())}`; };
const countdown = ms => {
  const s = Math.max(0, Math.round(ms / 1000));
  return `${Math.floor(s / 60)}:${pad(s % 60)}`;
};
const duration = minutes => minutes < 60 ? `${minutes} min` : `${Math.floor(minutes / 60)} h${minutes % 60 ? ` ${minutes % 60} min` : ''}`;
const clamp01 = x => Math.min(1, Math.max(0, x));

// A locked switch stays visibly on but ignores clicks (used for the last remaining tone).
function setSwitch(button, on, locked = false) {
  button.setAttribute('aria-checked', String(!!on));
  button.dataset.locked = String(locked);
  button.setAttribute('aria-disabled', String(locked));
  button.title = locked ? 'At least one tone stays on' : '';
}

function setSegment(group, value) {
  for (const b of group.querySelectorAll('button'))
    b.setAttribute('aria-checked', String(Number(b.dataset.value) === value));
}

function describeTones(s) {
  const parts = [];
  if (s.toneLow) parts.push(`${s.lowToneHz} Hz`);
  if (s.tone20) parts.push('20 Hz');
  if (s.tone20k) parts.push('20 kHz');
  return parts.join(' + ');
}

// Places a marker over a range input at a value (the thumb travels 18 px less than the track).
function placeMark(mark, input, value) {
  const min = Number(input.min), max = Number(input.max);
  const show = value != null && value >= min && value <= max;
  mark.hidden = !show;
  if (show) mark.style.left = `calc(9px + (100% - 18px) * ${(value - min) / (max - min)})`;
}

function render() {
  if (!state) return;
  const s = state.settings;

  $('status-pill').dataset.mode = state.status.mode;
  $('status-text').textContent = state.status.text;
  $('status-pill').title = state.status.text;
  setSwitch($('enabled'), s.enabled);
  $('hero-summary').textContent =
    `${describeTones(s)} · ${s.durationSeconds} s every ${s.intervalMinutes} min · ${minus(s.levelDb)} dBFS`;

  $('last').textContent = state.lastBurstAt ? clock(state.lastBurstAt) : '—';
  $('last').title = state.lastBurstText || '';
  $('output-state').textContent = state.deviceFound ? 'Connected' : 'Missing';
  $('output-state').classList.toggle('warn', !state.deviceFound);
  $('error-line').hidden = !state.lastError;
  $('error-line').textContent = state.lastError || '';
  $('play-now').disabled = state.playing || state.status.mode === 'calibrating';
  $('play-now').textContent = state.playing ? 'Playing…' : 'Play burst now';

  const snoozed = state.snoozedUntil != null;
  $('snooze').hidden = snoozed;
  $('snoozed').hidden = !snoozed;
  if (snoozed) $('snoozed-text').textContent = `Paused until ${clock(state.snoozedUntil)}`;
  for (const b of $('snooze').querySelectorAll('button')) b.disabled = !s.enabled;

  // Never let the last tone be switched off: a burst needs at least one.
  const tones = ['toneLow', 'tone20', 'tone20k'];
  const onCount = tones.filter(t => s[t]).length;
  for (const button of document.querySelectorAll('[data-setting]')) {
    const key = button.dataset.setting;
    setSwitch(button, s[key], tones.includes(key) && s[key] && onCount === 1);
  }
  setSwitch($('startup'), state.startup);

  level.sync(s.levelDb);
  threshold.sync(s.signalThresholdDb);
  const wake = s.calibratedWakeDb;
  placeMark($('level-mark'), $('level'), wake);
  placeMark($('threshold-mark'), $('threshold'), wake);
  $('level-mark').title = $('threshold-mark').title = wake == null ? '' : `The sub woke at ${minus(wake)} dBFS in the last calibration`;
  $('level-mark-text').hidden = wake == null;
  $('level-mark-text').textContent = wake == null ? '' : `Woke at ${minus(wake)}`;
  const match = $('match-calibration');
  match.hidden = wake == null || wake < -70 || wake > -10 || wake === s.signalThresholdDb;
  match.textContent = wake == null ? '' : `Match calibration (${minus(wake)} dBFS)`;

  $('calibrate-open').disabled = state.status.mode === 'calibrating';
  setSegment($('standby'), s.subStandbyMinutes);
  setSegment($('low-hz'), s.lowToneHz);
  $('low-hz-field').hidden = !s.toneLow;
  renderIntervals(state.intervals);
  setSegment($('interval'), s.intervalMinutes);
  setSegment($('duration'), s.durationSeconds);
  setSegment($('away'), s.awayMinutes);
  $('away-field').hidden = !s.sleepWhenAway;

  renderDevices(s);
  renderAudio(s);
  renderHoldApps(s);
  renderTimeline();

  $('quiet-times').hidden = !s.quietHours;
  if (document.activeElement !== $('quiet-start')) $('quiet-start').value = s.quietStart;
  if (document.activeElement !== $('quiet-end')) $('quiet-end').value = s.quietEnd;
  $('version').textContent = `v${state.version}`;
  renderUpdate();
  renderSetup(s);

  tick();
  renderCalibration();
}

// Interval choices depend on the standby time: every one stays under it.
function renderIntervals(intervals) {
  const group = $('interval');
  const key = intervals.join(',');
  if (group.dataset.key === key) return;
  group.dataset.key = key;
  group.replaceChildren(...intervals.map((m, i) => {
    const b = document.createElement('button');
    b.setAttribute('role', 'radio');
    b.dataset.value = m;
    b.textContent = i === 0 ? `${m} min` : String(m);
    return b;
  }));
}

function renderUpdate() {
  const u = state.update;
  $('update-banner').hidden = !u.available;
  $('update-text').textContent = u.available ? `SubWake ${u.available} is available. You have ${state.version}.` : '';
  const button = $('check-updates');
  button.disabled = u.checking;
  button.textContent = u.checking ? 'Checking…' : u.status || 'Check for updates';
}

function renderDevices(s) {
  const select = $('device');
  const options = state.devices.map(d => ({ value: d.id, label: d.name }));
  if (!state.deviceFound || !options.some(o => o.value === s.deviceId))
    options.unshift({ value: s.deviceId || '', label: state.deviceFound ? s.deviceName : `${s.deviceName}  (not connected)` });
  const key = JSON.stringify(options);
  if (select.dataset.options !== key) {
    select.replaceChildren(...options.map(o => new Option(o.label, o.value)));
    select.dataset.options = key;
  }
  select.value = s.deviceId || '';
}

// --- First-run setup ------------------------------------------------------------------------------

const setup = $('setup');
let setupSuggested = false;

function renderSetup(s) {
  if (s.setupDone) { if (setup.open) setup.close(); return; }
  if (!setup.open && !dialog.open) setup.showModal();
  // Suggest Windows' default output once, if nothing is chosen yet.
  if (!s.deviceId && !setupSuggested && state.defaultOutputId) {
    const d = state.devices.find(d => d.id === state.defaultOutputId);
    if (d) { setupSuggested = true; set({ deviceId: d.id, deviceName: d.name }); }
  }
  const select = $('setup-device');
  const options = [{ value: '', label: state.devices.length ? 'Choose an output…' : 'No outputs found' },
    ...state.devices.map(d => ({ value: d.id, label: d.id === state.defaultOutputId ? `${d.name}  (Windows default)` : d.name }))];
  const key = JSON.stringify(options);
  if (select.dataset.options !== key) {
    select.replaceChildren(...options.map(o => new Option(o.label, o.value)));
    select.dataset.options = key;
  }
  if (document.activeElement !== select) select.value = state.devices.some(d => d.id === s.deviceId) ? s.deviceId : '';
  setSegment($('setup-standby'), s.subStandbyMinutes);
  setupLevel.sync(s.levelDb);
  const chosen = state.devices.some(d => d.id === s.deviceId);
  $('setup-test').disabled = !chosen || state.playing;
  $('setup-test').textContent = state.playing ? 'Playing…' : 'Play test burst';
  $('setup-done').disabled = !chosen;
}

// --- Audio on the watched outputs -----------------------------------------------------------------

const toPercent = db => clamp01((db + 70) / 70) * 100;   // Level bars run from −70 to 0 dBFS.

function renderAudio(s) {
  const a = state.audio;
  const playingNow = a.active.length > 0;
  $('audio-state').textContent = playingNow ? 'Playing' : a.watching.length ? 'Silent' : '—';
  $('audio-state').title = playingNow ? `Audio on ${a.active.join(', ')}` : '';

  const list = $('audio-outs');
  list.hidden = !s.skipWhileAudio;
  $('threshold-block').hidden = !s.skipWhileAudio;
  const key = JSON.stringify(a.watching.length ? a.watching : [a.error || '']);
  if (list.dataset.key !== key) {
    list.dataset.key = key;
    // Show "Out 7/8" rather than "Out 7/8 (CONNECT 6)" when every output shares the device name.
    const suffix = a.watching.length > 1 && a.watching.every(n => n.endsWith(a.watching[0].slice(a.watching[0].lastIndexOf(' ('))))
      ? a.watching[0].slice(a.watching[0].lastIndexOf(' (')) : '';
    list.replaceChildren(...(a.watching.length
      ? a.watching.map(name => {
          const li = document.createElement('li');
          li.dataset.name = name;
          li.title = name;
          const label = document.createElement('span');
          label.textContent = suffix && name.endsWith(suffix) ? name.slice(0, -suffix.length) : name;
          const bar = document.createElement('span');
          bar.className = 'lvl';
          bar.append(document.createElement('b'), document.createElement('s'));
          li.append(document.createElement('i'), label, bar);
          return li;
        })
      : [Object.assign(document.createElement('li'), { className: 'note', textContent: a.error || 'No outputs to watch.' })]));
  }
  for (const li of list.querySelectorAll('li[data-name]')) {
    li.classList.toggle('on', a.active.includes(li.dataset.name));
    li.querySelector('s').style.left = `${toPercent(s.signalThresholdDb)}%`;
  }

  $('watch-row').hidden = !s.skipWhileAudio;
  if (!s.skipWhileAudio) $('watch-editor').hidden = true;
  const custom = s.watchOutputIds != null;
  const n = a.watching.length;
  $('watch-mode').textContent = custom
    ? `Your choice: ${n} output${n === 1 ? '' : 's'}, including your sub's output.`
    : `Automatic: every output on the same device as your sub's output${n ? ` (${n} now)` : ''}.`;
  const editing = !$('watch-editor').hidden;
  $('watch-edit').hidden = editing;
  $('watch-edit').setAttribute('aria-expanded', String(editing));
  if (editing) renderWatchList(s);

  const recording = $('recording-now');
  recording.hidden = !(s.holdWhileRecording && a.recording.length);
  recording.textContent = a.recording.length ? `Recording now: ${a.recording.join(', ')}` : '';
}

// The outputs wait-for-silence watches: the burst output always, and the rest by choice.
function renderWatchList(s) {
  const list = $('watch-list');
  const key = JSON.stringify([state.devices.map(d => [d.id, d.name]), s.deviceId]);
  if (list.dataset.key !== key) {
    list.dataset.key = key;
    list.replaceChildren(...state.devices.map(d => {
      const li = document.createElement('li');
      const label = Object.assign(document.createElement('span'), { textContent: d.name, title: d.name });
      const sw = document.createElement('button');
      sw.className = 'switch';
      sw.setAttribute('role', 'switch');
      sw.setAttribute('aria-label', `Watch ${d.name}`);
      sw.dataset.id = d.id;
      li.append(label, sw);
      return li;
    }));
  }
  for (const sw of list.querySelectorAll('[data-id]')) {
    const own = sw.dataset.id === s.deviceId;
    const on = own || state.devices.find(d => d.id === sw.dataset.id)?.watched;
    sw.setAttribute('aria-checked', String(!!on));
    sw.dataset.locked = String(own);
    sw.setAttribute('aria-disabled', String(own));
    sw.title = own ? 'The burst output always counts' : '';
  }
}

function renderLevels(levels) {
  for (const li of $('audio-outs').querySelectorAll('li[data-name]')) {
    const db = levels[li.dataset.name];
    if (db == null) continue;
    li.querySelector('b').style.width = `${toPercent(db)}%`;
    li.querySelector('b').classList.toggle('over', state && db >= state.settings.signalThresholdDb);
    li.title = `${li.dataset.name} · ${db <= -100 ? 'silent' : `${minus(Math.round(db))} dBFS`}`;
  }
}

// --- Hold while these apps are open ---------------------------------------------------------------

function renderHoldApps(s) {
  const open = state.holdAppsOpen;
  $('hold-apps').replaceChildren(...s.holdApps.map(name => {
    const li = document.createElement('li');
    li.className = open.includes(name) ? 'on' : '';
    li.title = open.includes(name) ? `${name} is open: bursts are held` : `${name} isn't running`;
    const remove = Object.assign(document.createElement('button'), { className: 'chip-remove', textContent: '×', title: `Remove ${name}` });
    remove.dataset.remove = name;
    const label = document.createElement('span');
    label.textContent = name;
    li.append(document.createElement('i'), label, remove);
    return li;
  }));
}

function showAppPicker(apps) {
  const taken = state.settings.holdApps.map(a => a.toLowerCase());
  const choices = apps.filter(a => !taken.includes(a.name.toLowerCase()));
  const pick = $('app-pick');
  pick.replaceChildren(new Option(choices.length ? 'Choose an open app…' : 'No other apps are open', ''),
    ...choices.map(a => new Option(a.title && a.title !== a.name ? `${a.name} · ${a.title}` : a.name, a.name)));
  pick.hidden = false;
  pick.focus();
}

// --- Activity timeline ----------------------------------------------------------------------------

function renderTimeline() {
  const h = state.history;
  const span = h.to - h.from;
  const x = t => `${clamp01((t - h.from) / span) * 100}%`;
  const w = (a, b) => `${clamp01(((b ?? h.to) - Math.max(a, h.from)) / span) * 100}%`;
  const parts = [];
  for (const [a, b] of h.paused) parts.push(seg('paused', a, b));
  for (const [a, b] of h.music) parts.push(seg('music', a, b));
  for (const t of h.bursts) {
    const tick = document.createElement('div');
    tick.className = 'tick';
    tick.style.left = x(t);
    tick.title = `Burst at ${clock(t)}`;
    parts.push(tick);
  }
  $('timeline').replaceChildren(...parts);
  $('today').textContent = `Today: ${h.today.bursts} burst${h.today.bursts === 1 ? '' : 's'} · music ${duration(h.today.musicMinutes)}`;

  function seg(kind, a, b) {
    const el = document.createElement('div');
    el.className = `seg ${kind}`;
    el.style.left = x(a);
    el.style.width = w(a, b);
    el.title = `${kind === 'music' ? 'Music' : 'Paused'} ${clock(a)}–${b ? clock(b) : 'now'}`;
    return el;
  }
}

// --- Countdowns run locally between host updates --------------------------------------------------

function tick() {
  if (!state) return;
  $('next').textContent = state.playing ? 'Now'
    : state.audio.holding && state.nextBurstAt ? 'After audio'
    : state.nextBurstAt ? countdown(state.nextBurstAt - Date.now())
    : state.status.mode === 'off' ? 'Off' : 'Paused';

  const from = state.subTimerFrom;
  const elapsed = from == null ? null : Math.max(0, Date.now() - from);
  const asleep = elapsed != null && elapsed >= state.subStandbyMs;
  $('sub-timer-text').textContent = elapsed == null ? '—' : asleep ? 'Likely asleep' : `${countdown(elapsed)} / ${countdown(state.subStandbyMs)}`;
  $('sub-timer').classList.toggle('asleep', asleep);
  $('sub-timer').classList.toggle('late', !asleep && elapsed != null && elapsed > state.subStandbyMs * 0.8);
  $('sub-timer-fill').style.width = `${elapsed == null ? 0 : clamp01(elapsed / state.subStandbyMs) * 100}%`;

  const c = state.calibration;
  $('cal-wait-min').textContent = c.waitMinutes;
  if (c.phase === 'waiting') $('cal-wait').textContent = countdown(c.waitUntil - Date.now());
}
setInterval(tick, 1000);

// --- Calibration dialog ---------------------------------------------------------------------------

const dialog = $('calibrate');

function renderCalibration() {
  const c = state.calibration;
  const phase = dialog.open && c.phase === 'none' ? 'none' : c.phase;
  if (c.phase !== 'none' && !dialog.open) dialog.showModal();   // Reopen if calibration is running.
  for (const panel of dialog.querySelectorAll('[data-phase]')) panel.hidden = panel.dataset.phase !== phase;
  $('cal-note-waiting').hidden = !c.note;
  $('cal-note-waiting').textContent = c.note || '';
  if (c.phase === 'stepping') {
    $('cal-level').textContent = c.level == null ? 'Starting…' : `${minus(c.level)} dBFS`;
    const progress = c.level == null ? 0 : (c.level - c.startDb) / (c.maxDb - c.startDb);
    $('cal-progress').style.width = `${Math.round(clamp01(progress) * 100)}%`;
  }
  $('cal-message').textContent = c.message;
  const suggested = c.suggestedDb;
  $('cal-suggested').hidden = suggested == null;
  $('cal-suggested').textContent = suggested == null ? '' : `${minus(suggested)} dBFS`;
  $('cal-apply').hidden = suggested == null;
  $('cal-apply').textContent = suggested == null ? 'Use' : `Use ${minus(suggested)} dBFS`;
  $('cal-keep').textContent = suggested == null ? 'Close' : `Keep ${minus(state.settings.levelDb)} dBFS`;
}

$('calibrate-open').addEventListener('click', () => {
  for (const panel of dialog.querySelectorAll('[data-phase]')) panel.hidden = panel.dataset.phase !== 'none';
  dialog.showModal();
});
dialog.addEventListener('click', e => {
  const action = e.target.closest('[data-action]')?.dataset.action;
  if (!action) return;
  if (action === 'close' || action === 'apply') dialog.close();
  send({ type: 'calibrate', action });
});
dialog.addEventListener('cancel', e => {
  // Esc during a running calibration cancels it rather than hiding it.
  if (state && ['waiting', 'stepping'].includes(state.calibration.phase)) { e.preventDefault(); send({ type: 'calibrate', action: 'cancel' }); }
  else send({ type: 'calibrate', action: 'close' });
});

// --- Controls -------------------------------------------------------------------------------------

const set = changes => send({ type: 'set', settings: changes });

$('enabled').addEventListener('click', () => set({ enabled: !state.settings.enabled }));
$('play-now').addEventListener('click', () => send({ type: 'play-now' }));
$('startup').addEventListener('click', () => send({ type: 'startup', value: !state.startup }));
for (const button of document.querySelectorAll('[data-setting]'))
  button.addEventListener('click', () => {
    if (button.dataset.locked !== 'true') set({ [button.dataset.setting]: !state.settings[button.dataset.setting] });
  });
$('snooze').addEventListener('click', e => {
  const minutes = e.target.closest('[data-minutes]')?.dataset.minutes;
  if (minutes) send({ type: 'snooze', minutes: Number(minutes) });
});
$('resume').addEventListener('click', () => send({ type: 'resume' }));

// A dBFS slider set by hand: drag, arrow keys, or the mouse wheel (1 dB per notch, saved once scrolling pauses).
function slider(id, outputId, key) {
  const input = $(id);
  let pending = null;
  const show = db => {
    input.value = db;
    $(outputId).textContent = `${minus(db)} dBFS`;
    input.style.setProperty('--fill', `${(db - input.min) / (input.max - input.min) * 100}%`);
  };
  input.addEventListener('input', () => show(Number(input.value)));
  input.addEventListener('change', () => set({ [key]: Number(input.value) }));
  input.addEventListener('wheel', e => {
    e.preventDefault();
    const step = e.deltaY < 0 ? 1 : e.deltaY > 0 ? -1 : 0;
    const db = Math.min(Number(input.max), Math.max(Number(input.min), Number(input.value) + step));
    show(db);
    clearTimeout(pending);
    pending = setTimeout(() => { pending = null; set({ [key]: db }); }, 350);
  }, { passive: false });
  return { sync: db => { if (!pending && document.activeElement !== input) show(db); } };
}
const level = slider('level', 'level-value', 'levelDb');
const setupLevel = slider('setup-level', 'setup-level-value', 'levelDb');
const threshold = slider('threshold', 'threshold-value', 'signalThresholdDb');
$('match-calibration').addEventListener('click', () => set({ signalThresholdDb: state.settings.calibratedWakeDb }));

for (const [id, key] of [['interval', 'intervalMinutes'], ['duration', 'durationSeconds'], ['away', 'awayMinutes'],
                          ['standby', 'subStandbyMinutes'], ['setup-standby', 'subStandbyMinutes'], ['low-hz', 'lowToneHz']])
  $(id).addEventListener('click', e => {
    const value = e.target.closest('button')?.dataset.value;
    if (value) set({ [key]: Number(value) });
  });

$('device').addEventListener('change', e => {
  const device = state.devices.find(d => d.id === e.target.value);
  if (device) set({ deviceId: device.id, deviceName: device.name });
});

$('setup-device').addEventListener('change', e => {
  const device = state.devices.find(d => d.id === e.target.value);
  if (device) set({ deviceId: device.id, deviceName: device.name });
});
$('setup-test').addEventListener('click', () => send({ type: 'play-now' }));
$('setup-done').addEventListener('click', () => set({ setupDone: true }));
setup.addEventListener('cancel', e => e.preventDefault());   // Setup has to be finished; closing the window is fine.

$('watch-edit').addEventListener('click', () => { $('watch-editor').hidden = false; render(); });
$('watch-close').addEventListener('click', () => { $('watch-editor').hidden = true; render(); });
$('watch-auto').addEventListener('click', () => set({ watchOutputIds: null }));
$('watch-list').addEventListener('click', e => {
  const sw = e.target.closest('[data-id]');
  if (!sw || sw.dataset.locked === 'true') return;
  const s = state.settings;
  const watched = state.devices.filter(d => d.watched && d.id !== s.deviceId).map(d => d.id);
  const id = sw.dataset.id;
  set({ watchOutputIds: watched.includes(id) ? watched.filter(x => x !== id) : [...watched, id] });
});

$('check-updates').addEventListener('click', () => send({ type: 'check-updates' }));
$('update-open').addEventListener('click', () => send({ type: 'open-update' }));

$('add-app').addEventListener('click', () => send({ type: 'list-apps' }));
$('app-pick').addEventListener('change', e => {
  if (e.target.value) set({ holdApps: [...state.settings.holdApps, e.target.value] });
  e.target.hidden = true;
});
$('app-pick').addEventListener('blur', e => { e.target.hidden = true; });
$('hold-apps').addEventListener('click', e => {
  const name = e.target.closest('[data-remove]')?.dataset.remove;
  if (name) set({ holdApps: state.settings.holdApps.filter(a => a !== name) });
});

$('quiet-start').addEventListener('change', e => e.target.value && set({ quietStart: e.target.value }));
$('quiet-end').addEventListener('change', e => e.target.value && set({ quietEnd: e.target.value }));
$('open-log').addEventListener('click', () => send({ type: 'open-log' }));
$('quit').addEventListener('click', () => send({ type: 'quit' }));

host?.addEventListener('message', e => {
  const m = e.data;
  if (m?.type === 'state') { state = m; render(); }
  else if (m?.type === 'levels') renderLevels(m.levels);
  else if (m?.type === 'apps') showAppPicker(m.apps);
});
send({ type: 'ready' });
