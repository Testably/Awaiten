/**
 * Renders the latest Awaiten-vs-other-DI-containers snapshot: one picker over every scenario and container size, a
 * headline number, time and memory charts and a scoreboard with versions and the trend over the recent runs. Reads
 * from `src/data/benchmarks.json`, which the `DocsData` build target refreshes before each deployment from the
 * `benchmarks` branch. The committed file doubles as a fallback so `npm start`/`npm run build` work in a clean
 * checkout.
 */
import React, {type ReactElement, useState} from 'react';
import benchmarks from '@site/src/data/benchmarks.json';
import styles from './styles.module.css';

const BASELINE_LIBRARY = 'Awaiten';
const LIBRARY_ORDER = ['Awaiten', 'MsDI', 'Autofac', 'DryIoc', 'SimpleInjector'] as const;
const LIBRARY_DISPLAY_NAMES: Record<string, string> = {
  MsDI: 'MS.DI',
  SimpleInjector: 'Simple Injector',
};
const SOURCE_URL = 'https://github.com/awaiten-dev/Awaiten/blob/main/Benchmarks/Awaiten.Benchmarks';

type ScenarioInfo = {label: string; unit: string; source: string; describe: (size?: string) => string};

const SCENARIOS: Record<string, ScenarioInfo> = {
  Realistic: {
    label: 'Realistic request',
    unit: 'per request',
    source: 'RealisticResolveBenchmarks.cs',
    describe: () =>
      'A per-request pattern, the shape a web app actually runs: open a scope, resolve a deep graph of mixed lifetimes rooted at a request handler, then dispose the scope.',
  },
  Resolve: {
    label: 'Resolve',
    unit: 'per resolve',
    source: 'ResolveBenchmarks.cs',
    describe: size =>
      `Steady-state resolution of a service by type once the container is built, in a container with ${size} registrations.`,
  },
  Build: {
    label: 'Build',
    unit: 'to build the container',
    source: 'BuildBenchmarks.cs',
    describe: size => `Container construction from cold, over a graph of ${size} singletons.`,
  },
};
const SCENARIO_ORDER = ['Realistic', 'Resolve', 'Build'];

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
  versions?: Record<string, string>;
};

const data = benchmarks as Snapshot;

type Metric = 'timeNs' | 'memoryBytes';

/**
 * Worded as "1.10× as long" rather than "1.10× slower", which could just as well be read as 110% slower.
 */
const WORDING: Record<Metric, {verb: string; amount: string}> = {
  timeNs: {verb: 'takes', amount: 'as long'},
  memoryBytes: {verb: 'allocates', amount: 'as much'},
};

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

const FORMAT: Record<Metric, (n: number) => string> = {timeNs: formatNs, memoryBytes: formatBytes};

function formatFactor(factor: number): string {
  if (factor < 10) return `${factor.toFixed(1)}×`;
  return `${Math.round(factor).toLocaleString('en-US')}×`;
}

/**
 * A multiple of Awaiten, or the absolute difference when Awaiten allocates nothing and a multiple has no meaning.
 */
function compare(baseline: number, value: number, metric: Metric, name: string): {text: string; title: string} {
  const {verb, amount} = WORDING[metric];
  if (baseline === value) return {text: 'same', title: `${name} ${verb} ${amount} as Awaiten`};
  if (baseline === 0) {
    return {text: `+${FORMAT[metric](value)}`, title: `${name} ${verb} ${FORMAT[metric](value)}, Awaiten nothing`};
  }
  const factor = value / baseline;
  return {text: formatFactor(factor), title: `${name} ${verb} ${factor.toFixed(2)}× ${amount} as Awaiten`};
}

/**
 * The ratio of a container to Awaiten for each run, aligned from the newest run, because a container that was
 * added later has a shorter history. A ratio of the same run does not depend on the speed of the runner.
 */
function ratioHistory(baseline: number[] | undefined, other: number[] | undefined): number[] {
  if (!baseline || !other) return [];
  const count = Math.min(baseline.length, other.length);
  return Array.from({length: count}, (_, i) => {
    const baselineValue = baseline[baseline.length - count + i];
    const otherValue = other[other.length - count + i];
    return baselineValue === otherValue ? 1 : otherValue / baselineValue;
  }).filter(Number.isFinite);
}

const SPARK_WIDTH = 96;
const SPARK_HEIGHT = 20;
const SPARK_PADDING = 3;

