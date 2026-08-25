
(function () {
    'use strict';



    var DEFAULT_WARNING_MS = 1500;
    var DEFAULT_CRITICAL_MS = 3000;
    var charts = [];

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


    function values(series, key) {
        return series.map(function (point) {
            var value = point[key];
            return typeof value === 'number' ? value : null;
        });
    }


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

    function render(root) {
        root = root || document;
        var host = root.nodeType === 1 && root.matches('[data-dashboard-trend]')
            ? root
            : root.querySelector('[data-dashboard-trend]');
        if (!host || typeof window.Chart === 'undefined') {
            return;
        }

        var series = readSeries(host);
        if (series.length === 0) {
            return;
        }

        var labels = series.map(function (point) { return point.day; });



        var prefersReducedMotion = window.matchMedia
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        var availabilityCanvas = host.querySelector('#dashboard-trend-availability');
        if (availabilityCanvas) {
            charts.push(new window.Chart(availabilityCanvas, {
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
            }));
        }

        var responseCanvas = host.querySelector('#dashboard-trend-response');
        if (responseCanvas) {
            charts.push(new window.Chart(responseCanvas, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [
                        {
                            label: 'P50 response time (ms)',
                            data: values(series, 'p50'),
                            borderColor: cssValue('--status-warning-text', '#c05621'),
                            backgroundColor: 'transparent',


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
            }));
        }
    }

    function destroy(root) {
        charts = charts.filter(function (chart) {
            if (!root.contains(chart.canvas)) {
                return true;
            }
            chart.destroy();
            return false;
        });
    }

    window.WebHealth = window.WebHealth || {};
    window.WebHealth.dashboard = { init: render, destroy: destroy };

    document.addEventListener('webhealth:before-fragment-replace', function (event) {
        destroy(event.detail.root);
    });
    document.addEventListener('webhealth:fragment-ready', function (event) {
        render(event.detail.root);
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { render(document); });
    } else {
        render(document);
    }
})();
