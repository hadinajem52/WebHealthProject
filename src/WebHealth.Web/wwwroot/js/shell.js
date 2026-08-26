
(function () {
    'use strict';

    document.documentElement.classList.add('js');
    window.WebHealth = window.WebHealth || {};

    var SIDEBAR_COLLAPSE_STORAGE_KEY = 'webhealth.sidebar-collapsed';

    function readStoredSidebarCollapsed() {
        try {
            return window.localStorage.getItem(SIDEBAR_COLLAPSE_STORAGE_KEY) === 'true';
        } catch (error) {
            return false;
        }
    }

    function storeSidebarCollapsed(collapsed) {
        try {
            window.localStorage.setItem(SIDEBAR_COLLAPSE_STORAGE_KEY, collapsed ? 'true' : 'false');
        } catch (error) {
            return;
        }
    }

    document.documentElement.setAttribute(
        'data-sidebar-collapsed',
        readStoredSidebarCollapsed() ? 'true' : 'false');

    var WIDE_VIEWPORT = '(min-width: 62em)';
    var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    function elements(root, selector) {
        var matches = [];
        if (root.nodeType === 1 && root.matches(selector)) {
            matches.push(root);
        }
        return matches.concat(Array.prototype.slice.call(root.querySelectorAll(selector)));
    }

    function first(root, selector) {
        return root.nodeType === 1 && root.matches(selector) ? root : root.querySelector(selector);
    }

    function beginInitialization(element) {
        if (!element || element.getAttribute('data-shell-initialized') === 'true') {
            return false;
        }
        element.setAttribute('data-shell-initialized', 'true');
        return true;
    }




    function setUpConfirmedSubmission() {
        if (!beginInitialization(document.documentElement)) {
            return;
        }
        document.addEventListener('submit', function (event) {
            var form = event.target;
            if (!form || !form.hasAttribute || !form.hasAttribute('data-shell-confirm')) {
                return;
            }

            if (!window.confirm(form.getAttribute('data-shell-confirm'))) {
                event.preventDefault();
            }
        });
    }





    function setUpPasswordReveal(toggle) {
        if (!beginInitialization(toggle)) {
            return;
        }
        var field = toggle.parentElement;
        var input = field && field.querySelector('input');
        if (!input) {
            return;
        }

        function apply(revealed) {
            input.type = revealed ? 'text' : 'password';
            toggle.setAttribute('aria-pressed', revealed ? 'true' : 'false');
            var label = revealed ? 'Hide password' : 'Show password';
            toggle.setAttribute('aria-label', label);
            toggle.setAttribute('title', label);
        }

        toggle.addEventListener('click', function () {
            var revealed = toggle.getAttribute('aria-pressed') === 'true';
            apply(!revealed);




            var caret = input.value.length;
            input.focus();
            if (input.setSelectionRange) {
                try {
                    input.setSelectionRange(caret, caret);
                } catch (error) {


                }
            }
        });





        var form = input.form;
        if (form) {
            form.addEventListener('submit', function () {
                apply(false);
            });
        }
    }





    var badgeTooltip = null;
    var openBadge = null;
    var badgeListenersReady = false;

    function showBadgeTooltip(badge) {
        var detail = badge.getAttribute('data-badge-detail');
        if (!detail || !badgeTooltip) {
            return;
        }

        openBadge = null;
        badgeTooltip.hidden = true;
        badgeTooltip.style.visibility = 'hidden';
        badgeTooltip.textContent = detail;
        badgeTooltip.hidden = false;
        var anchor = badge.getBoundingClientRect();
        var size = badgeTooltip.getBoundingClientRect();
        var gap = 8;
        var left = anchor.left + (anchor.width / 2) - (size.width / 2);
        left = Math.max(gap, Math.min(left, window.innerWidth - size.width - gap));
        var top = anchor.top - size.height - gap;
        if (top < gap) {
            top = anchor.bottom + gap;
        }
        badgeTooltip.style.left = left + 'px';
        badgeTooltip.style.top = top + 'px';
        badgeTooltip.style.visibility = '';
        openBadge = badge;
    }

    function hideBadgeTooltip(badge) {
        if (badge && openBadge !== badge) {
            return;
        }

        openBadge = null;
        if (badgeTooltip) {
            badgeTooltip.hidden = true;
        }
    }

    function setUpBadgeTooltips(root) {
        var badges = elements(root, '[data-badge-detail]');
        if (badges.length === 0) {
            return;
        }

        if (!badgeTooltip) {
            badgeTooltip = document.createElement('div');
            badgeTooltip.className = 'badge-tooltip';
            badgeTooltip.setAttribute('role', 'tooltip');
            badgeTooltip.hidden = true;
            document.body.appendChild(badgeTooltip);
        }

        Array.prototype.forEach.call(badges, function (badge) {
            if (!beginInitialization(badge)) {
                return;
            }
            badge.removeAttribute('title');
            badge.addEventListener('mouseenter', function () {
                showBadgeTooltip(badge);
            });
            badge.addEventListener('mouseleave', function () {
                hideBadgeTooltip(badge);
            });
            badge.addEventListener('focus', function () {
                showBadgeTooltip(badge);
            });
            badge.addEventListener('blur', function () {
                hideBadgeTooltip(badge);
            });
        });

        if (badgeListenersReady) {
            return;
        }
        badgeListenersReady = true;
        document.addEventListener('keydown', function (event) {
            if (openBadge && event.key === 'Escape') {
                openBadge.blur();
                hideBadgeTooltip();
            }
        });
        window.addEventListener('scroll', function () {
            if (openBadge) {
                hideBadgeTooltip();
            }
        }, true);
        window.addEventListener('resize', function () {
            if (openBadge) {
                hideBadgeTooltip();
            }
        });
    }

    function onReady(callback) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', callback);
        } else {
            callback();
        }
    }

    function focusableElements(container) {
        return Array.prototype.filter.call(
            container.querySelectorAll(FOCUSABLE),
            function (element) {
                return element.offsetParent !== null || element === document.activeElement;
            });
    }

    function setUpNavigationDrawer(sidebar, toggle, scrim, closeButton, content) {
        if (!beginInitialization(sidebar)) {
            return;
        }
        var wideViewport = window.matchMedia(WIDE_VIEWPORT);
        var isOpen = false;





        function setModalState(active) {
            if (active) {
                sidebar.setAttribute('role', 'dialog');
                sidebar.setAttribute('aria-modal', 'true');
            } else {
                sidebar.removeAttribute('role');
                sidebar.removeAttribute('aria-modal');
            }

            if (content) {
                content.inert = active;
            }
        }

        function open() {
            if (isOpen || wideViewport.matches) {
                return;
            }

            isOpen = true;
            sidebar.setAttribute('data-open', 'true');
            toggle.setAttribute('aria-expanded', 'true');
            scrim.hidden = false;
            document.body.classList.add('has-open-drawer');
            setModalState(true);

            var focusable = focusableElements(sidebar);
            if (focusable.length > 0) {
                focusable[0].focus();
            }
        }

        function close(returnFocus) {
            if (!isOpen) {
                return;
            }

            isOpen = false;
            sidebar.setAttribute('data-open', 'false');
            toggle.setAttribute('aria-expanded', 'false');
            scrim.hidden = true;
            document.body.classList.remove('has-open-drawer');
            setModalState(false);

            if (returnFocus) {
                toggle.focus();
            }
        }

        function trapFocus(event) {
            var focusable = focusableElements(sidebar);
            if (focusable.length === 0) {
                return;
            }

            var first = focusable[0];
            var last = focusable[focusable.length - 1];

            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        }

        toggle.addEventListener('click', function () {
            if (isOpen) {
                close(true);
            } else {
                open();
            }
        });

        scrim.addEventListener('click', function () {
            close(true);
        });

        if (closeButton) {
            closeButton.addEventListener('click', function () {
                close(true);
            });
        }

        document.addEventListener('keydown', function (event) {
            if (!isOpen) {
                return;
            }

            if (event.key === 'Escape') {
                close(true);
            } else if (event.key === 'Tab') {
                trapFocus(event);
            }
        });


        var onViewportChange = function (event) {
            if (event.matches) {
                close(false);
            }
        };

        if (typeof wideViewport.addEventListener === 'function') {
            wideViewport.addEventListener('change', onViewportChange);
        } else if (typeof wideViewport.addListener === 'function') {
            wideViewport.addListener(onViewportChange);
        }
    }

    function setUpSidebarCollapse(button, tip) {
        if (!beginInitialization(button)) {
            return;
        }
        var root = document.documentElement;

        function apply(collapsed) {
            root.setAttribute('data-sidebar-collapsed', collapsed ? 'true' : 'false');
            button.setAttribute('aria-expanded', collapsed ? 'false' : 'true');

            var label = collapsed ? 'Open sidebar' : 'Close sidebar';
            button.setAttribute('aria-label', label);

            if (tip) {
                tip.textContent = label;
            }
        }

        apply(root.getAttribute('data-sidebar-collapsed') === 'true');

        button.addEventListener('click', function () {
            var collapsed = root.getAttribute('data-sidebar-collapsed') !== 'true';
            apply(collapsed);
            storeSidebarCollapsed(collapsed);
        });
    }




    function setUpPopupMenu(container, toggle, menu) {
        if (!beginInitialization(container)) {
            return;
        }
        var listenerController = new AbortController();
        var isOpen = false;

        function setOpen(open) {
            isOpen = open;
            container.setAttribute('data-open', open ? 'true' : 'false');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        }

        function close(returnFocus) {
            if (!isOpen) {
                return;
            }

            setOpen(false);

            if (returnFocus) {
                toggle.focus();
            }
        }

        setOpen(false);

        container.webHealthDisposeMenu = function () {
            close(false);
            listenerController.abort();
        };

        container.addEventListener('webhealth:close-menu', function () {
            close(true);
        }, { signal: listenerController.signal });

        toggle.addEventListener('click', function () {
            if (isOpen) {
                close(false);
            } else {
                setOpen(true);
                var focusable = focusableElements(menu);
                if (focusable.length > 0) {
                    focusable[0].focus();
                }
            }
        }, { signal: listenerController.signal });

        document.addEventListener('keydown', function (event) {
            if (isOpen && event.key === 'Escape') {
                close(true);
            }
        }, { signal: listenerController.signal });

        document.addEventListener('pointerdown', function (event) {
            if (isOpen && !container.contains(event.target)) {
                close(false);
            }
        }, { signal: listenerController.signal });

        container.addEventListener('focusout', function (event) {
            if (isOpen && !container.contains(event.relatedTarget)) {
                close(false);
            }
        }, { signal: listenerController.signal });
    }


    var TIMEZONE_STORAGE_KEY = 'webhealth.display-timezone';
    var UTC_ZONE = 'utc';
    var LOCAL_ZONE = 'local';

    function readStoredTimezone() {
        try {
            var stored = window.localStorage.getItem(TIMEZONE_STORAGE_KEY);
            return stored === UTC_ZONE || stored === LOCAL_ZONE ? stored : null;
        } catch (error) {


            return null;
        }
    }

    function storeTimezone(zone) {
        try {
            window.localStorage.setItem(TIMEZONE_STORAGE_KEY, zone);
        } catch (error) {

        }
    }

    function pad(value) {
        return value < 10 ? '0' + value : String(value);
    }



    function zoneAbbreviation(date) {
        try {
            var parts = new Intl.DateTimeFormat(undefined, { timeZoneName: 'short' })
                .formatToParts(date);
            for (var index = 0; index < parts.length; index++) {
                if (parts[index].type === 'timeZoneName') {
                    return parts[index].value;
                }
            }
        } catch (error) {

        }

        var offsetMinutes = -date.getTimezoneOffset();
        var sign = offsetMinutes < 0 ? '-' : '+';
        var absolute = Math.abs(offsetMinutes);
        return 'UTC' + sign + pad(Math.floor(absolute / 60)) + ':' + pad(absolute % 60);
    }

    function formatLocal(date, withSeconds) {
        var text = date.getFullYear() + '-' + pad(date.getMonth() + 1) + '-' + pad(date.getDate())
            + ' ' + pad(date.getHours()) + ':' + pad(date.getMinutes());

        if (withSeconds) {
            text += ':' + pad(date.getSeconds());
        }

        return text + ' ' + zoneAbbreviation(date);
    }

    function formatUtc(date, withSeconds) {
        var text = date.getUTCFullYear() + '-' + pad(date.getUTCMonth() + 1) + '-' + pad(date.getUTCDate())
            + ' ' + pad(date.getUTCHours()) + ':' + pad(date.getUTCMinutes());

        if (withSeconds) {
            text += ':' + pad(date.getUTCSeconds());
        }

        return text + ' UTC';
    }

    function applyTimezone(zone, root) {
        var elements = (root || document).querySelectorAll('time[data-utc-time]');

        Array.prototype.forEach.call(elements, function (element) {
            var instant = element.getAttribute('datetime');
            if (!instant) {
                return;
            }

            var date = new Date(instant);
            if (isNaN(date.getTime())) {
                return;
            }

            var withSeconds = element.getAttribute('data-utc-time') === 'seconds';
            element.textContent = zone === UTC_ZONE
                ? formatUtc(date, withSeconds)
                : formatLocal(date, withSeconds);



            element.title = zone === UTC_ZONE
                ? formatLocal(date, withSeconds)
                : formatUtc(date, withSeconds);
        });
    }

    function setUpTimezonePreference(options, zoneNameLabel) {
        var pending = Array.prototype.filter.call(options, beginInitialization);
        if (pending.length === 0) {
            return;
        }
        var zone = readStoredTimezone() || LOCAL_ZONE;

        if (zoneNameLabel) {
            try {
                var resolved = Intl.DateTimeFormat().resolvedOptions().timeZone;
                if (resolved) {
                    zoneNameLabel.textContent = resolved.replace(/_/g, ' ');
                }
            } catch (error) {

            }
        }

        function sync() {
            Array.prototype.forEach.call(options, function (option) {
                option.checked = option.value === zone;
            });
            applyTimezone(zone);
        }

        Array.prototype.forEach.call(pending, function (option) {
            option.addEventListener('change', function () {
                if (!option.checked) {
                    return;
                }

                zone = option.value;
                storeTimezone(zone);
                applyTimezone(zone);
            });
        });

        sync();
    }

    function setUpFlashDismissal(container) {
        if (!beginInitialization(container)) {
            return;
        }
        var persistentFlashes = container.querySelectorAll('[data-shell-persistent-dismiss-key]');
        persistentFlashes.forEach(function (flash) {
            var key = flash.getAttribute('data-shell-persistent-dismiss-key');
            try {
                if (window.localStorage.getItem(key) === 'dismissed') {
                    flash.remove();
                }
            } catch (error) {

            }
        });

        if (!container.querySelector('.flash')) {
            if (container.hasAttribute('data-ajax-messages')) {
                container.replaceChildren();
                container.classList.remove('flash-messages');
            } else {
                container.remove();
            }
            return;
        }

        container.addEventListener('click', function (event) {
            var button = event.target.closest('[data-shell-flash-dismiss]');
            if (!button) {
                return;
            }

            var flash = button.closest('.flash');
            if (!flash) {
                return;
            }

            var persistentKey = flash.getAttribute('data-shell-persistent-dismiss-key');
            if (persistentKey) {
                try {
                    window.localStorage.setItem(persistentKey, 'dismissed');
                } catch (error) {

                }
            }

            flash.remove();



            var remaining = container.querySelector('[data-shell-flash-dismiss]');
            if (remaining) {
                remaining.focus();
                return;
            }

            if (container.hasAttribute('data-ajax-messages')) {
                container.replaceChildren();
                container.classList.remove('flash-messages');
            } else {
                container.remove();
            }
            var main = document.getElementById('main-content');
            if (main) {
                main.focus();
            }
        });
    }





    function setUpIntervalAvailability(toggle, field, input) {
        if (!beginInitialization(toggle)) {
            return;
        }
        var permissionLocked = input.getAttribute('data-permission-locked') === 'true';

        function sync() {
            var inactive = !toggle.checked;
            field.setAttribute('data-inactive', inactive ? 'true' : 'false');
            input.readOnly = permissionLocked || inactive;
        }

        toggle.addEventListener('change', sync);
        sync();
    }




    function setUpDependentFields(toggle) {
        if (!beginInitialization(toggle)) {
            return;
        }
        var name = toggle.getAttribute('data-shell-dependency');
        var dependents = document.querySelectorAll('[data-shell-dependent-on="' + name + '"]');

        if (!dependents.length) {
            return;
        }

        function sync() {
            var inactive = toggle.checked ? 'false' : 'true';
            dependents.forEach(function (dependent) {
                dependent.setAttribute('data-inactive', inactive);
            });
        }

        toggle.addEventListener('change', sync);
        sync();
    }

    function setUpEndpointRegistration(form) {
        if (!beginInitialization(form)) {
            return;
        }

        var placement = form.querySelector('[data-registration-placement]');
        var createNew = placement ? placement.getAttribute('data-registration-create-new') : '';
        var levels = ['client', 'website', 'environment'].map(function (name) {
            var picker = form.querySelector('[data-registration-picker="' + name + '"]');
            var select = picker ? picker.querySelector('select') : null;
            return {
                name: name,
                picker: picker,
                select: select,
                createOption: select ? select.querySelector('[data-registration-create]') : null,
                records: select
                    ? Array.prototype.slice.call(select.querySelectorAll('[data-registration-parent]'))
                    : []
            };
        }).filter(function (level) { return level.select; });
        var panels = Array.prototype.slice.call(form.querySelectorAll('[data-registration-new]'));
        var monitoring = form.querySelector('[data-registration-monitoring]');
        var authorization = form.querySelector('.registration-authorization');
        var advanced = form.querySelector('[data-registration-advanced]');
        var advancedState = form.querySelector('[data-registration-advanced-state]');

        function isRecord(value) {
            return !!value && value !== createNew;
        }

        function listRecords(level, parentId) {
            var matches = [];
            level.records.forEach(function (option) {
                if (option.parentNode) {
                    option.parentNode.removeChild(option);
                }
                if (option.getAttribute('data-registration-parent') === parentId) {
                    level.select.insertBefore(option, level.createOption);
                    matches.push(option.value);
                }
            });
            return matches;
        }

        function syncPlacement() {
            var parentId = '';
            var parentResolved = true;
            var creating = -1;

            levels.forEach(function (level, index) {
                level.picker.hidden = !parentResolved;
                if (!parentResolved) {
                    level.select.value = '';
                    return;
                }

                var selected = level.select.value;
                var matches = listRecords(level, parentId);
                if (selected !== createNew && matches.indexOf(selected) === -1) {
                    selected = '';
                }
                if (!matches.length) {
                    selected = createNew;
                }

                level.select.value = selected;
                if (creating === -1 && selected === createNew) {
                    creating = index;
                }

                parentId = selected;
                parentResolved = isRecord(parentId);
            });

            panels.forEach(function (panel) {
                var index = levels.findIndex(function (level) {
                    return level.name === panel.getAttribute('data-registration-new');
                });
                panel.hidden = creating === -1 || index < creating;
            });
        }

        function syncMonitoring() {
            if (authorization && monitoring) {
                authorization.setAttribute('data-inactive', monitoring.checked ? 'false' : 'true');
            }
        }

        levels.forEach(function (level) {
            level.select.addEventListener('change', syncPlacement);
        });
        if (monitoring) {
            monitoring.addEventListener('change', syncMonitoring);
        }
        if (advanced && advancedState) {
            advanced.addEventListener('toggle', function () {
                advancedState.value = advanced.open ? 'true' : 'false';
            });
            advancedState.value = advanced.open ? 'true' : 'false';
        }

        syncPlacement();
        syncMonitoring();
    }

    function initialize(root) {
        root = root || document;
        var sidebar = first(root, '[data-shell-sidebar]');
        var toggle = first(root, '[data-shell-toggle]');
        var scrim = first(root, '[data-shell-scrim]');
        var closeButton = first(root, '[data-shell-close]');
        var content = first(root, '[data-shell-content]');

        if (sidebar && toggle && scrim) {
            setUpNavigationDrawer(sidebar, toggle, scrim, closeButton, content);
        }

        var collapseButton = first(root, '[data-shell-collapse]');
        if (collapseButton) {
            setUpSidebarCollapse(
                collapseButton,
                collapseButton.querySelector('[data-shell-collapse-tip]'));
        }

        var account = first(root, '[data-shell-account]');
        var accountToggle = first(root, '[data-shell-account-toggle]');
        var accountMenu = first(root, '[data-shell-account-menu]');

        if (account && accountToggle && accountMenu) {
            setUpPopupMenu(account, accountToggle, accountMenu);
        }

        var notifications = first(root, '[data-shell-notifications]');
        var notificationsToggle = first(root, '[data-shell-notifications-toggle]');
        var notificationsMenu = first(root, '[data-shell-notifications-menu]');

        if (notifications && notificationsToggle && notificationsMenu) {
            setUpPopupMenu(notifications, notificationsToggle, notificationsMenu);
        }



        var filters = first(root, '[data-shell-filters]');
        var filtersToggle = first(root, '[data-shell-filters-toggle]');
        var filtersMenu = first(root, '[data-shell-filters-menu]');

        if (filters && filtersToggle && filtersMenu) {
            setUpPopupMenu(filters, filtersToggle, filtersMenu);
        }



        elements(root, '[data-shell-menu]').forEach(function (container) {
            var menuToggle = container.querySelector('[data-shell-menu-toggle]');
            var menuPanel = container.querySelector('[data-shell-menu-panel]');

            if (menuToggle && menuPanel) {
                setUpPopupMenu(container, menuToggle, menuPanel);
            }
        });

        var settings = first(root, '[data-shell-settings]');
        var settingsToggle = first(root, '[data-shell-settings-toggle]');
        var settingsMenu = first(root, '[data-shell-settings-menu]');

        if (settings && settingsToggle && settingsMenu) {
            setUpPopupMenu(settings, settingsToggle, settingsMenu);
        }

        var timezoneOptions = elements(root, '[data-shell-timezone-option]');
        if (timezoneOptions.length > 0) {
            setUpTimezonePreference(
                timezoneOptions,
                first(root, '[data-shell-timezone-name]'));
        }
        applyTimezone(readStoredTimezone() || LOCAL_ZONE, root);

        elements(root, '.flash-messages').forEach(setUpFlashDismissal);

        var schedulingToggle = first(root, '[data-shell-scheduling-toggle]');
        var intervalField = first(root, '[data-shell-interval-field]');
        var intervalInput = first(root, '[data-shell-interval-input]');

        if (schedulingToggle && intervalField && intervalInput) {
            setUpIntervalAvailability(schedulingToggle, intervalField, intervalInput);
        }

        elements(root, '[data-shell-dependency]')
            .forEach(setUpDependentFields);

        elements(root, '[data-shell-password-toggle]')
            .forEach(setUpPasswordReveal);

        elements(root, '[data-registration-form]')
            .forEach(setUpEndpointRegistration);

        setUpBadgeTooltips(root);

        setUpConfirmedSubmission();



        var validationSummary = first(root, '[data-shell-validation-summary]');
        if (validationSummary) {
            validationSummary.focus();
        }
    }

    window.WebHealth.init = initialize;
    window.WebHealth.applyTimezone = function (root) {
        applyTimezone(readStoredTimezone() || LOCAL_ZONE, root || document);
    };

    document.addEventListener('webhealth:before-fragment-replace', function (event) {
        var root = event.detail.root;
        if (openBadge && (openBadge === root || root.contains(openBadge))) {
            hideBadgeTooltip();
        }

        elements(event.detail.root, '[data-shell-initialized]').forEach(function (element) {
            if (typeof element.webHealthDisposeMenu === 'function') {
                element.webHealthDisposeMenu();
            }
        });
    });

    onReady(function () {
        initialize(document);
    });
})();
