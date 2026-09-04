#!/usr/bin/env node
/**
 * Compares BenchmarkDotNet results against the committed performance baseline.
 *
 * Usage:
 *   node scripts/compare-benchmarks.js [--results <dir>] [--baseline <file>] [--threshold <percent>]
 *
 * - Reads every "*-report-full.json" found (recursively) under --results
 *   (default: tests/PDK.Tests.Performance/BenchmarkDotNet.Artifacts).
 * - Reads the baseline (default: tests/PDK.Tests.Performance/Baselines/BaselineData.json). Its schema is
 *   defined by tests/PDK.Tests.Performance/Baselines/PerformanceBaseline.cs:
 *     { "Metrics": { "<Method>": { "Mean": 0.5, "StdDev": 0.1, "Unit": "ms", "RegressionThresholdPercent": 20 } } }
 * - A benchmark regresses when its mean exceeds the baseline mean by more than the threshold. The threshold is
 *   the larger of --threshold (default 25) and the metric's own RegressionThresholdPercent, so that noisy CI
 *   runners never fail the job on small drifts.
 * - Missing results or a missing baseline only produce warnings; the script fails (exit 1) on regressions only.
 */
'use strict';

const fs = require('fs');
const path = require('path');

const DEFAULTS = {
  results: 'tests/PDK.Tests.Performance/BenchmarkDotNet.Artifacts',
  baseline: 'tests/PDK.Tests.Performance/Baselines/BaselineData.json',
  threshold: 25,
};

// BenchmarkDotNet reports statistics in nanoseconds; baseline metrics carry their own unit.
const UNIT_TO_NS = { ns: 1, us: 1e3, 'µs': 1e3, 'μs': 1e3, ms: 1e6, s: 1e9 };

function parseArgs(argv) {
  const options = { ...DEFAULTS };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = argv[i + 1];
    switch (arg) {
      case '--results':
        options.results = value; i++; break;
      case '--baseline':
        options.baseline = value; i++; break;
      case '--threshold':
        options.threshold = Number(value); i++; break;
      case '--help':
      case '-h':
        console.log('Usage: node scripts/compare-benchmarks.js [--results <dir>] [--baseline <file>] [--threshold <percent>]');
        process.exit(0);
        break;
      default:
        console.error(`Unknown argument: ${arg}`);
        process.exit(2);
    }
  }
  if (!Number.isFinite(options.threshold) || options.threshold < 0) {
    console.error(`Invalid --threshold: ${options.threshold}`);
    process.exit(2);
  }
  return options;
}

function warn(message) {
  // "::warning::" renders as an annotation on GitHub Actions and is harmless elsewhere.
  console.log(`::warning::${message}`);
}

function findReports(directory) {
  const reports = [];
  if (!fs.existsSync(directory)) {
    return reports;
  }
  const stack = [directory];
  while (stack.length > 0) {
    const current = stack.pop();
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const fullPath = path.join(current, entry.name);
      if (entry.isDirectory()) {
        stack.push(fullPath);
      } else if (entry.isFile() && entry.name.endsWith('-report-full.json')) {
        reports.push(fullPath);
      }
    }
  }
  return reports.sort();
}

function loadJson(file) {
  return JSON.parse(fs.readFileSync(file, 'utf8'));
}

/** Collects the best (lowest) mean per benchmark method across all reports and jobs, in nanoseconds. */
function collectResults(reportFiles) {
  const results = new Map();
  for (const file of reportFiles) {
    let report;
    try {
      report = loadJson(file);
    } catch (error) {
      warn(`Skipping unreadable benchmark report ${file}: ${error.message}`);
      continue;
    }
    for (const benchmark of report.Benchmarks || []) {
      const name = benchmark.Method || (benchmark.FullName || '').split('.').pop();
      const mean = benchmark.Statistics && benchmark.Statistics.Mean;
      if (!name || typeof mean !== 'number' || !Number.isFinite(mean)) {
        continue;
      }
      const previous = results.get(name);
      if (previous === undefined || mean < previous) {
        results.set(name, mean);
      }
    }
  }
  return results;
}

