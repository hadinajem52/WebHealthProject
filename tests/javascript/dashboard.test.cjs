const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

test('response tooltips disclose approximation per day in a mixed history window', () => {
    const charts = [];
    const series = [
        { day: '2026-05-01', p50: 100, p95: 500, approximate: true },
        { day: '2026-09-01', p50: 100, p95: 500, approximate: false },
        { day: '2026-09-02', p50: 100, p95: 500 }
    ];
    const host = {
        dataset: { dashboardTrend: JSON.stringify(series) },
        querySelector: selector => selector === '#dashboard-trend-response' ? {} : null
    };
    const context = {
        document: {
            readyState: 'complete',
            documentElement: {},
            querySelector: () => host,
            addEventListener() {}
        },
        window: {
            Chart: function (canvas, configuration) { charts.push(configuration); }
        },
        getComputedStyle: () => ({ getPropertyValue: () => '' })
    };
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/WebHealth.Web/wwwroot/js/dashboard.js'), 'utf8'), context);
    const tooltip = charts[0].options.plugins.tooltip.callbacks.afterLabel;
    for (const datasetIndex of [0, 1]) {
        assert.equal(tooltip({ dataIndex: 0, datasetIndex }), 'Approximate percentile from archived histogram buckets');
        assert.equal(tooltip({ dataIndex: 1, datasetIndex }), '');
        assert.equal(tooltip({ dataIndex: 2, datasetIndex }), '');
    }
});
