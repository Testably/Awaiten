/**
 * The install section shared by all libraries: tabs for the .NET CLI, a PackageReference and Central Package
 * Management, filled with the latest version from NuGet (see the `nuget-versions` plugin). Pages without a
 * stable release, or with `prerelease`, get the latest pre-release.
 */
import React, {type ReactElement} from 'react';
import {usePluginData} from '@docusaurus/useGlobalData';
import CodeBlock from '@theme/CodeBlock';
import TabItem from '@theme/TabItem';
import Tabs from '@theme/Tabs';
import type {PackageVersions} from '@site/src/plugins/nuget-versions';
import styles from './styles.module.css';

type Props = {
  package: string | string[];
  prerelease?: boolean;
  developmentDependency?: boolean;
};

type ResolvedPackage = {id: string; version: string; prerelease: boolean};

function resolve(ids: string[], versions: PackageVersions, prerelease: boolean): ResolvedPackage[] {
  return ids.map((id) => {
    const found = versions[id];
    if (!found) {
      throw new Error(`Unknown package '${id}': add it to the nuget-versions plugin in docusaurus.config.ts.`);
    }
    const usePrerelease = prerelease || !found.stable;
    return {id, version: usePrerelease ? found.latest : found.stable!, prerelease: usePrerelease};
  });
}

export default function PackageInstall({
  package: packageIds,
  prerelease = false,
  developmentDependency = false,
}: Props): ReactElement {
  const versions = usePluginData('nuget-versions') as PackageVersions;
  const packages = resolve(Array.isArray(packageIds) ? packageIds : [packageIds], versions, prerelease);
  const privateAssets = developmentDependency ? ' PrivateAssets="all"' : '';

  const cli = packages
    .map((p) => `dotnet add package ${p.id}${p.prerelease ? ' --prerelease' : ''}`)
    .join('\n');
  const packageReferences = packages
    .map((p) => `<PackageReference Include="${p.id}" Version="${p.version}"${privateAssets} />`)
    .join('\n');
  const packageVersions = packages
    .map((p) => `<PackageVersion Include="${p.id}" Version="${p.version}" />`)
    .join('\n');
  const centralReferences = packages
    .map((p) => `<PackageReference Include="${p.id}"${privateAssets} />`)
    .join('\n');

  return (
    <>
      <Tabs groupId="install">
        <TabItem value="cli" label=".NET CLI" default>
          <CodeBlock language="bash">{cli}</CodeBlock>
        </TabItem>
        <TabItem value="packagereference" label="PackageReference">
          <CodeBlock language="xml">{packageReferences}</CodeBlock>
        </TabItem>
        <TabItem value="cpm" label="Central Package Management">
          <CodeBlock language="xml" title="Directory.Packages.props">{packageVersions}</CodeBlock>
          <CodeBlock language="xml" title="Project file">{centralReferences}</CodeBlock>
        </TabItem>
      </Tabs>
      <p className={styles.versions}>
        Latest version:{' '}
        {packages.map((p, index) => (
          <React.Fragment key={p.id}>
            {index > 0 && ', '}
            <a href={`https://www.nuget.org/packages/${p.id}/${p.version}`}>
              {packages.length > 1 ? `${p.id} ${p.version}` : p.version}
            </a>
          </React.Fragment>
        ))}
      </p>
    </>
  );
}
