const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function loadAjax() {
    global.File = class File {};
    global.navigator = { onLine: true };
    global.window = {
        WebHealth: {},
        location: {
            href: 'https://localhost/Targets/Endpoints?search=old',
            origin: 'https://localhost',
            pathname: '/Targets/Endpoints',
            search: '?search=old'
        },
        history: {},
        addEventListener() {}
    };
    global.document = {
        readyState: 'loading',
        addEventListener() {},
        querySelector() { return null; }
    };
    global.CustomEvent = class CustomEvent {};
    var script = fs.readFileSync(
        path.join(__dirname, '../../src/WebHealth.Web/wwwroot/js/ajax.js'),
        'utf8');
    vm.runInThisContext(script);
    return window.WebHealth.ajax;
}

const ajax = loadAjax();

test('buildUrl replaces the old query and preserves repeated fields', () => {
    const url = ajax.buildUrl('/Targets/Endpoints?search=old', [
        ['search', 'api'],
        ['tag', 'one'],
        ['tag', 'two']
    ]);

    assert.equal(url, 'https://localhost/Targets/Endpoints?search=api&tag=one&tag=two');
});

test('isLocalUrl accepts local paths and rejects external destinations', () => {
    assert.equal(ajax.isLocalUrl('/Incidents'), true);
    assert.equal(ajax.isLocalUrl('https://localhost/PageAudits'), true);
    assert.equal(ajax.isLocalUrl('https://example.com/'), false);
    assert.equal(ajax.isLocalUrl('http://[invalid'), false);
});

test('status messages cover the response contract', () => {
    assert.match(ajax.messageForStatus(401), /session/i);
    assert.match(ajax.messageForStatus(403), /permission/i);
    assert.match(ajax.messageForStatus(404), /no longer/i);
    assert.match(ajax.messageForStatus(409), /changed/i);
    assert.match(ajax.messageForStatus(422), /highlighted/i);
    assert.match(ajax.messageForStatus(500), /could not be completed/i);
});
