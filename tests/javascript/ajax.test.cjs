const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function loadAjax(options = {}) {
    global.File = class File {};
    global.FormData = options.FormData || global.FormData;
    global.fetch = options.fetch || global.fetch;
    global.navigator = { onLine: true };
    global.window = {
        WebHealth: {},
        location: {
            href: 'https://localhost/Targets/Endpoints?search=old',
            origin: 'https://localhost',
            pathname: '/Targets/Endpoints',
            search: '?search=old',
            reload() {}
        },
        history: options.history || {},
        addEventListener() {}
    };
    global.document = Object.assign({
        readyState: options.readyState || 'loading',
        addEventListener() {},
        dispatchEvent() {},
        querySelector() { return null; }
    }, options.document || {});
    global.CustomEvent = class CustomEvent {};
    global.DOMParser = options.DOMParser || global.DOMParser;
    var script = fs.readFileSync(
        path.join(__dirname, '../../src/WebHealth.Web/wwwroot/js/ajax.js'),
        'utf8');
    vm.runInThisContext(script);
    return window.WebHealth.ajax;
}

test('buildUrl replaces the old query and preserves repeated fields', () => {
    const ajax = loadAjax();
    const url = ajax.buildUrl('/Targets/Endpoints?search=old', [
        ['search', 'api'],
        ['tag', 'one'],
        ['tag', 'two']
    ]);

    assert.equal(url, 'https://localhost/Targets/Endpoints?search=api&tag=one&tag=two');
});

test('isLocalUrl accepts local paths and rejects external destinations', () => {
    const ajax = loadAjax();
    assert.equal(ajax.isLocalUrl('/Incidents'), true);
    assert.equal(ajax.isLocalUrl('https://localhost/PageAudits'), true);
    assert.equal(ajax.isLocalUrl('https://example.com/'), false);
    assert.equal(ajax.isLocalUrl('http://[invalid'), false);
});

test('status messages cover the response contract', () => {
    const ajax = loadAjax();
    assert.match(ajax.messageForStatus(401), /session/i);
    assert.match(ajax.messageForStatus(403), /permission/i);
    assert.match(ajax.messageForStatus(404), /no longer/i);
    assert.match(ajax.messageForStatus(409), /changed/i);
    assert.match(ajax.messageForStatus(422), /highlighted/i);
    assert.match(ajax.messageForStatus(500), /could not be completed/i);
});

test('starting a newer request cancels the older request for the same target', async () => {
    const target = { setAttribute() {} };
    let firstSignal;
    let requestCount = 0;
    const ajax = loadAjax({
        document: {
            querySelector(selector) {
                return selector === '#target' ? target : null;
            }
        },
        fetch(url, init) {
            requestCount += 1;
            if (requestCount === 1) {
                firstSignal = init.signal;
                return new Promise((resolve, reject) => {
                    init.signal.addEventListener('abort', () => {
                        const error = new Error('Aborted');
                        error.name = 'AbortError';
                        reject(error);
                    });
                });
            }
            return Promise.resolve(textResponse(404, ''));
        }
    });

    const first = ajax.load('/first', '#target');
    const second = ajax.load('/second', '#target');
    await Promise.all([first, second]);

    assert.equal(firstSignal.aborted, true);
    assert.equal(requestCount, 2);
});

test('an older response cannot replace content after slow body parsing', async () => {
    let releaseFirstBody;
    let replaced = false;
    let requestCount = 0;
    const current = {
        setAttribute() {},
        replaceWith() {
            replaced = true;
        }
    };
    const ajax = loadAjax({
        document: {
            querySelector(selector) {
                return selector === '#target' ? current : null;
            }
        },
        DOMParser: class DOMParser {
            parseFromString() {
                return {
                    querySelector: () => ({ querySelector: () => null })
                };
            }
        },
        fetch() {
            requestCount += 1;
            if (requestCount === 1) {
                return Promise.resolve({
                    status: 200,
                    ok: true,
                    headers: { get: () => 'text/html' },
                    text: () => new Promise(resolve => {
                        releaseFirstBody = resolve;
                    })
                });
            }
            return Promise.resolve(textResponse(404, ''));
        }
    });

    const first = ajax.load('/slow', '#target');
    await new Promise(resolve => setImmediate(resolve));
    const second = ajax.load('/newer', '#target');
    await second;
    releaseFirstBody('<div id="target"></div>');
    await first;

    assert.equal(replaced, false);
});

