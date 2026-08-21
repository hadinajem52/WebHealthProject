/*
 * Dashboard trend charts.
 *
 * Two single-axis charts rather than one dual-axis chart. Uptime percentage and response latency
 * share a date range and nothing else, and drawing them against two scales in one frame invites
 * a reader to see a correlation between them that the data does not assert.
 *
 * The charts are an enhancement, never the only route to the data. Both canvases are hidden from
 * assistive technology outright and the same numbers are always rendered in a plain table in the
 * served HTML, so a reader without script, without the vendored library, or using a screen reader
 * loses nothing. That is also why every failure here is silent — a broken canvas must not take
 * the page down with it.
 *
 * Chart.js is vendored under wwwroot/lib, matching how bootstrap and jquery are carried. No CDN
 * is used, so the page has no third-party origin to reach at render time.
 */
(function () {
    'use strict';

    // BR-P02's defaults, mirrored from ResponseTimeThresholds.Default. An endpoint may override
    // them, so these are drawn and labelled as the defaults rather than as this view's budget.
    var DEFAULT_WARNING_MS = 1500;
    var DEFAULT_CRITICAL_MS = 3000;

    function readSeries(host) {
        try {
            var parsed = JSON.parse(host.dataset.dashboardTrend || '[]');
            return Array.isArray(parsed) ? parsed : [];
        } catch (error) {
            return [];
        }
    }

    function cssValue(name, fallback) {
        var value = getComputedStyle(document.documentElement).getPropertyValue(name);
        return value ? value.trim() : fallback;
    }

    /*
     * A day the reader has no sample for is not a value, and it is not zero either. Chart.js
     * treats null as a gap once spanGaps is off, which is what missing monitoring data should
     * look like: absent. The previous chart joined straight across those days, drawing a
     * confident line through hours nothing was measured.
     */
    function values(series, key) {
        return series.map(function (point) {
            var value = point[key];
            return typeof value === 'number' ? value : null;
        });
    }

    /*
     * A horizontal reference line, drawn under the data so it never obscures a point. Registered
     * per chart rather than globally so a chart without thresholds is unaffected.
     */
    function thresholdPlugin(lines) {
        return {
            id: 'thresholds',
            beforeDatasetsDraw: function (chart) {
                var scale = chart.scales.y;
                var area = chart.chartArea;
                if (!scale || !area) {
                    return;
                }

                var context = chart.ctx;
                lines.forEach(function (line) {
                    if (line.value < scale.min || line.value > scale.max) {
                        return;
                    }

                    var y = scale.getPixelForValue(line.value);
                    context.save();
                    context.beginPath();
                    context.setLineDash([4, 4]);
                    context.lineWidth = 1;
                    context.strokeStyle = line.color;
                    context.moveTo(area.left, y);
                    context.lineTo(area.right, y);
                    context.stroke();

                    context.setLineDash([]);
                    context.fillStyle = line.color;
                    context.font = '600 11px system-ui, sans-serif';
                    context.textAlign = 'right';
                    context.textBaseline = 'bottom';
                    context.fillText(line.label, area.right - 4, y - 2);
                    context.restore();
                });
            }
        };
    }

    function baseOptions(prefersReducedMotion, yTitle, extraOptions) {
        var options = {
            responsive: true,
            maintainAspectRatio: false,
            animation: prefersReducedMotion ? false : undefined,
            interaction: { mode: 'index', intersect: false },
            scales: {
                y: {
                    type: 'linear',
                    title: { display: true, text: yTitle }
                }
            },
            plugins: {
                legend: { labels: { usePointStyle: true } }
            }
        };

        Object.keys(extraOptions || {}).forEach(function (key) {
            options.scales.y[key] = extraOptions[key];
        });

        return options;
    }

    function render() {
        var host = document.querySelector('[data-dashboard-trend]');
        if (!host || typeof window.Chart === 'undefined') {
            return;
        }

        var series = readSeries(host);
        if (series.length === 0) {
            return;
        }

        var labels = series.map(function (point) { return point.day; });

        // Reduced motion is a stated preference, not a hint: the charts draw in their final
        // position rather than animating into it.
        var prefersReducedMotion = window.matchMedia
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        var availabilityCanvas = document.getElementById('dashboard-trend-availability');
        if (availabilityCanvas) {
            new window.Chart(availabilityCanvas, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [{
                        label: 'Availability %',
                        data: values(series, 'uptime'),
                        borderColor: cssValue('--status-success-text', '#276749'),
                        backgroundColor: cssValue('--status-success-surface', '#f0fff4'),
                        pointStyle: 'circle',
                        tension: 0.25,
                        spanGaps: false,
                        fill: true
                    }]
                },
                options: baseOptions(prefersReducedMotion, 'Availability %', {
                    suggestedMin: 90,
                    suggestedMax: 100
                })
            });
        }

        var responseCanvas = document.getElementById('dashboard-trend-response');
        if (responseCanvas) {
            new window.Chart(responseCanvas, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [
                        {
                            label: 'P50 response time (ms)',
                            data: values(series, 'p50'),
                            borderColor: cssValue('--status-warning-text', '#c05621'),
                            backgroundColor: 'transparent',
                            // Each series keeps its own dash pattern and point shape, so they
                            // stay distinguishable without relying on their colours.
                            borderDash: [2, 3],
                            pointStyle: 'triangle',
                            tension: 0.25,
                            spanGaps: false
                        },
                        {
                            label: 'P95 response time (ms)',
                            data: values(series, 'p95'),
                            borderColor: cssValue('--color-purple-blue-500', '#4318ff'),
                            backgroundColor: 'transparent',
                            borderDash: [6, 4],
                            pointStyle: 'rectRot',
                            tension: 0.25,
                            spanGaps: false
                        }
                    ]
                },
                options: baseOptions(prefersReducedMotion, 'Milliseconds', { beginAtZero: true }),
                plugins: [thresholdPlugin([
                    {
                        value: DEFAULT_WARNING_MS,
                        label: 'Default warning ' + DEFAULT_WARNING_MS + ' ms',
                        color: cssValue('--status-warning-text', '#c05621')
                    },
                    {
                        value: DEFAULT_CRITICAL_MS,
                        label: 'Default critical ' + DEFAULT_CRITICAL_MS + ' ms',
                        color: cssValue('--status-danger-text', '#c53030')
                    }
                ])]
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', render);
    } else {
        render();
    }
})();
