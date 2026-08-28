const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function loadLiveStatus(responses, options = {}) {
    const timers = [];
    const loads = [];
    const requestedUrls = [];
    const live = {
        pages: liveElement('0'),
        links: liveElement('0'),
        broken: liveElement('0')
    };
    const attributes = {
        'data-live-run-status-url': '/Crawl/Runs/1/Status',
        'data-live-run-final-url': '/Crawl/Run?id=1',
        'data-live-run-results-url': '/Crawl/Runs/1/Results',
        'data-live-run-results-selector': '#crawl-broken-links-results',
        'data-live-run-version': 'Running:0:0',
        'data-live-run-lifetime': '1000000'
    };
    const results = {
        contains(element) {
            return options.focusInside === true && element === document.activeElement;
        }
    };
    const host = {
        nodeType: 1,
        id: 'ajax-page',
        matches(selector) {
            return selector === '[data-live-run-status-url]';
        },
        querySelector(selector) {
            if (selector.includes('[data-live="broken"]')) return live.broken;
            return null;
        },
        querySelectorAll(selector) {
            return selector === '[data-live]' ? Object.values(live) : [];
        },
        getAttribute(name) {
            return attributes[name] || null;
        },
        setAttribute(name, value) {
            attributes[name] = value;
        }
    };
    const documentHandlers = {};
    global.navigator = { onLine: true };
    global.document = {
        hidden: false,
        readyState: 'complete',
        activeElement: options.activeElement || null,
        addEventListener(name, handler) {
            documentHandlers[name] = handler;
        },
        querySelector(selector) {
            if (selector === '[data-live-run-status-url]' || selector === '#ajax-page') return host;
            return selector === '#crawl-broken-links-results' ? results : null;
        }
    };
    global.window = {
        location: { href: 'https://localhost/Crawl/Run?id=1' },
        WebHealth: {
            ajax: {
                async load(url, selector) {
                    loads.push({ url, selector });
                    return {};
                },
                messageForStatus(status) {
                    return `status ${status}`;
                },
                navigateToLogin() {},
                renderMessage() {}
            }
        },
        addEventListener() {},
        setTimeout(handler, delay) {
            const timer = { handler, delay, cleared: false };
            timers.push(timer);
            return timer;
        },
        clearTimeout(timer) {
            timer.cleared = true;
        }
    };
    global.fetch = async url => {
        requestedUrls.push(url);
        return responses.shift();
    };
    ['poller.js', 'live-run-status.js'].forEach(fileName => {
        const script = fs.readFileSync(
            path.join(__dirname, '../../src/WebHealth.Web/wwwroot/js', fileName),
            'utf8');
        vm.runInThisContext(script);
    });
    return { attributes, documentHandlers, host, live, loads, requestedUrls, results, timers };
}

function liveElement(value) {
    return {
        textContent: value,
        getAttribute(name) {
            if (name !== 'data-live') return null;
            return Object.entries(this.owner || {}).find(([, element]) => element === this)?.[0] || null;
        }
    };
}

function bindLiveOwners(runtime) {
    Object.values(runtime.live).forEach(element => {
        element.owner = runtime.live;
    });
}

async function runNext(runtime) {
    const timer = runtime.timers.find(item => !item.cleared);
    assert.ok(timer);
    timer.cleared = true;
    await timer.handler();
    return timer;
}

function jsonResponse(payload) {
    return {
        status: 200,
        ok: true,
        async json() {
            return payload;
        }
    };
}

function noContentResponse() {
    return { status: 204, ok: true };
}

test('live status patches counters and resets the polling backoff', async () => {
    const runtime = loadLiveStatus([
        jsonResponse({ active: true, version: 'Running:2:4', pages: 2, links: 4, broken: 1 })
    ]);
    bindLiveOwners(runtime);

    await runNext(runtime);

    assert.equal(runtime.live.pages.textContent, 2);
    assert.equal(runtime.live.links.textContent, 4);
    assert.equal(runtime.live.broken.textContent, 1);
    assert.equal(runtime.attributes['data-live-run-version'], 'Running:2:4');
    assert.match(runtime.requestedUrls[0], /version=Running%3A0%3A0/);
    assert.equal(runtime.timers.find(item => !item.cleared).delay, 1000);
    assert.deepEqual(runtime.loads, []);
});

test('a dirty results region refreshes on a later unchanged poll', async () => {
    const originalNow = Date.now;
    let now = 0;
    Date.now = () => now;
    try {
        const runtime = loadLiveStatus([
            jsonResponse({ active: true, version: 'Running:2:4', pages: 2, links: 4, broken: 1 }),
            noContentResponse()
        ]);
        bindLiveOwners(runtime);

        await runNext(runtime);
        now = 10000;
        await runNext(runtime);

        assert.deepEqual(runtime.loads, [{
            url: '/Crawl/Runs/1/Results',
            selector: '#crawl-broken-links-results'
        }]);
    } finally {
        Date.now = originalNow;
    }
});

test('focus inside dirty results postpones replacement without losing it', async () => {
    const originalNow = Date.now;
    let now = 0;
    Date.now = () => now;
    try {
        const focus = {};
        const runtime = loadLiveStatus([
            jsonResponse({ active: true, version: 'Running:2:4', pages: 2, links: 4, broken: 1 }),
            noContentResponse(),
            noContentResponse()
        ], { activeElement: focus, focusInside: true });
        bindLiveOwners(runtime);

        await runNext(runtime);
        now = 10000;
        await runNext(runtime);
        assert.deepEqual(runtime.loads, []);

        runtime.results.contains = () => false;
        now = 20000;
        await runNext(runtime);
        assert.equal(runtime.loads.length, 1);
    } finally {
        Date.now = originalNow;
    }
});

test('completion discards dirty results and performs one final full refresh', async () => {
    const runtime = loadLiveStatus([
        jsonResponse({ active: false, version: 'Completed:2:4', pages: 2, links: 4, broken: 1 })
    ]);
    bindLiveOwners(runtime);

    await runNext(runtime);

    assert.deepEqual(runtime.loads, [{
        url: '/Crawl/Run?id=1',
        selector: '#ajax-page'
    }]);
    assert.equal(runtime.timers.find(item => !item.cleared), undefined);
});
