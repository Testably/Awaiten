/**
 * Writes an `llms.txt` (https://llmstxt.org) that lists every docs page with its title and description in sidebar
 * order, so AI tools can find the right page without crawling the site.
 */
import {writeFile} from 'node:fs/promises';
import path from 'node:path';
import type {LoadContext, Plugin} from '@docusaurus/types';
import type {LoadedContent} from '@docusaurus/plugin-content-docs';

type Page = {title: string; description: string; permalink: string; source: string};

export default function llmsTxtPlugin(context: LoadContext): Plugin {
  let pages: Page[] = [];
  return {
    name: 'llms-txt',
    async allContentLoaded({allContent}) {
      const docs = allContent['docusaurus-plugin-content-docs']?.default as LoadedContent | undefined;
      pages = (docs?.loadedVersions[0].docs ?? [])
        .filter((doc) => !doc.unlisted)
        .map((doc) => ({title: doc.title, description: doc.description, permalink: doc.permalink, source: doc.source}))
        .sort((a, b) => a.source.localeCompare(b.source));
    },
    async postBuild({outDir}) {
      const {url, title, tagline} = context.siteConfig;
      const links = pages.map(
        (page) => `- [${page.title}](${url}${page.permalink})${page.description ? `: ${page.description}` : ''}`,
      );
      await writeFile(path.join(outDir, 'llms.txt'), `# ${title}\n\n> ${tagline}\n\n## Docs\n\n${links.join('\n')}\n`);
    },
  };
}
