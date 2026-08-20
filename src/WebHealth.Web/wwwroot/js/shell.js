/*
 * Application shell enhancement.
 *
 * Loaded in the document head so the "js" marker is applied before first paint.
 * Everything the script controls has server-rendered behavior without it: the
 * navigation stays in the document flow and every link is a normal request.
 */
(function () {
    'use strict';

    document.documentElement.classList.add('js');

    var WIDE_VIEWPORT = '(min-width: 62em)';
    var FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    // An irreversible action asks once before it runs. The prompt is an
    // enhancement rather than the guard: the server refuses the same request
    // when the caller lacks the role or the endpoint is not archived.
    function setUpConfirmedSubmission() {
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
        var wideViewport = window.matchMedia(WIDE_VIEWPORT);
        var isOpen = false;

        // The narrow-viewport drawer overlays the page, so it is announced as a
        // modal dialog and the rest of the application is made inert while it is
        // open. Both are removed on close, leaving the wide layout as a plain
        // navigation landmark.
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

        // Leaving the narrow layout removes the drawer, so reset its state.
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

    // A non-modal popup: it closes on Escape, on a click outside it, and as soon as
    // focus leaves it, so it never traps the user. Shared by the account and
    // notification menus in the header.
    function setUpPopupMenu(container, toggle, menu) {
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
        });

        document.addEventListener('keydown', function (event) {
            if (isOpen && event.key === 'Escape') {
                close(true);
            }
        });

        document.addEventListener('pointerdown', function (event) {
            if (isOpen && !container.contains(event.target)) {
                close(false);
            }
        });

        container.addEventListener('focusout', function (event) {
            if (isOpen && !container.contains(event.relatedTarget)) {
                close(false);
            }
        });
    }

    /*
     * Time zone display.
     *
     * The server renders every instant as <time datetime="{iso}">…  UTC</time>. The stored value
     * is UTC and stays UTC; this only decides which zone the reader sees, so it is a browser
     * preference held in localStorage rather than anything sent back.
     *
     * The ISO attribute is the source of truth for the conversion. Reparsing the rendered text
     * would mean turning a display string back into a moment, which breaks the first time a
     * format changes.
     */
    var TIMEZONE_STORAGE_KEY = 'webhealth.display-timezone';
    var UTC_ZONE = 'utc';
    var LOCAL_ZONE = 'local';

    function readStoredTimezone() {
        try {
            var stored = window.localStorage.getItem(TIMEZONE_STORAGE_KEY);
            return stored === UTC_ZONE || stored === LOCAL_ZONE ? stored : null;
        } catch (error) {
            // Private browsing and blocked storage both throw here. The preference is a
            // convenience, so losing it must not take the page down with it.
            return null;
        }
    }

    function storeTimezone(zone) {
        try {
            window.localStorage.setItem(TIMEZONE_STORAGE_KEY, zone);
        } catch (error) {
            // Ignored for the same reason.
        }
    }

    function pad(value) {
        return value < 10 ? '0' + value : String(value);
    }

    // The zone's own short name, taken from the formatter rather than assumed, so a zone that
    // shifts for daylight saving is labelled correctly for the instant being shown.
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
            // Fall through to the offset below.
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

            // The other reading stays available on hover, so a reader comparing against a log
            // or a notification never has to convert by hand.
            element.title = zone === UTC_ZONE
                ? formatLocal(date, withSeconds)
                : formatUtc(date, withSeconds);
        });
    }

    function setUpTimezonePreference(options, zoneNameLabel) {
        var zone = readStoredTimezone() || LOCAL_ZONE;

        if (zoneNameLabel) {
            try {
                var resolved = Intl.DateTimeFormat().resolvedOptions().timeZone;
                if (resolved) {
                    zoneNameLabel.textContent = resolved.replace(/_/g, ' ');
                }
            } catch (error) {
                // The default wording already describes it well enough.
            }
        }

        function sync() {
            Array.prototype.forEach.call(options, function (option) {
                option.checked = option.value === zone;
            });
            applyTimezone(zone);
        }

        Array.prototype.forEach.call(options, function (option) {
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

    // Flash messages are transient, so dismissal is client-only: nothing is
    // persisted and the next request renders whatever the server sends.
    function setUpFlashDismissal(container) {
        container.addEventListener('click', function (event) {
            var button = event.target.closest('[data-shell-flash-dismiss]');
            if (!button) {
                return;
            }

            var flash = button.closest('.flash');
            if (!flash) {
                return;
            }

            flash.remove();

            // The dismissed button held focus, so hand it somewhere predictable
            // rather than letting it fall back to the document body.
            var remaining = container.querySelector('[data-shell-flash-dismiss]');
            if (remaining) {
                remaining.focus();
                return;
            }

            container.remove();
            var main = document.getElementById('main-content');
            if (main) {
                main.focus();
            }
        });
    }

    // The monitoring interval only governs the scheduled cadence, so it is shown
    // as inactive while scheduled checks are off. It stays readonly rather than
    // disabled: a disabled input is not submitted, which would silently clear a
    // stored override on the next save.
    function setUpIntervalAvailability(toggle, field, input) {
        var permissionLocked = input.getAttribute('data-permission-locked') === 'true';

        function sync() {
            var inactive = !toggle.checked;
            field.setAttribute('data-inactive', inactive ? 'true' : 'false');
            input.readOnly = permissionLocked || inactive;
        }

        toggle.addEventListener('change', sync);
        sync();
    }

    onReady(function () {
        var sidebar = document.querySelector('[data-shell-sidebar]');
        var toggle = document.querySelector('[data-shell-toggle]');
        var scrim = document.querySelector('[data-shell-scrim]');
        var closeButton = document.querySelector('[data-shell-close]');
        var content = document.querySelector('[data-shell-content]');

        if (sidebar && toggle && scrim) {
            setUpNavigationDrawer(sidebar, toggle, scrim, closeButton, content);
        }

        var account = document.querySelector('[data-shell-account]');
        var accountToggle = document.querySelector('[data-shell-account-toggle]');
        var accountMenu = document.querySelector('[data-shell-account-menu]');

        if (account && accountToggle && accountMenu) {
            setUpPopupMenu(account, accountToggle, accountMenu);
        }

        var notifications = document.querySelector('[data-shell-notifications]');
        var notificationsToggle = document.querySelector('[data-shell-notifications-toggle]');
        var notificationsMenu = document.querySelector('[data-shell-notifications-menu]');

        if (notifications && notificationsToggle && notificationsMenu) {
            setUpPopupMenu(notifications, notificationsToggle, notificationsMenu);
        }

        var settings = document.querySelector('[data-shell-settings]');
        var settingsToggle = document.querySelector('[data-shell-settings-toggle]');
        var settingsMenu = document.querySelector('[data-shell-settings-menu]');

        if (settings && settingsToggle && settingsMenu) {
            setUpPopupMenu(settings, settingsToggle, settingsMenu);
        }

        var timezoneOptions = document.querySelectorAll('[data-shell-timezone-option]');
        if (timezoneOptions.length > 0) {
            setUpTimezonePreference(
                timezoneOptions,
                document.querySelector('[data-shell-timezone-name]'));
        }

        var flashMessages = document.querySelector('.flash-messages');
        if (flashMessages) {
            setUpFlashDismissal(flashMessages);
        }

        var schedulingToggle = document.querySelector('[data-shell-scheduling-toggle]');
        var intervalField = document.querySelector('[data-shell-interval-field]');
        var intervalInput = document.querySelector('[data-shell-interval-input]');

        if (schedulingToggle && intervalField && intervalInput) {
            setUpIntervalAvailability(schedulingToggle, intervalField, intervalInput);
        }

        setUpConfirmedSubmission();

        // A failed submission re-renders the page; move focus to the summary so
        // keyboard and screen-reader users start at the reported problem.
        var validationSummary = document.querySelector('[data-shell-validation-summary]');
        if (validationSummary) {
            validationSummary.focus();
        }
    });
})();
