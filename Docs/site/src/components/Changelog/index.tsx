/**
 * The changelog as a timeline, filled from `src/data/changelog.json`, which the `DocsData` build target generates from
 * the GitHub releases. Each major version is a chapter with a summary; the releases of the latest major version are
 * shown, older ones on demand.
 */
import React, {type ReactElement, type ReactNode, useState} from 'react';
import clsx from 'clsx';
import styles from './styles.module.css';

type Category = 'breaking' | 'feature' | 'fix' | 'performance';

type ChangelogItem = {category: Category; text: string; number: number; url: string};

type ChangelogRelease = {
  version: string;
  date: string;
  prerelease: boolean;
  url: string;
  commits: number;
  items: ChangelogItem[];
};

type Props = {
  changelog: {releases: ChangelogRelease[]};
};

type Summary = {commits: number; counts: Record<Category, number>};

const categories: {category: Category; label: string; one: string; many: string}[] = [
  {category: 'breaking', label: 'Breaking changes', one: 'breaking change', many: 'breaking changes'},
  {category: 'feature', label: 'Features', one: 'feature', many: 'features'},
  {category: 'fix', label: 'Fixes', one: 'fix', many: 'fixes'},
  {category: 'performance', label: 'Performance', one: 'performance improvement', many: 'performance improvements'},
];

// Some tags name a package before the version (e.g. `core/v0.19.0`).
const majorOf = (release: ChangelogRelease): number => Number.parseInt(release.version.match(/\d+/)?.[0] ?? '0', 10);

const formatDate = (date: string, options: Intl.DateTimeFormatOptions): string =>
  new Date(`${date}T12:00:00Z`).toLocaleDateString('en', {...options, timeZone: 'UTC'});

const plural = (count: number, one: string, many: string): string =>
  `${count.toLocaleString('en')} ${count === 1 ? one : many}`;

function renderText(text: string): ReactNode[] {
  return text.split('`').map((segment, index) => (index % 2 === 1 ? <code key={index}>{segment}</code> : segment));
}

function summarize(releases: ChangelogRelease[]): Summary {
  const counts: Record<Category, number> = {breaking: 0, feature: 0, fix: 0, performance: 0};
  let commits = 0;
  for (const release of releases) {
    commits += release.commits;
    for (const item of release.items) {
      counts[item.category]++;
    }
  }
  return {commits, counts};
}

function Bar({summary, thick}: {summary: Summary; thick?: boolean}): ReactElement {
  const present = categories.filter(({category}) => summary.counts[category] > 0);
  const total = present.reduce((sum, {category}) => sum + summary.counts[category], 0);
  return (
    <div
      className={clsx(styles.bar, thick && styles.thick)}
      role="img"
      aria-label={present.map(({category, many}) => `${summary.counts[category]} ${many}`).join(', ')}>
      {present.map(({category}) => (
        <span key={category} className={styles[category]} style={{width: `${(100 * summary.counts[category]) / total}%`}} />
      ))}
    </div>
  );
}

function Stats({summary}: {summary: Summary}): ReactElement {
  return (
    <div className={styles.stats}>
      <span>
        <b>{summary.commits.toLocaleString('en')}</b> {summary.commits === 1 ? 'commit' : 'commits'}
      </span>
      {categories
        .filter(({category}) => summary.counts[category] > 0)
        .map(({category, one, many}) => (
          <span key={category} className={clsx(category === 'breaking' && styles.breakingText)}>
            <b>{summary.counts[category].toLocaleString('en')}</b> {summary.counts[category] === 1 ? one : many}
          </span>
        ))}
    </div>
  );
}

function Release({release, latest}: {release: ChangelogRelease; latest: boolean}): ReactElement {
  const summary = summarize([release]);
  return (
    <>
      <div className={styles.when}>
        <b>{formatDate(release.date, {month: 'short', day: 'numeric'})}</b> {release.date.substring(0, 4)}
      </div>
      <div className={styles.body}>
        <span className={clsx(styles.dot, summary.counts.breaking > 0 && styles.breakingDot)} aria-hidden="true" />
        <div className={styles.head}>
          <a className={styles.version} href={release.url}>{release.version}</a>
          {latest && <span className={clsx(styles.badge, styles.latest)}>Latest</span>}
          {release.prerelease && <span className={styles.badge}>Pre-release</span>}
          {summary.counts.breaking > 0 && (
            <span className={clsx(styles.badge, styles.breakingBadge)}>{summary.counts.breaking} breaking</span>
          )}
        </div>
        <Bar summary={summary} />
        <Stats summary={summary} />
        <details className={styles.changes} open={latest}>
          <summary>{plural(release.items.length, 'change', 'changes')}</summary>
          {categories.map(({category, label}) => {
            const items = release.items.filter((item) => item.category === category);
            return items.length === 0 ? null : (
              <div key={category}>
                <div className={clsx(styles.group, styles[`${category}Text`])}>{label} · {items.length}</div>
                <ul className={styles.items}>
                  {items.map((item) => (
                    <li key={item.number}>
                      {renderText(item.text)} <a href={item.url}>#{item.number}</a>
                    </li>
                  ))}
                </ul>
              </div>
            );
          })}
        </details>
      </div>
    </>
  );
}

export default function Changelog({changelog: {releases}}: Props): ReactElement {
  const [expanded, setExpanded] = useState<number[]>([]);
  const majors = [...new Set(releases.map(majorOf))];

  return (
    <>
      <div className={styles.legend}>
        {categories.map(({category, label}) => (
          <span key={category} className={styles[`${category}Text`]}>{label}</span>
        ))}
      </div>
      <div className={styles.timeline}>
        {majors.map((major, index) => {
          const majorReleases = releases.filter((release) => majorOf(release) === major);
          const summary = summarize(majorReleases);
          const first = majorReleases[majorReleases.length - 1];
          const last = majorReleases[0];
          const from = formatDate(first.date, {month: 'short', year: 'numeric'});
          const to = formatDate(last.date, {month: 'short', year: 'numeric'});
          return (
            <React.Fragment key={major}>
              <div className={clsx(styles.when, styles.chapterWhen)}>{last.date.substring(0, 4)}</div>
              <div className={clsx(styles.body, styles.chapter)}>
                <span className={styles.medal} aria-hidden="true">v{major}</span>
                <h2 id={`version-${major}`}>Version {major}</h2>
                <p className={styles.span}>
                  {plural(majorReleases.length, 'release', 'releases')} · {from === to ? from : `${from} to ${to}`}
                </p>
                <Bar summary={summary} thick />
                <Stats summary={summary} />
              </div>
              {index === 0 || expanded.includes(major) ? (
                majorReleases.map((release) => (
                  <Release key={release.version} release={release} latest={release === releases[0]} />
                ))
              ) : (
                <>
                  <div className={styles.when} />
                  <div className={clsx(styles.body, styles.fold)}>
                    <button type="button" className={styles.show} onClick={() => setExpanded([...expanded, major])}>
                      Show {plural(majorReleases.length, 'release', 'releases')} of version {major}
                    </button>
                  </div>
                </>
              )}
            </React.Fragment>
          );
        })}
      </div>
    </>
  );
}
