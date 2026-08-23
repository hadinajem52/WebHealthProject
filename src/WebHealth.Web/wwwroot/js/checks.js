(function () {
    'use strict';

    var activeController = null;
    var activeTimer = null;
    var startedAt = 0;
    var attempt = 0;
    var intervals = [1000, 1500, 2500, 4000, 6000, 10000];
    var maximumLifetime = 120000;

    function stop() {
        if (activeController) {
            activeController.abort();
        }
        if (activeTimer) {
            window.clearTimeout(activeTimer);
        }
        activeController = null;
        activeTimer = null;
    }

    function schedule(statusUrl) {
        if (Date.now() - startedAt >= maximumLifetime) {
            stop();
            return;
        }
        var delay = intervals[Math.min(attempt, intervals.length - 1)];
        activeTimer = window.setTimeout(function () { poll(statusUrl); }, delay);
        attempt += 1;
    }

    async function poll(statusUrl) {
        if (document.hidden || navigator.onLine === false) {
            schedule(statusUrl);
            return;
        }

        activeController = new AbortController();
        try {
            var response = await fetch(statusUrl, {
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'X-WebHealth-Ajax': '1' },
                signal: activeController.signal
            });
            if (response.status === 404 || response.status === 403 || response.status === 401) {
                stop();
                return;
            }
            var payload = await response.json();
            if (response.status === 202) {
                schedule(statusUrl);
                return;
            }
            stop();
            if (payload.message) {
                window.WebHealth.ajax.renderMessage(payload.message, payload.level);
            }
            if (payload.refreshUrl) {
                await window.WebHealth.ajax.load(payload.refreshUrl, '#ajax-page');
            }
        } catch (error) {
            if (error.name !== 'AbortError') {
                schedule(statusUrl);
            }
        }
    }

    function start(statusUrl) {
        if (!statusUrl) {
            return;
        }
        stop();
        startedAt = Date.now();
        attempt = 0;
        schedule(statusUrl);
    }

    function startFromRoot(root) {
        var host = root.nodeType === 1 && root.matches('[data-endpoint-details]')
            ? root
            : root.querySelector('[data-endpoint-details]');
        if (host) {
            start(host.getAttribute('data-check-status-url'));
        }
    }

    document.addEventListener('webhealth:ajax-response', function (event) {
        var source = event.detail.source;
        var payload = event.detail.payload;
        if (source && source.closest('[data-endpoint-details]') && payload && payload.statusUrl) {
            start(payload.statusUrl);
        }
    });
    document.addEventListener('webhealth:before-fragment-replace', function (event) {
        if (event.detail.root.matches('[data-endpoint-details]')) {
            stop();
        }
    });
    document.addEventListener('webhealth:fragment-ready', function (event) {
        startFromRoot(event.detail.root);
    });
    document.addEventListener('visibilitychange', function () {
        var host = document.querySelector('[data-endpoint-details]');
        if (!document.hidden && host && !activeTimer) {
            start(host.getAttribute('data-check-status-url'));
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
