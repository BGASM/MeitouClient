/* @ds-bundle: {"format":4,"namespace":"MeitouUI","components":[{"name":"Window"},{"name":"Card"},{"name":"Message"},{"name":"Button"},{"name":"Tabs"},{"name":"Checkbox"},{"name":"TimeControls"},{"name":"StatBar"},{"name":"SkillRow"},{"name":"SquadCard"},{"name":"InventoryGrid"},{"name":"ItemTooltip"},{"name":"Dialogue"},{"name":"SaveCard"},{"name":"MainMenu"}]} */
(function () {
  var React = window.React;
  var h = React.createElement;
  var useState = React.useState;

  var MINUS = '−';
  var ICONS = ['continue', 'new', 'load', 'options', 'credits', 'exit'];

  function cx() {
    return Array.prototype.filter.call(arguments, Boolean).join(' ');
  }
  function num(n) {
    return Number(n).toLocaleString('en-US');
  }
  function signed(n) {
    return n > 0 ? '+' + n : n < 0 ? MINUS + Math.abs(n) : '0';
  }
  function omit(p, keys) {
    var o = {};
    for (var k in p) if (keys.indexOf(k) < 0) o[k] = p[k];
    return o;
  }
  function clamp01(v) {
    return Math.max(0, Math.min(1, v));
  }
  // Controlled when `value` is given, otherwise keeps its own state.
  function useControlled(value, initial, onChange) {
    var s = useState(initial);
    var current = value !== undefined ? value : s[0];
    return [current, function (v) { if (value === undefined) s[1](v); if (onChange) onChange(v); }];
  }
  // A built-in icon name ("load") or an image URL.
  function icon(name) {
    if (!name) return null;
    if (ICONS.indexOf(name) >= 0) return h('span', { className: 'mt-icon mt-icon-' + name, 'aria-hidden': true });
    return h('img', { className: 'mt-icon', src: name, alt: '' });
  }

  function Window(p) {
    var closable = p.closable !== false;
    return h('section', { className: cx('mt-window', p.className), style: Object.assign({ width: p.width }, p.style), 'aria-label': p.title },
      h('header', { className: 'mt-window-bar' },
        h('h2', { className: 'mt-window-title' }, p.title),
        p.meta ? h('span', { className: 'mt-window-meta' }, p.meta) : null,
        closable ? h('button', { type: 'button', className: 'mt-close', 'aria-label': 'Close ' + p.title, onClick: p.onClose }) : null),
      h('div', { className: 'mt-window-body' },
        p.tabs ? h('div', { style: { marginBottom: 12 } }, p.tabs) : null,
        p.children,
        p.footer ? h('footer', { className: 'mt-window-foot' }, p.footer) : null));
  }

  function Card(p) {
    var cls = p.variant === 'plate' ? 'mt-plate' : cx('mt-card', p.floating && 'is-floating');
    return h('div', { className: cx(cls, p.className), style: p.style },
      p.title ? h('h3', null, p.title) : null, p.children);
  }

  function Message(p) {
    return h('div', { className: cx('mt-message', p.className), role: 'status', style: p.style }, h('span', null, p.children));
  }

  function Button(p) {
    var rest = omit(p, ['variant', 'size', 'pressed', 'icon', 'block', 'className', 'children']);
    return h('button', Object.assign({ type: 'button' }, rest, {
      'aria-pressed': p.pressed === undefined ? undefined : !!p.pressed,
      className: cx('mt-btn', p.variant === 'primary' && 'mt-btn-primary', p.variant === 'danger' && 'mt-btn-danger',
        p.size === 'sm' && 'mt-btn-sm', p.icon && 'has-icon', p.block && 'is-block', p.className)
    }), icon(p.icon), h('span', null, p.children));
  }

  function Tabs(p) {
    var items = (p.tabs || []).map(function (t) { return typeof t === 'string' ? { id: t, label: t } : t; });
    var st = useControlled(p.value, p.defaultValue !== undefined ? p.defaultValue : items[0] && items[0].id, p.onChange);
    return h('div', { className: cx('mt-tabs', p.className), role: 'tablist', 'aria-label': p.label },
      items.map(function (t) {
        return h('button', { key: t.id, type: 'button', role: 'tab', 'aria-selected': t.id === st[0], className: 'mt-tab', disabled: t.disabled, onClick: function () { st[1](t.id); } }, h('span', null, t.label));
      }));
  }

  function Checkbox(p) {
    var st = useControlled(p.checked, !!p.defaultChecked, p.onChange);
    return h('label', { className: cx('mt-check', p.disabled && 'is-disabled', p.className) },
      h('input', { type: 'checkbox', checked: st[0], disabled: p.disabled, onChange: function (e) { st[1](e.target.checked); } }),
      h('span', null, p.label));
  }

  var SPEED_NAMES = ['Pause', 'Normal speed', 'Fast', 'Fastest'];
  function TimeControls(p) {
    var st = useControlled(p.speed, p.defaultSpeed !== undefined ? p.defaultSpeed : 1, p.onChange);
    return h('div', { className: cx('mt-time', p.className) },
      h('div', { className: 'mt-speed', role: 'group', 'aria-label': 'Game speed' },
        [0, 1, 2, 3].map(function (n) {
          return h('button', { key: n, type: 'button', className: 'mt-speed-btn s' + n, 'aria-pressed': st[0] === n, 'aria-label': SPEED_NAMES[n], title: SPEED_NAMES[n], onClick: function () { st[1](n); } });
        })),
      p.day !== undefined || p.money !== undefined ? h('div', { className: 'mt-clock mt-plate' },
        p.day !== undefined ? h('span', null, 'Day ' + num(p.day) + (p.time ? ', ' + p.time : '')) : null,
        p.money !== undefined ? h('span', { className: 'mt-clock-money' }, 'c.' + num(p.money)) : null) : null);
  }

  function StatBar(p) {
    var max = p.max || 100, value = p.value || 0, stun = Math.max(0, p.stun || 0);
    var fill = clamp01(value / max);
    var stunW = Math.min(clamp01(stun / max), fill);
    var neg = value < 0 ? clamp01(-value / max) : 0;
    return h('div', { className: cx('mt-stat', 'mt-tone-' + (p.tone || 'life'), value < 0 && 'is-down', p.className) },
      h('div', { className: 'mt-stat-head' },
        h('span', null, p.label),
        h('span', { className: 'mt-stat-value' }, (value < 0 ? MINUS + num(-value) : num(value)) + ' / ' + num(max))),
      h('div', { className: 'mt-bar', role: 'meter', 'aria-label': p.label, 'aria-valuemin': -max, 'aria-valuemax': max, 'aria-valuenow': value },
        fill ? h('span', { className: 'mt-bar-fill', style: { width: 'calc(' + fill * 100 + '% - 2px)' } }) : null,
        stunW ? h('span', { className: 'mt-bar-stun', style: { left: 'calc(' + (fill - stunW) * 100 + '% + 1px)', width: 'calc(' + stunW * 100 + '% - 2px)' } }) : null,
        neg ? h('span', { className: 'mt-bar-neg', style: { width: 'calc(' + neg * 100 + '% - 2px)' } }) : null));
  }

  function SkillRow(p) {
    var prog = clamp01(p.progress || 0);
    return h('div', { className: cx('mt-skill', p.className) },
      h('span', { className: 'mt-skill-name' }, p.name),
      h('span', { className: 'mt-skill-level' },
        p.bonus ? h('span', { className: cx('mt-skill-bonus', p.bonus < 0 && 'is-neg') }, '(' + signed(p.bonus) + ')') : null,
        h('b', null, p.level)),
      h('div', { className: 'mt-xp', role: 'progressbar', 'aria-label': p.name + ' experience', 'aria-valuemin': 0, 'aria-valuemax': 100, 'aria-valuenow': Math.round(prog * 100) },
        h('div', { style: { width: prog * 100 + '%' } })));
  }

  function SquadCard(p) {
    var hp = clamp01(p.health === undefined ? 1 : p.health);
    return h('button', { type: 'button', className: cx('mt-squad', p.selected && 'is-selected', p.className), 'aria-pressed': !!p.selected, onClick: p.onClick },
      h('span', { className: 'mt-portrait', 'aria-hidden': true }, (p.name || '?').charAt(0)),
      h('span', { className: 'mt-squad-name' }, p.name),
      p.status ? h('span', { className: 'mt-squad-status' }, p.status) : null,
      h('span', { className: 'mt-bar mt-bar-thin', role: 'meter', 'aria-label': p.name + ' health', 'aria-valuemin': 0, 'aria-valuemax': 100, 'aria-valuenow': Math.round(hp * 100) },
        h('span', { className: 'mt-bar-fill', style: { width: 'calc(' + hp * 100 + '% - 2px)' } })));
  }

  function InventoryGrid(p) {
    var cols = p.cols || 8, rows = p.rows || 6;
    var style = { '--cols': cols, '--rows': rows };
    if (p.cell) style['--cell'] = p.cell + 'px';
    return h('div', { className: cx('mt-inv', p.className), role: 'list', 'aria-label': p.label || 'Inventory', style: style },
      (p.items || []).map(function (it, i) {
        var w = it.w || 1, ht = it.h || 1;
        return h('div', {
          key: it.id || i, role: 'listitem', tabIndex: 0, title: it.name + (it.count > 1 ? ' ×' + it.count : ''),
          className: cx('mt-item', ht >= w * 2 && 'is-tall', it.selected && 'is-selected'),
          style: { gridColumn: it.x + 1 + ' / span ' + w, gridRow: it.y + 1 + ' / span ' + ht },
          onClick: p.onSelect ? function () { p.onSelect(it, i); } : undefined
        },
          h('span', { className: 'mt-item-name' }, it.short || it.name),
          it.count > 1 ? h('span', { className: 'mt-item-count' }, num(it.count)) : null);
      }));
  }

  function ItemTooltip(p) {
    var g = p.grade === 'artifact' ? 'art' : p.grade !== undefined && p.grade !== null ? 'g' + p.grade : null;
    return h('div', { className: cx('mt-tip mt-plate', p.className), role: 'tooltip' },
      h('div', { className: cx('mt-tip-name', g) }, p.name),
      p.gradeName || p.kind ? h('div', { className: 'mt-tip-sub' }, [p.gradeName, p.kind].filter(Boolean).join(' · ')) : null,
      p.stats && p.stats.length ? h('dl', { className: 'mt-tip-stats' }, p.stats.map(function (s, i) {
        return h(React.Fragment, { key: i }, h('dt', null, s[0]), h('dd', { className: s[2] ? 'is-' + s[2] : undefined }, s[1]));
      })) : null,
      p.description ? h('p', { className: 'mt-tip-desc' }, p.description) : null,
      p.weight !== undefined || p.value !== undefined ? h('div', { className: 'mt-tip-foot' },
        p.weight !== undefined ? h('span', null, 'Weight ' + p.weight + ' kg') : h('span'),
        p.value !== undefined ? h('span', null, 'c.' + num(p.value)) : null) : null);
  }

  function Dialogue(p) {
    return h('div', { className: cx('mt-dialogue mt-plate', p.className) },
      h('div', { className: 'mt-dialogue-who' },
        h('span', { className: 'mt-dialogue-speaker' }, p.speaker),
        p.faction ? h('span', { className: 'mt-dialogue-faction' }, p.faction) : null),
      h('p', { className: 'mt-dialogue-line' }, p.text),
      p.choices && p.choices.length ? h('ol', { className: 'mt-dialogue-choices' }, p.choices.map(function (c, i) {
        return h('li', { key: i }, h('button', { type: 'button', onClick: p.onChoose ? function () { p.onChoose(i, c); } : undefined },
          h('span', { className: 'mt-dialogue-n' }, i + 1 + '.'), ' ', c));
      })) : null);
  }

  function SaveCard(p) {
    return h('button', { type: 'button', className: cx('mt-save', p.className), onClick: p.onClick },
      h('span', { className: 'mt-portrait', 'aria-hidden': true }, (p.initial || p.squad || '?').charAt(0)),
      h('span', { className: 'mt-save-label' }, p.label || 'Continue'),
      p.hotkey !== null ? h('span', { className: 'mt-key' }, p.hotkey || 'Enter') : h('span'),
      p.line ? h('span', { className: 'mt-save-line' }, p.line) : null,
      p.meta ? h('span', { className: 'mt-save-meta' }, p.meta) : null);
  }

  var MENU = [
    { id: 'new', label: 'New Game', icon: 'new' },
    { id: 'load', label: 'Load Game', icon: 'load' },
    { id: 'options', label: 'Options', icon: 'options' },
    { id: 'credits', label: 'Credits', icon: 'credits' },
    { id: 'exit', label: 'Exit', icon: 'exit', danger: true }
  ];
  function MainMenu(p) {
    var items = p.items || MENU;
    return h('nav', { className: cx('mt-menu', p.className), 'aria-label': p.label || 'Main menu', style: p.style },
      p.save ? h(SaveCard, Object.assign({}, p.save, { onClick: function () { if (p.onSelect) p.onSelect('continue'); } })) : null,
      items.map(function (it) {
        return h(Button, { key: it.id, icon: it.icon, block: true, variant: it.danger ? 'danger' : undefined, onClick: function () { if (p.onSelect) p.onSelect(it.id); } }, it.label);
      }));
  }

  var api = { Window: Window, Card: Card, Message: Message, Button: Button, Tabs: Tabs, Checkbox: Checkbox, TimeControls: TimeControls, StatBar: StatBar, SkillRow: SkillRow, SquadCard: SquadCard, InventoryGrid: InventoryGrid, ItemTooltip: ItemTooltip, Dialogue: Dialogue, SaveCard: SaveCard, MainMenu: MainMenu };
  window.MeitouUI = Object.assign(window.MeitouUI || {}, api);
})();
