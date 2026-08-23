const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function loadPollingScript(fileName, options = {}) {
    const documentHandlers = {};
    const windowHandlers = {};
    const timers = [];
    Object.defineProperty(global, 'navigator', {
        value: { onLine: options.online !== false },
        configurable: true,
        writable: true
    });
    global.document = {
        hidden: options.hidden === true,
        readyState: 'complete',
        addEventListener(name, handler) {
            documentHandlers[name] = handler;
        },
        querySelector(selector) {
            const host = options.host;
            return host && host.matches(selector) ? host : null;
        }
    };
    global.window = {
        WebHealth: { ajax: options.ajax },
        addEventListener(name, handler) {
            windowHandlers[name] = handler;
        },
        setTimeout(handler, delay) {
            const timer = { handler, delay, cleared: false };
            timers.push(timer);
            return timer;
        },
        clearTimeout(timer) {
            timer.cleared = true;
        }
    };
    global.fetch = options.fetch;
    ['poller.js', fileName].forEach(name => {
        const script = fs.readFileSync(
            path.join(__dirname, '../../src/WebHealth.Web/wwwroot/js', name),
            'utf8');
        vm.runInThisContext(script);
    });
    return { documentHandlers, windowHandlers, timers };
}

function runStatusHost(active = true, statusUrl = '/PageAudits/Status?id=1') {
    return {
        nodeType: 1,
        id: 'page-audit-results',
        matches(selector) {
            return selector === '[data-run-status]' || selector === '#page-audit-results';
        },
        querySelector() {
            return null;
        },
        getAttribute(name) {
            if (name === 'data-run-active') {
                return active ? 'true' : 'false';
            }
            return name === 'data-run-url' ? statusUrl : null;
        }
    };
}

function checkHost(statusUrl = '/Checks/Status?id=1') {
    return {
        nodeType: 1,
        matches(selector) {
            return selector === '[data-endpoint-details]';
        },
        querySelector() {
            return null;
        },
        getAttribute(name) {
            return name === 'data-check-status-url' ? statusUrl : null;
        }
    };
}

function nextTimer(timers) {
    return timers.find(timer => !timer.cleared);
}

test('run polling replaces only its declared region', async () => {
    const calls = [];
    const messages = [];
    let host = runStatusHost();
    const runtime = loadPollingScript('run-status.js', {
        get host() {
            return host;
        },
        ajax: {
            async load(url, selector) {
                calls.push({ url, selector });
                host = runStatusHost(false, null);
                return host;
            },
            renderMessage(message, level) {
                messages.push({ message, level });
            }
        }
    });

    await nextTimer(runtime.timers).handler();

    assert.deepEqual(calls, [{
        url: '/PageAudits/Status?id=1',
        selector: '#page-audit-results'
    }]);
    assert.deepEqual(messages, []);
});

test('run polling pauses hidden time and resumes within the server retry window', () => {
    const originalNow = Date.now;
    let now = 1000;
    Date.now = () => now;
    try {
        const messages = [];
        const runtime = loadPollingScript('run-status.js', {
            host: runStatusHost(),
            ajax: {
                async load() {
                    return runStatusHost();
                },
                renderMessage(message, level) {
                    messages.push({ message, level });
                }
            }
        });

        document.hidden = true;
        runtime.documentHandlers.visibilitychange();
        now += 1000000;
        document.hidden = false;
        runtime.documentHandlers.visibilitychange();

        assert.ok(nextTimer(runtime.timers));
        assert.deepEqual(messages, []);
    } finally {
        Date.now = originalNow;
    }
});

test('run polling remains active through the bounded server retry lifecycle', async () => {
    const originalNow = Date.now;
    let now = 0;
    Date.now = () => now;
    try {
        const messages = [];
        const runtime = loadPollingScript('run-status.js', {
            host: runStatusHost(),
            ajax: {
                async load() {
                    return runStatusHost();
                },
                renderMessage(message, level) {
                    messages.push({ message, level });
                }
            }
        });

        const first = nextTimer(runtime.timers);
        now = 700000;
        await first.handler();

        assert.ok(runtime.timers.find(timer => !timer.cleared && timer !== first));
        assert.deepEqual(messages, []);
    } finally {
        Date.now = originalNow;
    }
});

