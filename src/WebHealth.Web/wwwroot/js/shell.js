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

    // The account menu is a non-modal popup: it closes on Escape, on a click
    // outside it, and as soon as focus leaves it, so it never traps the user.
    function setUpAccountMenu(container, toggle, menu) {
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
            setUpAccountMenu(account, accountToggle, accountMenu);
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

        // A failed submission re-renders the page; move focus to the summary so
        // keyboard and screen-reader users start at the reported problem.
        var validationSummary = document.querySelector('[data-shell-validation-summary]');
        if (validationSummary) {
            validationSummary.focus();
        }
    });
})();