function Sparkline({values, title}: {values: number[]; title: string}): ReactElement | null {
  if (values.length < 2) return null;

  // Parity is always in range, so a stable ratio stays flat instead of stretching noise to full height.
  const min = Math.min(1, ...values);
  const max = Math.max(1, ...values);
  const range = max - min;
  const innerHeight = SPARK_HEIGHT - SPARK_PADDING * 2;
  const toY = (v: number): number =>
    range === 0 ? SPARK_HEIGHT / 2 : SPARK_PADDING + innerHeight - ((v - min) / range) * innerHeight;
  const points = values.map((v, i) => [(i / (values.length - 1)) * SPARK_WIDTH, toY(v)] as const);
  const d = points.map(([x, y], i) => `${i === 0 ? 'M' : 'L'}${x.toFixed(2)} ${y.toFixed(2)}`).join(' ');
  const [lastX, lastY] = points[points.length - 1];

  return (
    <svg className={styles.sparkline} width={SPARK_WIDTH} height={SPARK_HEIGHT} viewBox={`0 0 ${SPARK_WIDTH} ${SPARK_HEIGHT}`} role="img" aria-label={title}>
      <title>{title}</title>
      <line x1={0} y1={toY(1)} x2={SPARK_WIDTH} y2={toY(1)} stroke="currentColor" strokeOpacity="0.4" strokeWidth="1" strokeDasharray="3 3" />
      <path d={d} fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinejoin="round" strokeLinecap="round" />
      <circle cx={lastX} cy={lastY} r="2" fill="currentColor" />
    </svg>
  );
}

type Row = {library: string; name: string; version?: string; sample: Sample};

function displayName(library: string): string {
  return LIBRARY_DISPLAY_NAMES[library] ?? library;
}