test('a newer GET form submission aborts the older filter request', async () => {
    const handlers = {};
    const target = { setAttribute() {} };
    const form = formStub('#target', 'get', [['search', 'first']]);
    let firstSignal;
    let requestCount = 0;
    loadAjax({
        readyState: 'complete',
        FormData: FormDataStub,
        document: {
            addEventListener(name, handler) {
                handlers[name] = handler;
            },
            querySelector(selector) {
                return selector === '#target' ? target : null;
            }
        },
        history: { replaceState() {} },
        fetch(url, init) {
            requestCount += 1;
            if (requestCount === 1) {
                firstSignal = init.signal;
                return new Promise((resolve, reject) => {
                    init.signal.addEventListener('abort', () => {
                        const error = new Error('Aborted');
                        error.name = 'AbortError';
                        reject(error);
                    });
                });
            }
            return Promise.resolve(textResponse(404, ''));
        }
    });

    handlers.submit(submitEvent(form, null));
    form.entries = [['search', 'second']];
    handlers.submit(submitEvent(form, null));
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(requestCount, 2);
    assert.equal(firstSignal.aborted, true);
});

test('a submitted form is blocked until its current request finishes', async () => {
    const handlers = {};
    const target = { setAttribute() {} };
    let finishRequest;
    let requestCount = 0;
    const form = formStub('#target');
    const submitter = { disabled: false, dataset: {} };
    loadAjax({
        readyState: 'complete',
        FormData: FormDataStub,
        document: {
            addEventListener(name, handler) {
                handlers[name] = handler;
            },
            querySelector(selector) {
                return selector === '#target' ? target : null;
            }
        },
        fetch() {
            requestCount += 1;
            return new Promise(resolve => {
                finishRequest = resolve;
            });
        }
    });
    const event = submitEvent(form, submitter);

    handlers.submit(event);
    handlers.submit(event);

    assert.equal(requestCount, 1);
    assert.equal(submitter.disabled, true);
    finishRequest(textResponse(404, ''));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(submitter.disabled, false);
});

test('a successful GET form replaces its target, initializes it, and pushes history', async () => {
    const handlers = {};
    const pushed = [];
    const incoming = {
        querySelector() { return null; }
    };
    let replacedWith;
    const current = {
        setAttribute() {},
        replaceWith(value) {
            replacedWith = value;
        }
    };
    const form = formStub('#ajax-page', 'get', [['search', 'api']]);
    let initialized;
    const ajax = loadAjax({
        readyState: 'complete',
        FormData: FormDataStub,
        history: {
            replaceState() {},
            pushState(state, title, url) {
                pushed.push({ state, url });
            }
        },
        document: {
            addEventListener(name, handler) {
                handlers[name] = handler;
            },
            querySelector(selector) {
                return selector === '#ajax-page' ? current : null;
            }
        },
        DOMParser: class DOMParser {
            parseFromString() {
                return { querySelector: () => incoming };
            }
        },
        fetch() {
            return Promise.resolve(textResponse(200, '<div id="ajax-page"></div>'));
        }
    });
    window.WebHealth.init = root => {
        initialized = root;
    };

    handlers.submit(submitEvent(form, null));
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(replacedWith, incoming);
    assert.equal(initialized, incoming);
    assert.equal(pushed.length, 1);
    assert.equal(pushed[0].url, 'https://localhost/Registry?search=api');
    assert.equal(pushed[0].state.ajaxTarget, '#ajax-page');
    assert.equal(ajax.isLocalUrl(pushed[0].url), true);
});

