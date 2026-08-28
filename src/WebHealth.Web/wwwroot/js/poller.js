(function () {
    'use strict';

    window.WebHealth = window.WebHealth || {};

    var instances = [];

    function createPoller(options) {
        var intervals = options.intervals;
        var defaultLifetime = options.lifetime;
        var expiryMessage = options.expiryMessage;
        var run = options.run;

        var timer = null;
        var controller = null;
        var target = null;
        var lifetime = defaultLifetime;
        var startedAt = 0;
        var pausedAt = 0;
        var attempt = 0;
        var generation = 0;
        var pending = false;

        function clearTimer() {
            if (timer) {
                window.clearTimeout(timer);
            }
            timer = null;
        }

        function abort() {
            if (controller) {
                controller.abort();
                controller = null;
            }
        }

        function stop() {
            generation += 1;
            clearTimer();
            abort();
            target = null;
            pausedAt = 0;
            pending = false;
        }




        function pause() {
            clearTimer();
            if (controller) {
                generation += 1;
                abort();
            }
            if (target && !pausedAt) {
                pausedAt = Date.now();
            }
        }

        function resume() {
            if (!target || timer || pending || document.hidden || navigator.onLine === false) {
                return;
            }
            if (pausedAt) {
                startedAt += Date.now() - pausedAt;
                pausedAt = 0;
            }
            schedule();
        }

        function schedule() {
            if (!target) {
                return;
            }
            if (document.hidden || navigator.onLine === false) {
                pause();
                return;
            }
            if (Date.now() - startedAt >= lifetime) {
                window.WebHealth.ajax.renderMessage(expiryMessage, 'warning');
                stop();
                return;
            }
            timer = window.setTimeout(tick, intervals[Math.min(attempt, intervals.length - 1)]);
            attempt += 1;
        }

        async function tick() {
            timer = null;
            if (document.hidden || navigator.onLine === false) {
                pause();
                return;
            }

            var current = generation;
            var url = target;
            controller = new AbortController();
            var signal = controller.signal;
            var outcome;
            pending = true;
            try {
                outcome = await run(url, signal);
            } catch (error) {
                outcome = error && error.name === 'AbortError' ? false : true;
            } finally {
                pending = false;
                if (current === generation && controller && controller.signal === signal) {
                    controller = null;
                }
            }

            if (current !== generation) {
                return;
            }
            if (outcome === false) {
                stop();
                return;
            }
            if (typeof outcome === 'string' && outcome) {
                target = outcome;
            }
            schedule();
        }

        var poller = {
            start: function (url, lifetimeOverride) {
                if (!url) {
                    return;
                }
                stop();
                target = url;
                lifetime = lifetimeOverride > 0 ? lifetimeOverride : defaultLifetime;
                startedAt = Date.now();
                pausedAt = 0;
                attempt = 0;
                schedule();
            },
            stop: stop,
            pause: pause,
            resume: resume,
            resetBackoff: function () {
                attempt = 0;
            },
            isPending: function () {
                return pending;
            },
            isRunning: function () {
                return target !== null;
            }
        };

        instances.push(poller);
        return poller;
    }

    function each(action) {
        instances.forEach(function (poller) {
            action(poller);
        });
    }

    document.addEventListener('visibilitychange', function () {
        each(function (poller) {
            if (document.hidden) {
                poller.pause();
            } else {
                poller.resume();
            }
        });
    });
    window.addEventListener('online', function () {
        each(function (poller) {
            poller.resume();
        });
    });
    window.addEventListener('offline', function () {
        each(function (poller) {
            poller.pause();
        });
    });

    window.WebHealth.createPoller = createPoller;
})();
