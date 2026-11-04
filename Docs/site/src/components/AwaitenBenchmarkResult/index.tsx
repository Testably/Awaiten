/**
 * Renders the latest Awaiten-vs-other-DI-containers snapshot for a single
 * benchmark scenario ("Build", "Resolve", "Realistic"). Reads from
 * `src/data/benchmarks.json`, which the `DocsData` build target refreshes before
 * each deployment from the `benchmarks` branch. The committed file doubles as a
 * fallback so `npm start`/`npm run build` work in a clean checkout.
 *
 * The Build/Resolve scenarios are parameterised by container size; the
 * component renders inline Size=8 / Size=64 / Size=256 buttons so readers can
 * switch between them.
 */
import React, {type ReactElement, useCallback, useState} from 'react';
import benchmarks from '@site/src/data/benchmarks.json';
import styles from './styles.module.css';

const BASELINE_LIBRARY = 'Awaiten';
const LIBRARY_ORDER = [
  'Awaiten',
  'MsDI',
  'Autofac',
  'Jab',
  'PureDI',
  'DryIoc',
  'SimpleInjector',
] as const;
const LIBRARY_DISPLAY_NAMES: Record<string, string> = {
  MsDI: 'MS.DI',
  PureDI: 'Pure.DI',
  SimpleInjector: 'Simple Injector',
};

type Sample = {
  timeNs: number;
  memoryBytes?: number;
  history?: {
    timeNs: number[];
    memoryBytes: number[];
  };
};

type Scenario = {
  parameters: string[];
  samples: Record<string, Record<string, Sample>>;
};

type Snapshot = {
  capturedAt: {
    sha: string;
    shortSha: string;
    date: string;
    message?: string;
  };
  environment?: {
    runner?: string;
    toolchain?: string;
  };
  benchmarks: Record<string, Scenario>;
};

const data = benchmarks as Snapshot;

function formatNs(ns: number): string {
  if (ns < 1_000) return `${ns.toFixed(1)} ns`;
  if (ns < 1_000_000) return `${(ns / 1_000).toFixed(2)} µs`;
  return `${(ns / 1_000_000).toFixed(2)} ms`;
}

function formatBytes(b: number): string {
  if (b < 1_024) return `${b} B`;
  if (b < 1_024 * 1_024) return `${(b / 1_024).toFixed(2)} KiB`;
  return `${(b / (1_024 * 1_024)).toFixed(2)} MiB`;
}

function formatRatio(value: number, baseline: number): string {
  if (baseline === 0) return value === 0 ? '1.00x' : '∞';
  return `${(value / baseline).toFixed(2)}x`;
}

const SPARK_WIDTH = 90;
const SPARK_HEIGHT = 16;
const SPARK_PADDING = 2;

type SparklineProps = {
  values: number[] | undefined;
  className: string;
};

function Sparkline({values, className}: SparklineProps): ReactElement | null {
  if (!values || values.length < 2) return null;

  const min = Math.min(...values);
  const max = Math.max(...values);
  const range = max - min;
  const innerHeight = SPARK_HEIGHT - SPARK_PADDING * 2;

  const points = values.map((v, i) => {
    const x = (i / (values.length - 1)) * SPARK_WIDTH;
    const y = range === 0
      ? SPARK_HEIGHT / 2
      : SPARK_PADDING + innerHeight - ((v - min) / range) * innerHeight;
    return [x, y] as const;
  });

  const d = points
    .map(([x, y], i) => `${i === 0 ? 'M' : 'L'}${x.toFixed(2)} ${y.toFixed(2)}`)
    .join(' ');
  const [lastX, lastY] = points[points.length - 1];

  return (
    <svg
      className={className}
      width={SPARK_WIDTH}
      height={SPARK_HEIGHT}
      viewBox={`0 0 ${SPARK_WIDTH} ${SPARK_HEIGHT}`}
      aria-hidden="true">
      <path
        d={d}
        fill="none"
        stroke="currentColor"
        strokeWidth="1.2"
        strokeLinejoin="round"
        strokeLinecap="round"
      />
      <circle cx={lastX} cy={lastY} r="1.6" fill="currentColor" />
    </svg>
  );
}

type LibraryRow = {
  library: string;
  value: number | null;
  history?: number[];
};

type MetricBlockProps = {
  label: string;
  rows: LibraryRow[];
  baselineValue: number;
  format: (n: number) => string;
  inactive: ReadonlySet<string>;
  onToggle: (library: string) => void;
};