test('a network failure offers one retry that repeats the request', async () => {
    const target = { setAttribute() {} };
    const region = {
        classList: { add() {} },
        replaceChildren(value) {
            this.child = value;
        }
    };
    const created = [];
    let requestCount = 0;
    const ajax = loadAjax({
        document: {
            querySelector(selector) {
                if (selector === '#target') {
                    return target;
                }
                return selector === '[data-ajax-messages]' ? region : null;
            },
            createElement(tagName) {
                const element = elementStub(tagName);
                created.push(element);
                return element;
            }
        },
        fetch() {
            requestCount += 1;
            return requestCount === 1
                ? Promise.reject(new Error('Network unavailable'))
                : Promise.resolve(jsonResponse(200, {}));
        }
    });

    await ajax.load('/retryable', '#target');
    const retryButton = created.find(element => element.textContent === 'Retry');
    assert.ok(retryButton);
    retryButton.listeners.click();
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(requestCount, 2);
    assert.equal(retryButton.disabled, true);
    assert.equal(region.child, undefined);
});

test('a failed POST offers a page reload without repeating an ambiguous mutation', async () => {
    const handlers = {};
    const target = { setAttribute() {} };
    const region = {
        classList: { add() {} },
        replaceChildren(value) {
            this.child = value;
        }
    };
    const created = [];
    let requestCount = 0;
    let reloaded = false;
    const form = formStub('#target');
    loadAjax({
        readyState: 'complete',
        FormData: FormDataStub,
        document: {
            addEventListener(name, handler) {
                handlers[name] = handler;
            },
            querySelector(selector) {
                if (selector === '#target') {
                    return target;
                }
                return selector === '[data-ajax-messages]' ? region : null;
            },
            createElement(tagName) {
                const element = elementStub(tagName);
                created.push(element);
                return element;
            }
        },
        fetch() {
            requestCount += 1;
            return Promise.reject(new Error('Response lost'));
        }
    });
    window.location.reload = () => {
        reloaded = true;
    };

    handlers.submit(submitEvent(form, null));
    await new Promise(resolve => setImmediate(resolve));
    const reloadButton = created.find(element => element.textContent === 'Reload page');
    assert.ok(reloadButton);
    reloadButton.listeners.click();

    assert.equal(reloaded, true);
    assert.equal(requestCount, 1);
});

test('a successful queued action closes its containing action menu', async () => {
    const handlers = {};
    const target = { setAttribute() {} };
    let closed = false;
    const menu = {
        dispatchEvent(event) {
            closed = event instanceof CustomEvent;
        }
    };
    const form = formStub('#target');
    form.closest = selector => selector === '[data-shell-menu]' ? menu : form;
    loadAjax({
        readyState: 'complete',
        FormData: FormDataStub,
        document: {
            addEventListener(name, handler) {
                handlers[name] = handler;
            },
            querySelector(selector) {
                return selector === '#target' ? target : null;
            }
        },
        fetch() {
            return Promise.resolve(jsonResponse(202, {
                message: 'Check queued.',
                statusUrl: '/Checks/Status?id=1'
            }));
        }
    });

    handlers.submit(submitEvent(form, null));
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(closed, true);
});

function textResponse(status, body) {
    return {
        status,
        ok: status >= 200 && status < 300,
        headers: { get: () => 'text/html' },
        text: async () => body
    };
}

function jsonResponse(status, body) {
    return {
        status,
        ok: status >= 200 && status < 300,
        headers: { get: () => 'application/json' },
        json: async () => body
    };
}

class FormDataStub {
    constructor(form) {
        this.values = form.entries;
    }

    entries() {
        return this.values[Symbol.iterator]();
    }
}

function formStub(target, method = 'post', entries = []) {
    const attributes = {
        'data-ajax-target': target,
        'data-ajax-history': method === 'get' ? 'push' : null
    };
    const form = {
        action: 'https://localhost/Registry',
        method,
        entries,
        toggleAttribute() {},
        getAttribute(name) {
            return attributes[name];
        }
    };
    form.closest = () => form;
    return form;
}

function submitEvent(form, submitter) {
    return {
        target: form,
        submitter,
        defaultPrevented: false,
        preventDefault() {
            this.defaultPrevented = true;
        }
    };
}

function elementStub(tagName) {
    return {
        tagName,
        children: [],
        listeners: {},
        setAttribute() {},
        append(...values) {
            this.children.push(...values);
        },
        addEventListener(name, handler) {
            this.listeners[name] = handler;
        }
    };
}
