// Run with node --test tests/theme-preferences.test.cjs. No browser or database is required.
const _assert = require('node:assert/strict');
const _test = require('node:test');
const _fs = require('node:fs');
const _path = require('node:path');
const _vm = require('node:vm');
const _webRoot = _path.join(__dirname, '../src/MetaGrow.Web');
const _source = _fs.readFileSync(_path.join(_webRoot, 'wwwroot/scripts/theme-preferences.js'), 'utf8');

function loadTheme(cookie, hasBody = true) {
    const attributes = {};
    const classes = new Set(['existing-class']);
    const body = { classList: { toggle(name, enabled) { enabled ? classes.add(name) : classes.delete(name); } } };
    const document = { cookie, body: hasBody ? body : null, documentElement: {
        setAttribute(name, value) { attributes[name] = value; }
    } };
    const context = { window: {}, document };
    _vm.runInNewContext(_source, context);
    return { scheme: context.window.mantineScheme, document, attributes, classes, body };
}

_test('saved dark mode applies before body parsing and again when body is available', () => {
    const state = loadTheme('unrelated=value; mantine-color-scheme=dark', false);
    _assert.equal(state.attributes['data-bs-theme'], 'dark');
    state.document.body = state.body;
    state.scheme.restore();
    _assert.ok(state.classes.has('mantine-dark'));
    _assert.ok(state.classes.has('existing-class'));
});

_test('switching persists the choice, synchronizes page state and survives body replacement', () => {
    const state = loadTheme('mantine-color-scheme=light');
    state.scheme.set('dark');
    _assert.match(state.document.cookie, /mantine-color-scheme=dark; path=\/; max-age=31536000; SameSite=Lax/);
    state.classes.delete('mantine-dark'); // Enhanced navigation replaced the server-rendered class.
    state.scheme.restore();
    _assert.ok(state.classes.has('mantine-dark'));
    state.scheme.set('light');
    _assert.equal(state.attributes['data-bs-theme'], 'light');
    _assert.ok(!state.classes.has('mantine-dark'));
    _assert.ok(state.classes.has('existing-class'));
});

_test('absent, malformed and similarly named cookies safely default to light', () => {
    for (const cookie of ['', 'other-mantine-color-scheme=dark', 'mantine-color-scheme=invalid', 'mantine-color-scheme=%64ark']) {
        _assert.equal(loadTheme(cookie).attributes['data-bs-theme'], 'light');
    }
});

function luminance(hex) {
    const channels = hex.replace('#', '').match(/../g).map(value => parseInt(value, 16) / 255)
        .map(value => value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4);
    return channels[0] * 0.2126 + channels[1] * 0.7152 + channels[2] * 0.0722;
}

_test('semantic text and badge colours meet normal-text contrast in both themes', () => {
    const css = _fs.readFileSync(_path.join(_webRoot, 'wwwroot/css/application-theme.css'), 'utf8');
    for (const mode of [':root', ':root[data-bs-theme="dark"]']) {
        const block = css.slice(css.indexOf(mode + ' {')).split('}')[0];
        const tokens = Object.fromEntries([...block.matchAll(/--mg-([\w-]+):\s*(#[\da-f]{6})/g)].map(match => [match[1], match[2]]));
        for (const [foreground, background] of [['text', 'surface'], ['muted', 'surface'], ['link', 'surface'],
            ...['info', 'success', 'warning', 'danger', 'purple'].map(kind => [kind + '-text', kind + '-surface'])]) {
            const a = luminance(tokens[foreground]);
            const b = luminance(tokens[background]);
            const contrast = (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
            _assert.ok(contrast >= 4.5, `${mode} ${foreground}/${background}: ${contrast.toFixed(2)}`);
        }
    }
});

_test('page styles do not reintroduce the unresolved DevExpress tokens that caused light fallbacks', () => {
    for (const file of _fs.readdirSync(_path.join(_webRoot, 'Components'), { recursive: true }).filter(file => file.endsWith('.css'))) {
        const css = _fs.readFileSync(_path.join(_webRoot, 'Components', file), 'utf8');
        _assert.doesNotMatch(css, /--dxds-color-(?:surface-neutral-rest|surface-neutral-hovered-rest|content-neutral-rest|content-brand-default-rest|border-danger-rest)\b/, file);
    }
});
