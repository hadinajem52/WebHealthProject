(function () {
    'use strict';

    var activeTimer = null;
    var startedAt = 0;
    var attempt = 0;
    var pending = false;
    var statusUrl = null;
    var intervals = [800, 1200, 2000, 3500, 5000, 8000, 12000];
    var maximumLifetime = 300000;

    function stop() {
        if (activeTimer) {
            window.clearTimeout(activeTimer);
        }
        activeTimer = null;
        statusUrl = null;
    }

    function schedule() {
        if (!statusUrl || Date.now() - startedAt >= maximumLifetime) {
            stop();
            return;
        }
        var delay = intervals[Math.min(attempt, intervals.length - 1)];
        activeTimer = window.setTimeout(poll, delay);
        attempt += 1;
    }

    async function poll() {
        if (document.hidden || navigator.onLine === false) {
            schedule();
            return;
        }

        pending = true;
        var root = await window.WebHealth.ajax.load(statusUrl, '#ajax-page');
        pending = false;
        if (!root || root.getAttribute('data-page-audit-active') !== 'true') {
            stop();
            return;
        }
        statusUrl = root.getAttribute('data-page-audit-status-url') || statusUrl;
        schedule();
    }

    function start(url) {
        if (!url) {
            return;
        }
        stop();
        statusUrl = url;
        startedAt = Date.now();
        attempt = 0;
        schedule();
    }

    function startFromRoot(root) {
        var host = root.nodeType === 1 && root.matches('[data-page-audits]')
            ? root
            : root.querySelector('[data-page-audits]');
        if (host && host.getAttribute('data-page-audit-active') === 'true') {
            start(host.getAttribute('data-page-audit-status-url'));
        }
    }

    document.addEventListener('webhealth:ajax-start', function (event) {
        var source = event.detail.source;
        if (source && source.closest('[data-page-audits]')) {
            stop();
        }
    });
    document.addEventListener('webhealth:ajax-response', function (event) {
        var source = event.detail.source;
        var payload = event.detail.payload;
        if (source && source.closest('[data-page-audits]') && payload && payload.statusUrl) {
            start(payload.statusUrl);
        }
    });
    document.addEventListener('webhealth:fragment-ready', function (event) {
        if (!pending) {
            startFromRoot(event.detail.root);
        }
    });
    window.addEventListener('online', function () {
        if (statusUrl && !activeTimer && !pending) {
            schedule();
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
