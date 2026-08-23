(function () {
    'use strict';

    var AJAX_HEADER = 'X-WebHealth-Ajax';
    var DEFAULT_TARGET = '#ajax-page';
    var activeReads = new Map();
    var activeMutations = new Map();
    var pendingReads = new Map();
    var ambiguousTargets = new Set();
    var requestVersions = new Map();
    var submittingForms = new WeakSet();
    var AMBIGUOUS_MUTATION = {};

    window.WebHealth = window.WebHealth || {};

    function isLocalUrl(value) {
        if (!value) {
            return false;
        }

        try {
            return new URL(value, window.location.href).origin === window.location.origin;
        } catch (error) {
            return false;
        }
    }

    function buildUrl(path, entries) {
        var url = new URL(path, window.location.href);
        url.search = '';
        entries.forEach(function (entry) {
            if (entry[1] instanceof File) {
                return;
            }
            url.searchParams.append(entry[0], entry[1]);
        });
        return url.toString();
    }

    function formData(form, submitter) {
        var data;
        try {
            data = new FormData(form, submitter || undefined);
        } catch (error) {
            data = new FormData(form);
            if (submitter && submitter.name) {
                data.append(submitter.name, submitter.value);
            }
        }
        return data;
    }

    function formEntries(form, submitter) {
        return Array.from(formData(form, submitter).entries());
    }

    function targetSelector(source) {
        return source.getAttribute('data-ajax-target') || DEFAULT_TARGET;
    }

    function selectorList(selector) {
        return selector
            .split(',')
            .map(function (part) { return part.trim(); })
            .filter(function (part) { return part.length > 0; });
    }

    function statusMessage(status) {
        switch (status) {
            case 401:
                return 'Your session has expired. Sign in again to continue.';
            case 403:
                return 'You do not have permission to perform this action.';
            case 404:
                return 'The requested record is no longer available.';
            case 409:
                return 'This record changed before the action could be applied.';
            case 422:
                return 'Review the highlighted fields and try again.';
            default:
                return 'The request could not be completed.';
        }
    }

    function renderMessage(message, level, action) {
        if (!message) {
            return;
        }

        var region = document.querySelector('[data-ajax-messages]');
        if (!region) {
            return;
        }

        var flash = document.createElement('div');
        flash.className = 'flash';
        flash.setAttribute('data-level', level || 'information');
        flash.setAttribute('role', level === 'error' ? 'alert' : 'status');

        var body = document.createElement('div');
        var label = document.createElement('p');
        label.className = 'flash__label';
        label.textContent = level === 'error' ? 'Request failed' : level === 'warning' ? 'Attention' : 'Updated';
        var text = document.createElement('p');
        text.className = 'flash__text';
        text.textContent = message;
        body.append(label, text);

        if (action) {
            var actions = document.createElement('div');
            actions.className = 'flash__actions';
            var actionButton = document.createElement('button');
            actionButton.type = 'button';
            actionButton.className = 'button button--secondary';
            actionButton.textContent = action.label;
            actionButton.addEventListener('click', function () {
                actionButton.disabled = true;
                region.replaceChildren();
                action.run();
            }, { once: true });
            actions.append(actionButton);
            body.append(actions);
        }

        var dismiss = document.createElement('button');
        dismiss.type = 'button';
        dismiss.className = 'flash__dismiss';
        dismiss.setAttribute('data-shell-flash-dismiss', '');
        dismiss.setAttribute('aria-label', 'Dismiss this message');
        dismiss.textContent = '×';

        flash.append(body, dismiss);
        region.replaceChildren(flash);
        region.classList.add('flash-messages');

        if (window.WebHealth.init) {
            window.WebHealth.init(region);
        }
    }

    function setBusy(source, selector, busy, request) {
        if (source) {
            if (busy) {
                source.webHealthAjaxBusyVersion = request.version;
                source.toggleAttribute('data-ajax-loading', true);
            } else if (source.webHealthAjaxBusyVersion === request.version) {
                source.toggleAttribute('data-ajax-loading', false);
                delete source.webHealthAjaxBusyVersion;
            }
        }
        var target = document.querySelector(selectorList(selector)[0]);
        if (target && (busy || isCurrentRequest(selector, request))) {
            target.setAttribute('aria-busy', busy ? 'true' : 'false');
        }
    }

    function setSubmitterBusy(submitter, busy) {
        if (!submitter) {
            return;
        }

        if (busy) {
            submitter.dataset.ajaxWasDisabled = submitter.disabled ? 'true' : 'false';
            submitter.disabled = true;
            return;
        }

        if (submitter.dataset.ajaxWasDisabled !== 'true') {
            submitter.disabled = false;
        }
        delete submitter.dataset.ajaxWasDisabled;
    }

    function nextRequest(selector, isRead) {
        var version = (requestVersions.get(selector) || 0) + 1;
        requestVersions.set(selector, version);
        var previous = activeReads.get(selector);
        if (previous) {
            previous.abort();
        }
        var controller = new AbortController();
        if (isRead) {
            activeReads.set(selector, controller);
        }
        return { controller: controller, version: version, isRead: isRead };
    }

    function isCurrentRequest(selector, request) {
        return requestVersions.get(selector) === request.version;
    }

    function focusResponse(root, status, source) {
        var summary = root.querySelector('[data-shell-validation-summary]');
        if (summary) {
            summary.focus();
            return;
        }

        if (status < 400 && source && source.closest('[data-shell-notifications]')) {
            var notificationToggle = root.querySelector('[data-shell-notifications-toggle]');
            if (notificationToggle) {
                notificationToggle.focus();
                return;
            }
        }

        if (status >= 400 || source) {
            var destination = status < 400 ? root.querySelector('h1, h2, h3') || root : root;
            if (destination.focus) {
                destination.setAttribute('tabindex', '-1');
                destination.focus();
            }
        }
    }

    function replaceFragment(html, selector, status, url, source) {
        var parsed = new DOMParser().parseFromString(html, 'text/html');
        var regions = selectorList(selector).map(function (one) {
            return { incoming: parsed.querySelector(one), current: document.querySelector(one) };
        });
        var complete = regions.length > 0 && regions.every(function (region) {
            return region.incoming && region.current;
        });
        if (!complete) {
            throw new Error('The server response did not contain the requested page region.');
        }

        regions.forEach(function (region) {
            document.dispatchEvent(new CustomEvent('webhealth:before-fragment-replace', {
                detail: { root: region.current }
            }));
            region.current.replaceWith(region.incoming);
            if (window.WebHealth.init) {
                window.WebHealth.init(region.incoming);
            }
        });

        var primary = regions[0].incoming;
        focusResponse(primary, status, source);
        regions.forEach(function (region) {
            document.dispatchEvent(new CustomEvent('webhealth:fragment-ready', {
                detail: { root: region.incoming, status: status, url: url }
            }));
        });
        return primary;
    }

    function problemMessage(payload, status) {
        if (!payload) {
            return statusMessage(status);
        }

        var message = payload.detail || payload.title || payload.message || statusMessage(status);
        if (payload.correlationId) {
            message += ' Reference: ' + payload.correlationId;
        }
        return message;
    }

    function navigateToLogin() {
        var returnUrl = window.location.pathname + window.location.search;
        window.location.assign('/Account/Login?returnUrl=' + encodeURIComponent(returnUrl));
    }

    async function readPayload(response) {
        var contentType = response.headers.get('content-type') || '';
        if (contentType.indexOf('json') >= 0) {
            try {
                return { kind: 'json', value: await response.json() };
            } catch (error) {
                return { kind: 'invalid-json', value: null };
            }
        }
        return { kind: 'html', value: await response.text() };
    }

    function dispatchResponse(source, selector, response, payload) {
        document.dispatchEvent(new CustomEvent('webhealth:ajax-response', {
            detail: {
                source: source,
                target: selector,
                status: response.status,
                payload: payload
            }
        }));
    }

    function closeContainingMenu(source) {
        var menu = source && source.closest
            ? source.closest('[data-shell-menu]')
            : null;
        if (menu) {
            menu.dispatchEvent(new CustomEvent('webhealth:close-menu'));
        }
    }

    function reloadAction() {
        return {
            label: 'Reload page',
            run: function () {
                window.location.reload();
            }
        };
    }

    function lockAmbiguousMutation(selector, request, message) {
        request.isAmbiguous = true;
        ambiguousTargets.add(selector);
        renderMessage(message, 'error', reloadAction());
    }

    async function refreshFragment(url, selector, source, request) {
        if (!isLocalUrl(url)) {
            throw new Error('The server returned an invalid refresh address.');
        }
        var response = await fetch(url, {
            method: 'GET',
            credentials: 'same-origin',
            headers: {
                'Accept': 'text/html, application/problem+json, application/json',
                'X-WebHealth-Ajax': '1'
            },
            signal: request.controller.signal
        });
        if (!isCurrentRequest(selector, request)) {
            return null;
        }
        return handleResponse(source, selector, response, null, url, request);
    }

    async function handleJson(source, selector, response, payload, request) {
        if (!payload || typeof payload !== 'object' || Array.isArray(payload)) {
            if (!request.isRead && response.ok) {
                lockAmbiguousMutation(
                    selector,
                    request,
                    'The server returned an invalid response. The operation may have completed; reload before trying again.');
            } else {
                renderMessage('The server returned an invalid response.', 'error');
            }
            return null;
        }

        var isError = !response.ok && response.status !== 409;
        if (isError) {
            renderMessage(problemMessage(payload, response.status), 'error');
        }

        if (response.ok) {
            closeContainingMenu(source);
        }

        if (!isError && payload.message) {
            renderMessage(payload.message, payload.level);
        }
        if (payload && payload.redirectUrl) {
            if (!isLocalUrl(payload.redirectUrl)) {
                renderMessage('The server returned an invalid redirect address.', 'error');
                return null;
            }
            window.location.assign(payload.redirectUrl);
            return null;
        }
        if (payload && payload.refreshUrl) {
            await refreshFragment(payload.refreshUrl, selector, source, request);
        }

        dispatchResponse(source, selector, response, payload);
        return payload;
    }

    async function handleResponse(source, selector, response, historyMode, requestedUrl, request) {
        if (response.status === 401) {
            renderMessage(statusMessage(401), 'error');
            navigateToLogin();
            return null;
        }

        var payload = await readPayload(response);
        if (!isCurrentRequest(selector, request)) {
            return null;
        }
        if (payload.kind === 'invalid-json') {
            if (!request.isRead && response.ok) {
                lockAmbiguousMutation(
                    selector,
                    request,
                    'The server returned an invalid response. The operation may have completed; reload before trying again.');
            } else {
                renderMessage('The server returned an invalid response.', 'error');
            }
            return null;
        }
        if (payload.kind === 'json') {
            return handleJson(source, selector, response, payload.value, request);
        }

        if (!response.ok && response.status !== 409 && response.status !== 422) {
            renderMessage(statusMessage(response.status), 'error');
            return null;
        }

        var root = replaceFragment(payload.value, selector, response.status, requestedUrl, source);
        if (request.isRead && response.ok) {
            ambiguousTargets.delete(selector);
        }
        if (historyMode === 'push') {
            window.history.pushState({ ajaxTarget: selector }, '', requestedUrl);
        } else if (historyMode === 'replace') {
            window.history.replaceState({ ajaxTarget: selector }, '', requestedUrl);
        }
        return root;
    }

    function queueRead(url, selector, source, historyMode, requestInit) {
        var previous = pendingReads.get(selector);
        if (previous) {
            previous.resolve(null);
        }
        return new Promise(function (resolve) {
            pendingReads.set(selector, {
                url: url,
                source: source,
                historyMode: historyMode,
                requestInit: requestInit,
                resolve: resolve
            });
        });
    }

    function runPendingRead(selector) {
        var pending = pendingReads.get(selector);
        if (!pending) {
            return;
        }
        pendingReads.delete(selector);
        requestFragment(
            pending.url,
            selector,
            pending.source,
            pending.historyMode,
            pending.requestInit).then(pending.resolve);
    }

    async function executeRequest(url, selector, source, historyMode, requestInit, request) {
        var options = Object.assign({}, requestInit || {});
        var caller = options.abortSignal;
        var reportStatus = options.onStatus;
        delete options.abortSignal;
        delete options.onStatus;
        options.headers = Object.assign({}, options.headers || {}, {
            'Accept': 'text/html, application/problem+json, application/json',
            'X-WebHealth-Ajax': '1'
        });
        options.credentials = 'same-origin';
        options.signal = request.controller.signal;
        if (caller) {
            if (caller.aborted) {
                request.controller.abort();
            } else {
                caller.addEventListener('abort', function () {
                    request.controller.abort();
                }, { once: true });
            }
        }

        setBusy(source, selector, true, request);
        document.dispatchEvent(new CustomEvent('webhealth:ajax-start', {
            detail: { source: source, target: selector, url: url }
        }));

        try {
            var response = await fetch(url, options);
            if (!isCurrentRequest(selector, request)) {
                return null;
            }
            if (reportStatus) {
                reportStatus(response.status);
            }
            var result = await handleResponse(source, selector, response, historyMode, url, request);
            return request.isAmbiguous ? AMBIGUOUS_MUTATION : result;
        } catch (error) {
            if (error.name !== 'AbortError') {
                var message = navigator.onLine === false
                    ? 'You are offline. Reconnect and try again.'
                    : request.isRead
                        ? 'The network request failed. Try again.'
                        : 'The network request failed. The operation may have completed; reload before trying again.';
                if (!request.isRead) {
                    lockAmbiguousMutation(selector, request, message);
                } else {
                    renderMessage(message, 'error', {
                        label: 'Retry',
                        run: function () {
                            requestFragment(url, selector, source, historyMode, requestInit);
                        }
                    });
                }
            }
            return request.isAmbiguous ? AMBIGUOUS_MUTATION : null;
        } finally {
            setBusy(source, selector, false, request);
            if (activeReads.get(selector) === request.controller) {
                activeReads.delete(selector);
            }
        }
    }

    function requestFragment(url, selector, source, historyMode, requestInit) {
        var method = ((requestInit && requestInit.method) || 'GET').toUpperCase();
        var isRead = method === 'GET' || method === 'HEAD';
        if (isRead && activeMutations.has(selector)) {
            return queueRead(url, selector, source, historyMode, requestInit);
        }
        if (!isRead && (activeMutations.has(selector) || ambiguousTargets.has(selector))) {
            renderMessage(
                ambiguousTargets.has(selector)
                    ? 'Reload this page before making another change.'
                    : 'Another update is still in progress.',
                'warning',
                ambiguousTargets.has(selector) ? reloadAction() : null);
            return Promise.resolve(null);
        }

        var request = nextRequest(selector, isRead);
        var operation = executeRequest(url, selector, source, historyMode, requestInit, request);
        if (isRead) {
            return operation;
        }
        activeMutations.set(selector, operation);
        operation.finally(function () {
            if (activeMutations.get(selector) === operation) {
                activeMutations.delete(selector);
                runPendingRead(selector);
            }
        });
        return operation;
    }

    function submitForm(event) {
        var form = event.target.closest('form[data-ajax-form]');
        if (!form || event.defaultPrevented) {
            return;
        }

        var action = new URL(form.action || window.location.href, window.location.href);
        if (action.origin !== window.location.origin) {
            return;
        }

        event.preventDefault();
        var submitter = event.submitter;
        var method = (form.method || 'get').toUpperCase();
        var isRead = method === 'GET' || method === 'HEAD';
        if (!isRead && submittingForms.has(form)) {
            return;
        }
        var entries = formEntries(form, submitter);
        var selector = targetSelector(form);
        var historyMode = form.getAttribute('data-ajax-history');
        var url = method === 'GET' ? buildUrl(action.toString(), entries) : action.toString();
        var options = method === 'GET'
            ? { method: 'GET' }
            : { method: method, body: formData(form, submitter) };

        if (isRead) {
            requestFragment(url, selector, form, historyMode, options);
            return;
        }

        submittingForms.add(form);
        setSubmitterBusy(submitter, true);
        requestFragment(url, selector, form, historyMode, options)
            .then(function (result) {
                if (result !== AMBIGUOUS_MUTATION) {
                    submittingForms.delete(form);
                    setSubmitterBusy(submitter, false);
                }
            });
    }

    function activateLink(event) {
        var link = event.target.closest('a[data-ajax-link]');
        if (!link || event.defaultPrevented || event.button !== 0
            || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey
            || link.target || link.hasAttribute('download')) {
            return;
        }

        var url = new URL(link.href, window.location.href);
        if (url.origin !== window.location.origin) {
            return;
        }

        event.preventDefault();
        requestFragment(
            url.toString(),
            targetSelector(link),
            link,
            link.getAttribute('data-ajax-history') || 'push',
            { method: 'GET' });
    }

    function restoreHistory(event) {
        var selector = event.state && event.state.ajaxTarget;
        if (!selector || !document.querySelector(selector)) {
            window.location.reload();
            return;
        }
        requestFragment(window.location.href, selector, document.querySelector(selector), null, { method: 'GET' });
    }

    function initialize() {
        if (document.querySelector(DEFAULT_TARGET)) {
            window.history.replaceState({ ajaxTarget: DEFAULT_TARGET }, '', window.location.href);
        }
        document.addEventListener('submit', submitForm);
        document.addEventListener('click', activateLink);
        window.addEventListener('popstate', restoreHistory);
    }

    window.WebHealth.ajax = {
        buildUrl: buildUrl,
        isLocalUrl: isLocalUrl,
        messageForStatus: statusMessage,
        messageForProblem: problemMessage,
        navigateToLogin: navigateToLogin,
        load: function (url, selector, options) {
            return requestFragment(
                url,
                selector || DEFAULT_TARGET,
                null,
                null,
                Object.assign({ method: 'GET' }, options || {}));
        },
        renderMessage: renderMessage
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
