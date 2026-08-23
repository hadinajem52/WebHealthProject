(function () {
    'use strict';

    var DEFAULT_INTERVALS = [800, 1200, 2000, 3500, 5000, 8000, 12000];
    var DEFAULT_LIFETIME = 900000;

    var hostSelector = null;
    var regionSelectors = null;
    var completeMessage = null;

    var poller = window.WebHealth.createPoller({
        intervals: DEFAULT_INTERVALS,
        lifetime: DEFAULT_LIFETIME,
        expiryMessage: 'Automatic updates stopped. Reload the page to see the current state.',
        run: refresh
    });

    async function refresh(url, signal) {
        var root = await window.WebHealth.ajax.load(url, regionSelectors, { abortSignal: signal });
        if (!root) {
            return true;
        }

        var host = document.querySelector(hostSelector);
        if (!host) {
            return false;
        }
        configure(host);
        if (host.getAttribute('data-run-active') === 'true') {
            return host.getAttribute('data-run-url') || url;
        }

        if (completeMessage) {
            window.WebHealth.ajax.renderMessage(completeMessage, 'success');
        }
        return false;
    }

    function configure(host) {
        if (!host || !host.id) {
            return 0;
        }
        hostSelector = '#' + host.id;
        var companions = host.getAttribute('data-run-also');
        regionSelectors = companions ? hostSelector + ',' + companions : hostSelector;
        completeMessage = host.getAttribute('data-run-complete-message');
        return parseInt(host.getAttribute('data-run-lifetime'), 10) || 0;
    }

    function hostWithin(root) {
        if (root.nodeType === 1 && root.matches('[data-run-status]')) {
            return root;
        }
        return root.querySelector ? root.querySelector('[data-run-status]') : null;
    }

    function startFromRoot(root) {
        var host = hostWithin(root);
        if (!host || host.getAttribute('data-run-active') !== 'true') {
            return;
        }
        var lifetime = configure(host);
        poller.start(host.getAttribute('data-run-url'), lifetime);
    }

    function startFromScope(scope, url) {
        var host = document.querySelector(scope.getAttribute('data-run-scope'));
        if (!host) {
            return;
        }
        var lifetime = configure(host);
        poller.start(url, lifetime);
    }

    document.addEventListener('webhealth:ajax-start', function (event) {
        var source = event.detail.source;
        if (source && source.closest('[data-run-scope]')) {
            poller.stop();
        }
    });
    document.addEventListener('webhealth:ajax-response', function (event) {
        var source = event.detail.source;
        var payload = event.detail.payload;
        if (!source || !payload || !payload.statusUrl) {
            return;
        }
        var scope = source.closest('[data-run-scope]');
        if (scope) {
            startFromScope(scope, payload.statusUrl);
        }
    });
    document.addEventListener('webhealth:fragment-ready', function (event) {
        if (!poller.isPending()) {
            startFromRoot(event.detail.root);
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { startFromRoot(document); });
    } else {
        startFromRoot(document);
    }
})();