function Chart({title, metric, rows, baseline}: {title: string; metric: Metric; rows: Row[]; baseline: Sample}): ReactElement | null {
  const values = rows.map(row => row.sample[metric]);
  if (values.some(v => v === undefined)) return null;
  const positive = (values as number[]).filter(v => v > 0);
  // Bars beyond four times the smallest value are cut, so one slow container does not flatten all the others.
  const cap = positive.length > 0 ? Math.min(...positive) * 4 : 1;
  const sorted = [...rows].sort((a, b) => a.sample[metric]! - b.sample[metric]!);

  return (
    <div className={styles.chart}>
      <div className={styles.chartHeader}>
        <h3>{title}</h3>
        <span>lower is better</span>
      </div>
      <div className={styles.chartRows}>
        {sorted.map(row => {
          const value = row.sample[metric]!;
          const isBaseline = row.library === BASELINE_LIBRARY;
          const cut = value > cap;
          const width = cut ? 100 : (value / cap) * 100;
          const comparison = compare(baseline[metric]!, value, metric, row.name);
          return (
            <div key={row.library} className={isBaseline ? `${styles.chartRow} ${styles.chartRowBaseline}` : styles.chartRow} title={isBaseline ? `Awaiten: ${FORMAT[metric](value)}` : comparison.title}>
              <span className={styles.chartName}>{row.name}</span>
              <span className={styles.track}>
                {value > 0 && (
                  <span className={cut ? `${styles.bar} ${styles.barCut}` : styles.bar} style={{width: `${width}%`}} />
                )}
              </span>
              <span className={styles.chartValue}>{FORMAT[metric](value)}</span>
              <span className={styles.chartFactor}>{isBaseline ? '' : comparison.text}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function Headline({rows, baseline, info}: {rows: Row[]; baseline: Sample; info: ScenarioInfo}): ReactElement {
  const others = rows.filter(row => row.library !== BASELINE_LIBRARY).sort((a, b) => a.sample.timeNs - b.sample.timeNs);
  const fastest = others[0];
  const msdi = others.find(row => row.library === 'MsDI');
  const factor = (row: Row) => formatFactor(row.sample.timeNs / baseline.timeNs);

  let sentence: ReactElement;
  if (!fastest) {
    sentence = <>No other container was measured in this scenario.</>;
  } else if (fastest.sample.timeNs < baseline.timeNs) {
    sentence = (
      <>
        {fastest.name} is the fastest here at {formatNs(fastest.sample.timeNs)}; Awaiten takes{' '}
        <strong>{formatFactor(baseline.timeNs / fastest.sample.timeNs)}</strong> as long.
      </>
    );
  } else if (fastest.sample.timeNs === baseline.timeNs) {
    sentence = <>{fastest.name} is as fast as Awaiten here.</>;
  } else {
    sentence = (
      <>
        The next fastest container, {fastest.name}, takes <strong>{factor(fastest)}</strong> as long
        {msdi && msdi !== fastest && (
          <>
            , and MS.DI takes <strong>{factor(msdi)}</strong> as long
          </>
        )}
        .
      </>
    );
  }

  return (
    <div className={styles.headline}>
      <div className={styles.headlineNumber}>
        <span>{formatNs(baseline.timeNs)}</span>
        <span>{info.unit} with Awaiten</span>
      </div>
      <p>{sentence}</p>
    </div>
  );
}

function Scoreboard({rows, baseline}: {rows: Row[]; baseline: Sample}): ReactElement {
  const sorted = [...rows].sort((a, b) => a.sample.timeNs - b.sample.timeNs);
  const hasMemory = baseline.memoryBytes !== undefined;
  return (
    <div className={styles.tableWrapper}>
      <table className={styles.table}>
        <thead>
          <tr>
            <th>Container</th>
            <th className={styles.number}>Time</th>
            <th>vs Awaiten</th>
            {hasMemory && <th className={styles.number}>Memory</th>}
            {hasMemory && <th>vs Awaiten</th>}
            <th>Time vs Awaiten, recent runs</th>
          </tr>
        </thead>
        <tbody>
          {sorted.map(row => {
            const isBaseline = row.library === BASELINE_LIBRARY;
            const time = compare(baseline.timeNs, row.sample.timeNs, 'timeNs', row.name);
            const memory =
              hasMemory && row.sample.memoryBytes !== undefined
                ? compare(baseline.memoryBytes!, row.sample.memoryBytes, 'memoryBytes', row.name)
                : undefined;
            const ratios = ratioHistory(baseline.history?.timeNs, row.sample.history?.timeNs);
            return (
              <tr key={row.library} className={isBaseline ? styles.baselineRow : undefined}>
                <td>
                  <span className={styles.tableName}>{row.name}</span>
                  {row.version && <span className={styles.version}>{row.version}</span>}
                </td>
                <td className={styles.number}>{formatNs(row.sample.timeNs)}</td>
                <td title={isBaseline ? undefined : time.title}>{isBaseline ? <span className={styles.baselineTag}>baseline</span> : time.text}</td>
                {hasMemory && (
                  <td className={styles.number}>
                    {row.sample.memoryBytes === undefined ? '—' : formatBytes(row.sample.memoryBytes)}
                  </td>
                )}
                {hasMemory && (
                  <td title={isBaseline ? undefined : memory?.title}>
                    {isBaseline ? <span className={styles.baselineTag}>baseline</span> : (memory?.text ?? '—')}
                  </td>
                )}
                <td>
                  {!isBaseline && (
                    <Sparkline values={ratios} title={`${row.name} compared to Awaiten over the last ${ratios.length} runs; the dashed line marks equal time`} />
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

function sizeOf(parameter: string): string | undefined {
  return parameter.split('=')[1];
}

export default function AwaitenBenchmarkResult(): ReactElement {
  const scenarios = SCENARIO_ORDER.filter(name => data.benchmarks[name]);
  const [scenario, setScenario] = useState(scenarios[0]);
  const [preferredParameter, setPreferredParameter] = useState<string>();
  if (!scenario) {
    return <div className={styles.error}>No benchmark data.</div>;
  }

  const info = SCENARIOS[scenario];
  const parameters = data.benchmarks[scenario].parameters;
  // The chosen size carries over between Resolve and Build, which are measured at the same sizes.
  const parameter =
    preferredParameter !== undefined && parameters.includes(preferredParameter) ? preferredParameter : parameters[0];
  const size = sizeOf(parameter);
  const samples = data.benchmarks[scenario].samples[parameter] ?? {};
  const baseline = samples[BASELINE_LIBRARY];
  const rows: Row[] = LIBRARY_ORDER.filter(library => samples[library]).map(library => ({
    library,
    name: displayName(library),
    version: data.versions?.[library],
    sample: samples[library],
  }));

  return (
    <div className={styles.result}>
      <div className={styles.picker} role="group" aria-label="Scenario">
        {scenarios.map(name => (
          <button
            key={name}
            type="button"
            aria-pressed={name === scenario}
            className={name === scenario ? `${styles.chip} ${styles.chipActive}` : styles.chip}
            onClick={() => setScenario(name)}>
            {SCENARIOS[name].label}
          </button>
        ))}
      </div>
      <p className={styles.description}>{info.describe(size)}</p>
      {parameters.length > 1 && (
        <div className={styles.sizes} role="group" aria-label="Container size">
          {parameters.map(p => (
            <button
              key={p}
              type="button"
              aria-pressed={p === parameter}
              className={p === parameter ? `${styles.size} ${styles.sizeActive}` : styles.size}
              onClick={() => setPreferredParameter(p)}>
              {sizeOf(p) ? `${sizeOf(p)} services` : p}
            </button>
          ))}
        </div>
      )}

      {baseline ? (
        <>
          <Headline rows={rows} baseline={baseline} info={info} />
          <div className={styles.charts}>
            <Chart title="Time" metric="timeNs" rows={rows} baseline={baseline} />
            <Chart title="Memory" metric="memoryBytes" rows={rows} baseline={baseline} />
          </div>
          <p className={styles.note}>
            Bars longer than four times the fastest container are cut so the others stay comparable. Hover a bar or
            a multiple for the exact comparison.
          </p>
          <Scoreboard rows={rows} baseline={baseline} />
        </>
      ) : (
        <div className={styles.error}>No Awaiten measurement for this scenario.</div>
      )}

      <div className={styles.caption}>
        <span>
          Measured on {data.capturedAt.date} at commit{' '}
          <a href={`https://github.com/awaiten-dev/Awaiten/commit/${data.capturedAt.sha}`} title={data.capturedAt.message}>
            <code>{data.capturedAt.shortSha}</code>
          </a>
        </span>
        {data.environment?.toolchain && <span>{data.environment.toolchain}</span>}
        {data.environment?.runner && <span>{data.environment.runner}</span>}
        <a href={`${SOURCE_URL}/${info.source}`}>View the benchmark source on GitHub →</a>
      </div>
    </div>
  );
}
