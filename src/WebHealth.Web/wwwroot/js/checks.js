(function () {
    'use strict';

    var activeController = null;
    var activeTimer = null;
    var startedAt = 0;
    var attempt = 0;
    var pending = false;
    var statusUrl = null;
    var pausedAt = 0;
    var intervals = [1000, 1500, 2500, 4000, 6000, 10000];
    var maximumLifetime = 120000;

    function clearTimer() {
        if (activeTimer) {
            window.clearTimeout(activeTimer);
        }
        activeTimer = null;
    }

    function stop() {
        if (activeController) {
            activeController.abort();
        }
        clearTimer();
        activeController = null;
        statusUrl = null;
        pausedAt = 0;
    }

    function pause() {
        if (activeController) {
            activeController.abort();
            activeController = null;
        }
        clearTimer();
        if (statusUrl && !pausedAt) {
            pausedAt = Date.now();
        }
    }

    function resume() {
        if (!statusUrl || pending || activeTimer || document.hidden || navigator.onLine === false) {
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
                'Automatic check updates stopped. Refresh status to continue.',
                'warning');
            stop();
            return;
        }
        var delay = intervals[Math.min(attempt, intervals.length - 1)];
        activeTimer = window.setTimeout(poll, delay);
        attempt += 1;
    }

    async function readJson(response) {
        try {
            return await response.json();
        } catch (error) {
            return null;
        }
    }

    async function poll() {
        activeTimer = null;
        if (document.hidden || navigator.onLine === false) {
            pause();
            return;
        }

        var controller = new AbortController();
        activeController = controller;
        pending = true;
        try {
            var response = await fetch(statusUrl, {
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'X-WebHealth-Ajax': '1' },
                signal: controller.signal
            });
            if (response.status === 401) {
                stop();
                window.WebHealth.ajax.renderMessage(
                    window.WebHealth.ajax.messageForStatus(401),
                    'error');
                window.WebHealth.ajax.navigateToLogin();
                return;
            }
            if (response.status === 403 || response.status === 404) {
                stop();
                window.WebHealth.ajax.renderMessage(
                    window.WebHealth.ajax.messageForStatus(response.status),
                    'error');
                return;
            }
            var payload = await readJson(response);
            if (!payload) {
                stop();
                window.WebHealth.ajax.renderMessage('The server returned an invalid check status response.', 'error');
                return;
            }
            if (!response.ok && response.status !== 202) {
                stop();
                window.WebHealth.ajax.renderMessage(
                    window.WebHealth.ajax.messageForProblem(payload, response.status),
                    'error');
                return;
            }
            if (response.status === 202) {
                schedule();
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
                window.WebHealth.ajax.renderMessage('The check status request failed. Retrying.', 'error');
                schedule();
            }
        } finally {
            if (activeController === controller) {
                activeController = null;
            }
            pending = false;
        }
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
        if (document.hidden) {
            pause();
        } else {
            resume();
        }
    });
    window.addEventListener('online', resume);
    window.addEventListener('offline', pause);

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
