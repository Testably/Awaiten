import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Awaiten',
  tagline: 'The async-first, compile-time dependency injection container for .NET.',
  favicon: 'img/logo.png',

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
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
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
        docsRouteBasePath: '/',
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
  ],

  themeConfig: {
    colorMode: {
      respectPrefersColorScheme: true,
    },
    navbar: {
      title: 'Awaiten',
      logo: {
        alt: 'Awaiten logo',
        src: 'img/logo.png',
        width: 32,
        height: 32,
      },
      items: [
        {
          href: 'https://github.com/awaiten-dev/Awaiten',
          label: 'GitHub',
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
