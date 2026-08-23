(function () {
    'use strict';

    var activeTimer = null;
    var startedAt = 0;
    var attempt = 0;
    var pending = false;
    var statusUrl = null;
    var pausedAt = 0;
    var intervals = [800, 1200, 2000, 3500, 5000, 8000, 12000];
    var maximumLifetime = 900000;

    function clearTimer() {
        if (activeTimer) {
            window.clearTimeout(activeTimer);
        }
        activeTimer = null;
    }

    function stop() {
        clearTimer();
        statusUrl = null;
        pausedAt = 0;
    }

    function pause() {
        clearTimer();
        if (statusUrl && !pausedAt) {
            pausedAt = Date.now();
        }
    }

    function resume() {
        if (!statusUrl || activeTimer || pending || document.hidden || navigator.onLine === false) {
            return;
        }
        if (pausedAt) {
            startedAt += Date.now() - pausedAt;
            pausedAt = 0;
        }
        schedule();
    }

    function schedule() {
        if (!statusUrl) {
            return;
        }
        if (document.hidden || navigator.onLine === false) {
            pause();
            return;
        }
        if (Date.now() - startedAt >= maximumLifetime) {
            window.WebHealth.ajax.renderMessage(
                'Automatic PageSpeed updates stopped. Refresh status to continue.',
                'warning');
            stop();
            return;
        }
        var delay = intervals[Math.min(attempt, intervals.length - 1)];
        activeTimer = window.setTimeout(poll, delay);
        attempt += 1;
    }

    async function poll() {
        activeTimer = null;
        if (document.hidden || navigator.onLine === false) {
            pause();
            return;
        }

        pending = true;
        var root = await window.WebHealth.ajax.load(statusUrl, '#page-audit-results');
        pending = false;
        if (!root) {
            schedule();
            return;
        }
        if (root.getAttribute('data-page-audit-active') !== 'true') {
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
        pausedAt = 0;
        attempt = 0;
        schedule();
    }

    function startFromRoot(root) {
        var host = root.nodeType === 1 && root.matches('[data-page-audit-results]')
            ? root
            : root.querySelector('[data-page-audit-results]');
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
        resume();
    });
    window.addEventListener('offline', function () {
        pause();
    });
    document.addEventListener('visibilitychange', function () {
        if (document.hidden) {
            pause();
        } else {
            resume();
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
