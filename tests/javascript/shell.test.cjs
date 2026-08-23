const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function createAttributes(values = {}) {
    const attributes = new Map(Object.entries(values));
    return {
        getAttribute(name) {
            return attributes.get(name) ?? null;
        },
        removeAttribute(name) {
            attributes.delete(name);
        },
        setAttribute(name, value) {
            attributes.set(name, String(value));
        }
    };
}

function createBadge(detail) {
    const attributes = createAttributes({
        'data-badge-detail': detail,
        title: detail
    });
    const listeners = {};
    return {
        ...attributes,
        listeners,
        addEventListener(name, handler) {
            listeners[name] = handler;
        },
        blur() {},
        getBoundingClientRect() {
            return { bottom: 125, height: 25, left: 100, top: 100, width: 80 };
        }
    };
}

function loadShell(badges) {
    const documentHandlers = {};
    const positionChanges = [];
    const documentElement = {
        ...createAttributes(),
        classList: { add() {} }
    };
    let tooltip;
    global.document = {
        body: {
            appendChild(element) {
                tooltip = element;
            }
        },
        documentElement,
        readyState: 'complete',
        addEventListener(name, handler) {
            documentHandlers[name] = handler;
        },
        createElement() {
            let hidden = false;
            const style = { visibility: '' };
            Object.defineProperties(style, {
                left: {
                    set(value) {
                        positionChanges.push({ hidden, value, visibility: style.visibility });
                    }
                },
                top: {
                    set(value) {
                        positionChanges.push({ hidden, value, visibility: style.visibility });
                    }
                }
            });
            return {
                get hidden() {
                    return hidden;
                },
                set hidden(value) {
                    hidden = value;
                },
                style,
                textContent: '',
                setAttribute() {},
                getBoundingClientRect() {
                    return { height: 40, width: 160 };
                }
            };
        },
        querySelector() {
            return null;
        },
        querySelectorAll(selector) {
            return selector === '[data-badge-detail]' ? badges : [];
        }
    };
    global.window = {
        WebHealth: {},
        innerWidth: 1200,
        localStorage: {
            getItem() {
                return null;
            },
            setItem() {}
        },
        addEventListener() {},
    };
    const script = fs.readFileSync(
        path.join(__dirname, '../../src/WebHealth.Web/wwwroot/js/shell.js'),
        'utf8');
    vm.runInThisContext(script);
    return { documentHandlers, positionChanges, tooltip };
}

test('hovering a badge shows its popup immediately', () => {
    const badge = createBadge('Hovered detail');
    const runtime = loadShell([badge]);

    badge.listeners.mouseenter();

    assert.equal(runtime.tooltip.hidden, false);
    assert.equal(runtime.tooltip.textContent, 'Hovered detail');
    assert.ok(runtime.positionChanges.every(change => change.visibility === 'hidden'));
});

test('moving to another badge closes the previous popup before the handoff', () => {
    const firstBadge = createBadge('First detail');
    const secondBadge = createBadge('Second detail');
    const runtime = loadShell([firstBadge, secondBadge]);

    firstBadge.listeners.focus();
    secondBadge.listeners.mouseenter();

    assert.equal(runtime.tooltip.hidden, false);
    assert.equal(runtime.tooltip.textContent, 'Second detail');
});

test('a sibling exit cannot close the current badge popup', () => {
    const firstBadge = createBadge('First detail');
    const secondBadge = createBadge('Second detail');
    const runtime = loadShell([firstBadge, secondBadge]);

    firstBadge.listeners.focus();
    secondBadge.listeners.focus();
    firstBadge.listeners.blur();

    assert.equal(runtime.tooltip.hidden, false);
    assert.equal(runtime.tooltip.textContent, 'Second detail');
});

test('keyboard focus shows a badge popup without a hover delay', () => {
    const badge = createBadge('Keyboard detail');
    const runtime = loadShell([badge]);

    badge.listeners.focus();

    assert.equal(runtime.tooltip.hidden, false);
    assert.equal(runtime.tooltip.textContent, 'Keyboard detail');
});

test('replacing a table region closes its active badge popup', () => {
    const badge = createBadge('Removed detail');
    const runtime = loadShell([badge]);

    badge.listeners.mouseenter();
    runtime.documentHandlers['webhealth:before-fragment-replace']({
        detail: {
            root: {
                contains(element) {
                    return element === badge;
                },
                matches() {
                    return false;
                },
                nodeType: 1,
                querySelectorAll() {
                    return [];
                }
            }
        }
    });

    assert.equal(runtime.tooltip.hidden, true);
    assert.equal(runtime.tooltip.textContent, 'Removed detail');
});
