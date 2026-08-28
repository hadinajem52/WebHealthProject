(function () {
    'use strict';

    var INTERVALS = [1000, 1000, 1500, 2000, 2000];
    var DEFAULT_LIFETIME = 900000;
    var RESULTS_COOLDOWN = 3000;

    var hostSelector = null;
    var statusUrl = null;
    var finalUrl = null;
    var finalSelectors = null;
    var resultsUrl = null;
    var resultsSelector = null;
    var version = null;
    var resultCount = null;
    var resultsDirty = false;
    var lastResultsRefreshAt = 0;

    var poller = window.WebHealth.createPoller({
        intervals: INTERVALS,
        lifetime: DEFAULT_LIFETIME,
        expiryMessage: 'Automatic updates stopped. Reload the page to see the current state.',
        run: poll
    });

    function isRetryable(status) {
        return status === 0 || status === 429 || status >= 500;
    }

    function versionedStatusUrl() {
        var url = new URL(statusUrl, window.location.href);
        if (version) {
            url.searchParams.set('version', version);
        }
        return url.toString();
    }

    function resultCountOf(payload) {
        if (typeof payload.resultCount === 'number') return payload.resultCount;
        return typeof payload.broken === 'number' ? payload.broken : null;
    }

    function patch(host, payload) {
        host.querySelectorAll('[data-live]').forEach(function (element) {
            var key = element.getAttribute('data-live');
            if (Object.prototype.hasOwnProperty.call(payload, key)) {
                element.textContent = payload[key];
            }
        });
    }

    function focusIsInsideResults() {
        var region = resultsSelector ? document.querySelector(resultsSelector) : null;
        return region && document.activeElement && region.contains(document.activeElement);
    }

    async function refreshResults() {
        if (!resultsDirty || !resultsUrl || !resultsSelector) return;
        if (Date.now() - lastResultsRefreshAt < RESULTS_COOLDOWN || focusIsInsideResults()) return;

        var root = await window.WebHealth.ajax.load(resultsUrl, resultsSelector);
        if (!root) return;
        resultsDirty = false;
        lastResultsRefreshAt = Date.now();
    }

    async function finish() {
        resultsDirty = false;
        if (finalUrl && finalSelectors) {
            await window.WebHealth.ajax.load(finalUrl, finalSelectors);
        }
        return false;
    }

    function fail(status) {
        if (status === 401) {
            window.WebHealth.ajax.navigateToLogin();
        }
        window.WebHealth.ajax.renderMessage(
            window.WebHealth.ajax.messageForStatus(status),
            'error');
        return isRetryable(status);
    }

    async function poll(url, signal) {
        var response = await fetch(versionedStatusUrl(), {
            signal: signal,
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        });

        if (response.status === 204) {
            await refreshResults();
            return true;
        }
        if (!response.ok) return fail(response.status);

        var payload = await response.json();
        var host = document.querySelector(hostSelector);
        if (!host) return false;

        var nextResultCount = resultCountOf(payload);
        if (resultCount !== null && nextResultCount !== null && resultCount !== nextResultCount) {
            resultsDirty = true;
        }
        resultCount = nextResultCount;
        version = payload.version;
        host.setAttribute('data-live-run-version', version);
        patch(host, payload);
        poller.resetBackoff();

        if (payload.active === false) return finish();
        await refreshResults();
        return url;
    }

    function initialResultCount(host) {
        var element = host.querySelector('[data-live="resultCount"], [data-live="broken"]');
        if (!element) return null;
        var parsed = Number(element.textContent);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function configure(host) {
        if (!host || !host.id) return 0;
        hostSelector = '#' + host.id;
        statusUrl = host.getAttribute('data-live-run-status-url');
        finalUrl = host.getAttribute('data-live-run-final-url');
        resultsUrl = host.getAttribute('data-live-run-results-url');
        resultsSelector = host.getAttribute('data-live-run-results-selector');
        version = host.getAttribute('data-live-run-version');
        resultCount = initialResultCount(host);
        resultsDirty = false;
        lastResultsRefreshAt = Date.now();
        var companions = host.getAttribute('data-run-also');
        finalSelectors = companions ? hostSelector + ',' + companions : hostSelector;
        return parseInt(host.getAttribute('data-live-run-lifetime'), 10) || 0;
    }

    function hostWithin(root) {
        if (root.nodeType === 1 && root.matches('[data-live-run-status-url]')) return root;
        return root.querySelector ? root.querySelector('[data-live-run-status-url]') : null;
    }

    function startFromRoot(root) {
        var host = hostWithin(root);
        if (!host) return;
        var lifetime = configure(host);
        if (statusUrl) poller.start(statusUrl, lifetime);
    }

    function startFromScope(scope, url) {
        var host = document.querySelector(scope.getAttribute('data-live-run-scope'));
        if (!host) return;
        var lifetime = configure(host);
        statusUrl = url || statusUrl;
        poller.start(statusUrl, lifetime);
    }

    document.addEventListener('webhealth:ajax-start', function (event) {
        var source = event.detail.source;
        if (source && source.closest('[data-live-run-scope]')) poller.stop();
    });
    document.addEventListener('webhealth:ajax-response', function (event) {
        var source = event.detail.source;
        var payload = event.detail.payload;
        if (!source || !payload || !payload.statusUrl) return;
        var scope = source.closest('[data-live-run-scope]');
        if (scope) startFromScope(scope, payload.statusUrl);
    });
    document.addEventListener('webhealth:fragment-ready', function (event) {
        if (!poller.isPending()) startFromRoot(event.detail.root);
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
