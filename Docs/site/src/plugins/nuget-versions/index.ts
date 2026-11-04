/**
 * Reads the latest stable and pre-release version of each configured package from NuGet at build time and
 * exposes them as global data for the `PackageInstall` component. The libraries trigger a site build after
 * each release, so the versions stay current without a request from the browser.
 */
import type {LoadContext, Plugin} from '@docusaurus/types';

export type PackageVersion = {stable?: string; latest: string};
export type PackageVersions = Record<string, PackageVersion>;

type Options = {packages: string[]};

// A single network hiccup on the build runner should not fail the deployment.
async function fetchWithRetry(url: string, attempts = 3): Promise<Response> {
  for (let attempt = 1; ; attempt++) {
    try {
      return await fetch(url);
    } catch (error) {
      if (attempt >= attempts) {
        throw error;
      }
      await new Promise((resolve) => setTimeout(resolve, attempt * 5000));
    }
  }
}

async function readVersions(id: string): Promise<PackageVersion> {
  const response = await fetchWithRetry(`https://api.nuget.org/v3-flatcontainer/${id.toLowerCase()}/index.json`);
  if (!response.ok) {
    throw new Error(`Could not read the versions of '${id}' from NuGet (HTTP ${response.status}).`);
  }
  const {versions} = (await response.json()) as {versions: string[]};
  return {
    stable: versions.filter((version) => !version.includes('-')).at(-1),
    latest: versions.at(-1)!,
  };
}

export default function nugetVersionsPlugin(_context: LoadContext, options: Options): Plugin<PackageVersions> {
  return {
    name: 'nuget-versions',
    async loadContent() {
      const entries = await Promise.all(
        options.packages.map(async (id) => [id, await readVersions(id)] as const),
      );
      return Object.fromEntries(entries);
    },
    async contentLoaded({content, actions}) {
      actions.setGlobalData(content);
    },
  };
}