function formatNs(ns) {
  if (ns >= 1e9) return `${(ns / 1e9).toFixed(3)} s`;
  if (ns >= 1e6) return `${(ns / 1e6).toFixed(3)} ms`;
  if (ns >= 1e3) return `${(ns / 1e3).toFixed(3)} us`;
  return `${ns.toFixed(1)} ns`;
}

function main() {
  const options = parseArgs(process.argv.slice(2));

  const reportFiles = findReports(options.results);
  if (reportFiles.length === 0) {
    warn(`No BenchmarkDotNet "*-report-full.json" files found under ${options.results}; nothing to compare.`);
    return 0;
  }

  if (!fs.existsSync(options.baseline)) {
    warn(`Baseline file ${options.baseline} not found; nothing to compare.`);
    return 0;
  }

  let baseline;
  try {
    baseline = loadJson(options.baseline);
  } catch (error) {
    warn(`Baseline file ${options.baseline} could not be parsed: ${error.message}`);
    return 0;
  }
  const metrics = (baseline && baseline.Metrics) || {};

  const results = collectResults(reportFiles);
  console.log(`Compared ${results.size} benchmark(s) from ${reportFiles.length} report file(s) against ${options.baseline}`);
  console.log('');

  const rows = [];
  const regressions = [];
  const unknown = [];

  for (const [name, currentNs] of [...results.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    const metric = metrics[name];
    if (!metric || typeof metric.Mean !== 'number') {
      unknown.push(name);
      continue;
    }
    const unitFactor = UNIT_TO_NS[String(metric.Unit || 'ms').toLowerCase()];
    if (!unitFactor) {
      warn(`Baseline metric ${name} has unknown unit '${metric.Unit}'; skipped.`);
      continue;
    }
    const baselineNs = metric.Mean * unitFactor;
    if (!(baselineNs > 0)) {
      warn(`Baseline metric ${name} has a non-positive mean; skipped.`);
      continue;
    }
    const threshold = Math.max(options.threshold, Number(metric.RegressionThresholdPercent) || 0);
    const change = ((currentNs - baselineNs) / baselineNs) * 100;
    const isRegression = change > threshold;
    rows.push({ name, baselineNs, currentNs, change, threshold, isRegression });
    if (isRegression) {
      regressions.push({ name, change, threshold });
    }
  }

  const table = [
    '| Benchmark | Baseline | Current | Change | Threshold | Status |',
    '|-----------|----------|---------|--------|-----------|--------|',
    ...rows.map(r => `| ${r.name} | ${formatNs(r.baselineNs)} | ${formatNs(r.currentNs)} | ${r.change >= 0 ? '+' : ''}${r.change.toFixed(1)}% | +${r.threshold}% | ${r.isRegression ? 'REGRESSION' : 'ok'} |`),
  ].join('\n');
  console.log(table);

  if (unknown.length > 0) {
    console.log('');
    console.log(`Benchmarks without a baseline entry (not compared): ${unknown.join(', ')}`);
  }

  if (process.env.GITHUB_STEP_SUMMARY) {
    const summary = ['## Benchmark comparison', '', table, ''];
    if (unknown.length > 0) {
      summary.push(`Without baseline: ${unknown.join(', ')}`, '');
    }
    fs.appendFileSync(process.env.GITHUB_STEP_SUMMARY, summary.join('\n'));
  }

  console.log('');
  if (regressions.length > 0) {
    for (const r of regressions) {
      console.log(`::error::Performance regression: ${r.name} is ${r.change.toFixed(1)}% slower than its baseline (threshold +${r.threshold}%)`);
    }
    return 1;
  }

  console.log(`No regressions above the threshold detected (${rows.length} benchmark(s) compared).`);
  return 0;
}

process.exitCode = main();
