/**
 * Summarises the realistic per-request benchmark relative to Awaiten for every other container, with the version
 * that was measured. Reads the same data as `AwaitenBenchmarkResult`.
 */
import React, {type ReactElement} from 'react';
import benchmarks from '@site/src/data/benchmarks.json';
import styles from './styles.module.css';

const SCENARIO = 'Realistic';
const BASELINE_LIBRARY = 'Awaiten';
const OTHER_LIBRARIES = ['MsDI', 'Autofac', 'DryIoc', 'SimpleInjector'] as const;
const DISPLAY_NAMES: Record<string, string> = {
  MsDI: 'MS.DI',
  SimpleInjector: 'Simple Injector',
};

type Sample = {timeNs: number; memoryBytes?: number};
type Scenario = {parameters: string[]; samples: Record<string, Record<string, Sample>>};
const data = benchmarks as {benchmarks: Record<string, Scenario>; versions?: Record<string, string>};

function factor(baseline: number | undefined, value: number | undefined): number | undefined {
  if (baseline === undefined || value === undefined) return undefined;
  if (baseline === value) return 1;
  return baseline === 0 ? Infinity : value / baseline;
}

function formatFactor(value: number | undefined): string {
  if (value === undefined) return '—';
  if (!Number.isFinite(value)) return '∞';
  return value < 10 ? `${value.toFixed(1)}×` : `${Math.round(value)}×`;
}

export default function AwaitenBenchmarkSummary(): ReactElement | null {
  const scenario = data.benchmarks[SCENARIO];
  const samples = scenario?.samples[scenario.parameters[0]] ?? {};
  const baseline = samples[BASELINE_LIBRARY];
  if (!baseline) return null;

  const rows = OTHER_LIBRARIES
    .filter(library => samples[library])
    .map(library => ({
      library,
      version: data.versions?.[library],
      time: factor(baseline.timeNs, samples[library].timeNs)!,
      memory: factor(baseline.memoryBytes, samples[library].memoryBytes),
    }))
    .sort((a, b) => a.time - b.time);

  return (
    <div className={styles.summary}>
      <div className={styles.inline}>
        {rows.map(row => (
          <div key={row.library} className={styles.inlineItem}>
            <div className={styles.name}>
              {DISPLAY_NAMES[row.library] ?? row.library}
              {row.version && <span className={styles.version}> v{row.version}</span>}
            </div>
            <div><strong>{formatFactor(row.time)}</strong> <span className={styles.muted}>as long</span></div>
            <div><strong>{formatFactor(row.memory)}</strong> <span className={styles.muted}>as much memory</span></div>
          </div>
        ))}
      </div>
      <div className={styles.caption}>Relative to Awaiten for one realistic request.</div>
    </div>
  );
}