function MetricBlock({label, rows, baselineValue, format, inactive, onToggle}: MetricBlockProps): ReactElement {
  // Bars rescale to the max of currently *active* rows so toggling a slow
  // container off lets the faster ones spread out instead of all hugging the
  // left edge.
  const max = Math.max(
    ...rows
      .filter(r => r.value !== null && !inactive.has(r.library))
      .map(r => r.value as number),
    0,
  );

  return (
    <div className={styles.metricBlock}>
      <div className={styles.metricLabel}>{label}</div>
      <div className={styles.bars}>
        {rows.map(row => {
          const isBaseline = row.library === BASELINE_LIBRARY;
          const isMissing = row.value === null;
          const isInactive = !isMissing && inactive.has(row.library);
          const showFill = !isMissing && !isInactive;
          const width = showFill && max !== 0 ? (row.value! / max) * 100 : 0;
          const rowClass = [
            styles.barRow,
            isBaseline && styles.barRowBaseline,
            isMissing && styles.barRowMissing,
            isInactive && styles.barRowInactive,
          ].filter(Boolean).join(' ');
          const display = LIBRARY_DISPLAY_NAMES[row.library] ?? row.library;
          const content = (
            <>
              <span className={styles.libraryLabel}>{display}</span>
              <span className={styles.bar}>
                {showFill && (
                  <span
                    className={isBaseline ? styles.barFillBaseline : styles.barFill}
                    style={{width: `${width}%`}}
                  />
                )}
              </span>
              <span className={styles.barValue}>
                {isMissing ? 'not available' : format(row.value!)}
              </span>
              <span className={styles.ratio}>
                {isMissing ? '—' : isBaseline ? 'baseline' : formatRatio(row.value!, baselineValue)}
              </span>
              {!isMissing && (
                <Sparkline
                  values={row.history}
                  className={isBaseline ? `${styles.sparkline} ${styles.sparklineBaseline}` : styles.sparkline}
                />
              )}
            </>
          );
          if (isMissing) {
            return (
              <div key={row.library} className={rowClass}>
                {content}
              </div>
            );
          }
          return (
            <button
              key={row.library}
              type="button"
              className={rowClass}
              aria-pressed={!isInactive}
              title={isInactive ? `Show ${display} in chart` : `Hide ${display} from chart`}
              onClick={() => onToggle(row.library)}>
              {content}
            </button>
          );
        })}
      </div>
    </div>
  );
}

function buildRows(
  samples: Record<string, Sample>,
  picker: (s: Sample) => number | undefined,
  history: (s: Sample) => number[] | undefined,
): LibraryRow[] {
  // Render every library in LIBRARY_ORDER so absences are visible: a library
  // with no sample for this scenario / metric appears as a "not available"
  // placeholder rather than silently disappearing from the comparison.
  // Present rows sort by value ascending (fastest first); missing rows fall
  // through to the bottom in declared order so the chart's shape stays stable
  // across scenarios.
  const present: LibraryRow[] = [];
  const missing: LibraryRow[] = [];
  for (const library of LIBRARY_ORDER) {
    const sample = samples[library];
    const value = sample ? picker(sample) : undefined;
    if (value === undefined) {
      missing.push({library, value: null});
    } else {
      present.push({library, value, history: sample ? history(sample) : undefined});
    }
  }
  present.sort((a, b) => (a.value as number) - (b.value as number));
  return [...present, ...missing];
}

type Props = {
  name: string;
};

export default function AwaitenBenchmarkResult({name}: Props): ReactElement {
  const scenario = data.benchmarks[name];
  if (!scenario) {
    return (
      <div className={styles.error}>
        No benchmark data for <code>{name}</code>.
      </div>
    );
  }

  const params = scenario.parameters;
  const [activeParam, setActiveParam] = useState(params[0]);
  const [inactive, setInactive] = useState<ReadonlySet<string>>(() => new Set());
  const toggleLibrary = useCallback((library: string) => {
    setInactive(prev => {
      const next = new Set(prev);
      if (next.has(library)) {
        next.delete(library);
      } else {
        next.add(library);
      }
      return next;
    });
  }, []);
  const samples = scenario.samples[activeParam] ?? {};
  const baselineSample = samples[BASELINE_LIBRARY];

  const timeRows = buildRows(samples, s => s.timeNs, s => s.history?.timeNs);
  const memoryRows = buildRows(samples,
    s => s.memoryBytes,
    s => s.history?.memoryBytes);

  const showParamTabs = params.length > 1 || (params.length === 1 && params[0] !== '');

  return (
    <div className={styles.result}>
      {showParamTabs && (
        <div className={styles.paramTabs} role="group" aria-label={`${name} parameter`}>
          {params.map(p => (
            <button
              type="button"
              key={p || '_default'}
              aria-pressed={p === activeParam}
              className={p === activeParam ? `${styles.paramTab} ${styles.paramTabActive}` : styles.paramTab}
              onClick={() => setActiveParam(p)}>
              {p || 'default'}
            </button>
          ))}
        </div>
      )}
      <MetricBlock
        label="Time"
        rows={timeRows}
        baselineValue={baselineSample?.timeNs ?? 0}
        format={formatNs}
        inactive={inactive}
        onToggle={toggleLibrary}
      />
      {memoryRows.length > 0 && baselineSample?.memoryBytes !== undefined && (
        <MetricBlock
          label="Memory"
          rows={memoryRows}
          baselineValue={baselineSample.memoryBytes}
          format={formatBytes}
          inactive={inactive}
          onToggle={toggleLibrary}
        />
      )}
      <div className={styles.caption}>
        Captured on commit{' '}
        <a
          href={`https://github.com/awaiten-dev/Awaiten/commit/${data.capturedAt.sha}`}
          target="_blank"
          rel="noreferrer"
          title={data.capturedAt.message}>
          <code>{data.capturedAt.shortSha}</code>
        </a>{' '}
        ({data.capturedAt.date})
        {data.environment?.runner ? ` on ${data.environment.runner}` : ''}.
      </div>
    </div>
  );
}
