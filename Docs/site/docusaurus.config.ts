import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Awaiten',
  tagline: 'The async-first, compile-time dependency injection container for .NET.',
  favicon: 'img/favicon.svg',

  future: {
    v4: true,
  },

  url: 'https://awaiten-dev.github.io',
  baseUrl: '/Awaiten/',

  organizationName: 'awaiten-dev',
  projectName: 'Awaiten',

  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',
  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  presets: [
    [
      'classic',
      {
        docs: {
          path: '../pages',
          routeBasePath: 'docs',
          sidebarPath: './sidebars.ts',
          sidebarItemsGenerator: async ({defaultSidebarItemsGenerator, ...args}) =>
            (await defaultSidebarItemsGenerator(args)).filter(
              (item) => !(item.type === 'doc' && ['benchmarks', 'changelog'].includes(item.id)),
            ),
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        docsRouteBasePath: 'docs',
        docsDir: '../pages',
        indexBlog: false,
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
      },
    ],
  ],

  plugins: [
    [
      './src/plugins/nuget-versions/index.ts',
      {packages: ['Awaiten', 'Awaiten.Extensions.DependencyInjection']},
    ],
    './src/plugins/llms-txt/index.ts',
    [
      '@docusaurus/plugin-client-redirects',
      {
        redirects: [{from: '/docs', to: '/docs/getting-started'}],
        createRedirects: (path: string) =>
          path.startsWith('/docs/') && path.length > '/docs/'.length ? [path.slice('/docs'.length)] : undefined,
      },
    ],
  ],

  themeConfig: {
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Awaiten',
      logo: {
        alt: 'Awaiten logo',
        src: 'img/logo.svg',
        srcDark: 'img/logo-dark.svg',
        width: 32,
        height: 32,
      },
      items: [
        {
          to: '/docs/getting-started',
          label: 'Docs',
          position: 'left',
          activeBaseRegex: '/docs/(?!benchmarks|changelog)',
        },
        {
          to: '/docs/benchmarks',
          label: 'Benchmarks',
          position: 'left',
        },
        {
          to: '/docs/changelog',
          label: 'Changelog',
          position: 'left',
        },
        {
          href: 'https://github.com/awaiten-dev/Awaiten',
          label: 'GitHub',
          position: 'right',
        },
        {
          href: 'https://github.com/sponsors/vbreuss',
          label: '❤️ Sponsor',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      copyright: `Copyright © ${new Date().getFullYear()} Awaiten contributors.`,
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'powershell', 'bash'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
