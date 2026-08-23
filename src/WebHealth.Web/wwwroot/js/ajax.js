(function () {
    'use strict';

    var AJAX_HEADER = 'X-WebHealth-Ajax';
    var DEFAULT_TARGET = '#ajax-page';
    var activeRequests = new Map();
    var requestVersions = new Map();
    var submittingForms = new WeakSet();

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

    function renderMessage(message, level, retry) {
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

        if (retry) {
            var actions = document.createElement('div');
            actions.className = 'flash__actions';
            var retryButton = document.createElement('button');
            retryButton.type = 'button';
            retryButton.className = 'button button--secondary';
            retryButton.textContent = 'Retry';
            retryButton.addEventListener('click', function () {
                retryButton.disabled = true;
                retry();
            }, { once: true });
            actions.append(retryButton);
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

    function setBusy(source, selector, busy) {
        source.toggleAttribute('data-ajax-loading', busy);
        var target = document.querySelector(selector);
        if (target) {
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

    function nextRequest(selector) {
        var version = (requestVersions.get(selector) || 0) + 1;
        requestVersions.set(selector, version);
        var previous = activeRequests.get(selector);
        if (previous) {
            previous.abort();
        }
        var controller = new AbortController();
        activeRequests.set(selector, controller);
        return { controller: controller, version: version };
    }

    function isCurrentRequest(selector, request) {
        return requestVersions.get(selector) === request.version;
    }

    function focusResponse(root, status) {
        var summary = root.querySelector('[data-shell-validation-summary]');
        if (summary) {
            summary.focus();
            return;
        }

        if (status >= 400 && root.focus) {
            root.setAttribute('tabindex', '-1');
            root.focus();
        }
    }

    function replaceFragment(html, selector, status, url) {
        var parsed = new DOMParser().parseFromString(html, 'text/html');
        var incoming = parsed.querySelector(selector);
        var current = document.querySelector(selector);
        if (!incoming || !current) {
            throw new Error('The server response did not contain the requested page region.');
        }

        document.dispatchEvent(new CustomEvent('webhealth:before-fragment-replace', {
            detail: { root: current }
        }));
        current.replaceWith(incoming);
        if (window.WebHealth.init) {
            window.WebHealth.init(incoming);
        }
        focusResponse(incoming, status);
        document.dispatchEvent(new CustomEvent('webhealth:fragment-ready', {
            detail: { root: incoming, status: status, url: url }
        }));
        return incoming;
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
                return { kind: 'json', value: null };
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

    async function refreshFragment(url, selector) {
        if (!isLocalUrl(url)) {
            throw new Error('The server returned an invalid refresh address.');
        }
        return requestFragment(url, selector, null, null);
    }

    async function handleJson(source, selector, response, payload) {
        if (!response.ok && response.status !== 409) {
            renderMessage(problemMessage(payload, response.status), 'error');
            return null;
        }

        if (response.ok) {
            closeContainingMenu(source);
        }

        if (payload && payload.message) {
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
            await refreshFragment(payload.refreshUrl, selector);
        }

        dispatchResponse(source, selector, response, payload);
        return payload;
    }

    async function handleResponse(source, selector, response, historyMode, requestedUrl) {
        if (response.status === 401) {
            renderMessage(statusMessage(401), 'error');
            navigateToLogin();
            return null;
        }

        var payload = await readPayload(response);
        if (payload.kind === 'json') {
            return handleJson(source, selector, response, payload.value);
        }

        if (!response.ok && response.status !== 409 && response.status !== 422) {
            renderMessage(statusMessage(response.status), 'error');
            return null;
        }

        var root = replaceFragment(payload.value, selector, response.status, requestedUrl);
        if (historyMode === 'push') {
            window.history.pushState({ ajaxTarget: selector }, '', requestedUrl);
        } else if (historyMode === 'replace') {
            window.history.replaceState({ ajaxTarget: selector }, '', requestedUrl);
        }
        return root;
    }

    async function requestFragment(url, selector, source, historyMode, requestInit) {
        var request = nextRequest(selector);
        var options = Object.assign({}, requestInit || {});
        options.headers = Object.assign({}, options.headers || {}, {
            'Accept': 'text/html, application/problem+json, application/json',
            'X-WebHealth-Ajax': '1'
        });
        options.credentials = 'same-origin';
        options.signal = request.controller.signal;

        if (source) {
            setBusy(source, selector, true);
        }
        document.dispatchEvent(new CustomEvent('webhealth:ajax-start', {
            detail: { source: source, target: selector, url: url }
        }));

        try {
            var response = await fetch(url, options);
            if (!isCurrentRequest(selector, request)) {
                return null;
            }
            return await handleResponse(source, selector, response, historyMode, url);
        } catch (error) {
            if (error.name !== 'AbortError') {
                renderMessage(navigator.onLine === false
                    ? 'You are offline. Reconnect and try again.'
                    : 'The network request failed. Try again.', 'error', function () {
                        requestFragment(url, selector, source, historyMode, requestInit);
                    });
            }
            return null;
        } finally {
            if (source) {
                setBusy(source, selector, false);
            }
            if (activeRequests.get(selector) === request.controller) {
                activeRequests.delete(selector);
            }
        }
    }

    function submitForm(event) {
        var form = event.target.closest('form[data-ajax-form]');
        if (!form || event.defaultPrevented || submittingForms.has(form)) {
            return;
        }

        var action = new URL(form.action || window.location.href, window.location.href);
        if (action.origin !== window.location.origin) {
            return;
        }

        event.preventDefault();
        var submitter = event.submitter;
        var entries = formEntries(form, submitter);
        var method = (form.method || 'get').toUpperCase();
        var selector = targetSelector(form);
        var historyMode = form.getAttribute('data-ajax-history');
        var url = method === 'GET' ? buildUrl(action.toString(), entries) : action.toString();
        var options = method === 'GET'
            ? { method: 'GET' }
            : { method: method, body: formData(form, submitter) };

        submittingForms.add(form);
        setSubmitterBusy(submitter, true);
        requestFragment(url, selector, form, historyMode, options).finally(function () {
            submittingForms.delete(form);
            setSubmitterBusy(submitter, false);
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
        load: function (url, selector) {
            return requestFragment(url, selector || DEFAULT_TARGET, null, null, { method: 'GET' });
        },
        renderMessage: renderMessage
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initialize);
    } else {
        initialize();
    }
})();
