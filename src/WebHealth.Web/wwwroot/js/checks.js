(function () {
    'use strict';

    var poller = window.WebHealth.createPoller({
        intervals: [1000, 1500, 2500, 4000, 6000, 10000],
        lifetime: 120000,
        expiryMessage: 'Automatic check updates stopped. Refresh status to continue.',
        run: poll
    });

    async function readJson(response) {
        try {
            return await response.json();
        } catch (error) {
            return null;
        }
    }

    async function poll(url, signal) {
        try {
            var response = await fetch(url, {
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json', 'X-WebHealth-Ajax': '1' },
                signal: signal
            });
            if (response.status === 401) {
                window.WebHealth.ajax.renderMessage(
                    window.WebHealth.ajax.messageForStatus(401),
                    'error');
                window.WebHealth.ajax.navigateToLogin();
                return false;
            }
            if (response.status === 403 || response.status === 404) {
                window.WebHealth.ajax.renderMessage(
                    window.WebHealth.ajax.messageForStatus(response.status),
                    'error');
                return false;
            }
            var payload = await readJson(response);
            if (!payload) {
                window.WebHealth.ajax.renderMessage('The server returned an invalid check status response.', 'error');
                return false;
            }
            if (!response.ok && response.status !== 202) {
                window.WebHealth.ajax.renderMessage(
                    window.WebHealth.ajax.messageForProblem(payload, response.status),
                    'error');
                return false;
            }
            if (response.status === 202) {
                return payload.statusUrl || true;
            }
            if (payload.message) {
                window.WebHealth.ajax.renderMessage(payload.message, payload.level);
            }
            if (payload.refreshUrl) {
                await window.WebHealth.ajax.load(payload.refreshUrl, '#ajax-page');
            }
            return false;
        } catch (error) {
            if (error.name === 'AbortError') {
                return false;
            }
            window.WebHealth.ajax.renderMessage('The check status request failed. Retrying.', 'error');
            return true;
        }
    }

    function startFromRoot(root) {
        var host = root.nodeType === 1 && root.matches('[data-endpoint-details]')
            ? root
            : root.querySelector('[data-endpoint-details]');
        if (host) {
            poller.start(host.getAttribute('data-check-status-url'));
        }
    }

    document.addEventListener('webhealth:ajax-response', function (event) {
        var source = event.detail.source;
        var payload = event.detail.payload;
        if (source && source.closest('[data-endpoint-details]') && payload && payload.statusUrl) {
            poller.start(payload.statusUrl);
        }
    });
    document.addEventListener('webhealth:before-fragment-replace', function (event) {
        if (event.detail.root.matches('[data-endpoint-details]')) {
            poller.stop();
        }
    });
    document.addEventListener('webhealth:fragment-ready', function (event) {
        startFromRoot(event.detail.root);
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
