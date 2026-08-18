/*
 * Dashboard trend chart.
 *
 * The chart is an enhancement, never the only route to the data. The canvas is hidden from
 * assistive technology outright and the same numbers are always rendered in a plain table beside
 * it, so a reader without script, without the vendored library, or using a screen reader loses
 * nothing. That is also why every failure here is silent — a broken canvas must not take the
 * page down with it.
 *
 * Chart.js is vendored under wwwroot/lib, matching how bootstrap and jquery are carried. No CDN
 * is used, so the page has no third-party origin to reach at render time.
 */
(function () {
    'use strict';

    function readSeries(canvas) {
        try {
            var parsed = JSON.parse(canvas.dataset.dashboardTrend || '[]');
            return Array.isArray(parsed) ? parsed : [];
        } catch (error) {
            return [];
        }
    }

    function cssValue(name, fallback) {
        var value = getComputedStyle(document.documentElement).getPropertyValue(name);
        return value ? value.trim() : fallback;
    }

    function render() {
        var canvas = document.getElementById('dashboard-trend');
        if (!canvas || typeof window.Chart === 'undefined') {
            return;
        }

        var series = readSeries(canvas);
        if (series.length === 0) {
            return;
        }

        // Reduced motion is a stated preference, not a hint: the chart draws in its final
        // position rather than animating into it.
        var prefersReducedMotion = window.matchMedia
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;

        new window.Chart(canvas, {
            type: 'line',
            data: {
                labels: series.map(function (point) { return point.day; }),
                datasets: [
                    {
                        label: 'Uptime %',
                        data: series.map(function (point) { return point.uptime; }),
                        borderColor: cssValue('--status-success-text', '#22543d'),
                        backgroundColor: cssValue('--status-success-surface', '#f0fff4'),
                        yAxisID: 'uptime',
                        tension: 0.25,
                        // Each series gets its own dash pattern and point shape, so they stay
                        // distinguishable without relying on their colours.
                        pointStyle: 'circle',
                        spanGaps: true
                    },
                    {
                        label: 'P50 response time (ms)',
                        data: series.map(function (point) { return point.p50; }),
                        borderColor: cssValue('--status-warning-text', '#c05621'),
                        backgroundColor: 'transparent',
                        borderDash: [2, 3],
                        pointStyle: 'triangle',
                        yAxisID: 'duration',
                        tension: 0.25,
                        spanGaps: true
                    },
                    {
                        label: 'P95 response time (ms)',
                        data: series.map(function (point) { return point.p95; }),
                        borderColor: cssValue('--color-purple-blue-500', '#3182ce'),
                        backgroundColor: 'transparent',
                        borderDash: [6, 4],
                        pointStyle: 'rectRot',
                        yAxisID: 'duration',
                        tension: 0.25,
                        spanGaps: true
                    }
                ]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: prefersReducedMotion ? false : undefined,
                interaction: { mode: 'index', intersect: false },
                scales: {
                    uptime: {
                        type: 'linear',
                        position: 'left',
                        suggestedMin: 90,
                        suggestedMax: 100,
                        title: { display: true, text: 'Uptime %' }
                    },
                    duration: {
                        type: 'linear',
                        position: 'right',
                        beginAtZero: true,
                        grid: { drawOnChartArea: false },
                        title: { display: true, text: 'Milliseconds' }
                    }
                },
                plugins: {
                    legend: { labels: { usePointStyle: true } }
                }
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', render);
    } else {
        render();
    }
})();