test('run polling stops when the run finishes, without announcing it', async () => {
    const messages = [];
    let host = runStatusHost();
    const finished = {
        nodeType: 1,
        id: 'page-audit-results',
        matches(selector) {
            return selector === '[data-run-status]' || selector === '#page-audit-results';
        },
        querySelector() {
            return null;
        },
        getAttribute(name) {
            return name === 'data-run-active' ? 'false' : null;
        }
    };
    const runtime = loadPollingScript('run-status.js', {
        get host() {
            return host;
        },
        ajax: {
            async load() {
                host = finished;
                return finished;
            },
            renderMessage(message, level) {
                messages.push({ message, level });
            }
        }
    });

    const first = nextTimer(runtime.timers);
    await first.handler();

    assert.deepEqual(messages, []);
    assert.equal(
        runtime.timers.find(timer => !timer.cleared && timer !== first),
        undefined);
});

test('manual-check polling reports server failures through the shared AJAX messages', async () => {
    const messages = [];
    const runtime = loadPollingScript('checks.js', {
        host: checkHost(),
        ajax: {
            load() {},
            messageForStatus(status) {
                return `status ${status}`;
            },
            messageForProblem(payload) {
                return `${payload.detail} Reference: ${payload.correlationId}`;
            },
            navigateToLogin() {},
            renderMessage(message, level) {
                messages.push({ message, level });
            }
        },
        fetch() {
            return Promise.resolve({
                status: 500,
                ok: false,
                json: async () => ({ detail: 'Server failed.', correlationId: 'abc' })
            });
        }
    });

    await nextTimer(runtime.timers).handler();

    assert.deepEqual(messages, [{
        message: 'Server failed. Reference: abc',
        level: 'error'
    }]);
});

test('manual-check polling navigates to login on an expired session', async () => {
    const messages = [];
    let navigated = false;
    const runtime = loadPollingScript('checks.js', {
        host: checkHost(),
        ajax: {
            load() {},
            messageForStatus() {
                return 'Your session has expired.';
            },
            messageForProblem() {},
            navigateToLogin() {
                navigated = true;
            },
            renderMessage(message, level) {
                messages.push({ message, level });
            }
        },
        fetch() {
            return Promise.resolve({ status: 401, ok: false });
        }
    });

    await nextTimer(runtime.timers).handler();

    assert.equal(navigated, true);
    assert.deepEqual(messages, [{
        message: 'Your session has expired.',
        level: 'error'
    }]);
});

test('manual-check polling resumes after reconnecting without spending offline time', () => {
    const runtime = loadPollingScript('checks.js', {
        host: checkHost(),
        online: false,
        ajax: {
            load() {},
            messageForStatus() {},
            messageForProblem() {},
            navigateToLogin() {},
            renderMessage() {}
        },
        fetch() {
            throw new Error('Fetch should not run while offline.');
        }
    });

    assert.equal(nextTimer(runtime.timers), undefined);
    navigator.onLine = true;
    runtime.windowHandlers.online();

    assert.ok(nextTimer(runtime.timers));
});

test('a manual-check request cancelled by a pause resumes instead of counting as finished', async () => {
    const runtime = loadPollingScript('checks.js', {
        host: checkHost(),
        ajax: {
            load() {},
            messageForStatus() {},
            messageForProblem() {},
            navigateToLogin() {},
            renderMessage() {}
        },
        fetch(url, init) {
            return new Promise((resolve, reject) => {
                init.signal.addEventListener('abort', () => {
                    const error = new Error('Aborted');
                    error.name = 'AbortError';
                    reject(error);
                });
            });
        }
    });

    const first = nextTimer(runtime.timers);
    const inFlight = first.handler();
    document.hidden = true;
    runtime.documentHandlers.visibilitychange();
    await inFlight;
    document.hidden = false;
    runtime.documentHandlers.visibilitychange();

    assert.ok(
        runtime.timers.find(timer => !timer.cleared && timer !== first),
        'hiding the tab mid-request must not end the poll');
});

test('run polling stops on a refusal rather than repeating it until the lifetime expires', async () => {
    const runtime = loadPollingScript('run-status.js', {
        host: runStatusHost(),
        ajax: {
            async load(url, selector, options) {
                options.onStatus(404);
                return null;
            },
            renderMessage() {}
        }
    });

    const first = nextTimer(runtime.timers);
    await first.handler();

    assert.equal(
        runtime.timers.find(timer => !timer.cleared && timer !== first),
        undefined,
        'a run that is gone answers the same way however often it is asked');
});

test('run polling retries a request that never reached the server', async () => {
    const runtime = loadPollingScript('run-status.js', {
        host: runStatusHost(),
        ajax: {
            async load() {
                return null;
            },
            renderMessage() {}
        }
    });

    const first = nextTimer(runtime.timers);
    await first.handler();

    assert.ok(runtime.timers.find(timer => !timer.cleared && timer !== first));
});
