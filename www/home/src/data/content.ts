/**
 * All copy, numbers and links for the netpack landing page live here.
 * Edit this file to update the page — components just render it.
 */

export type IconVariant =
  | 'square'
  | 'circle-dot'
  | 'overlap-circles'
  | 'diamond'
  | 'gradient-dash'
  | 'ring-square';

export interface FeatureCard {
  icon: IconVariant;
  title: string;
  description: string;
}

export interface StatCard {
  icon: IconVariant;
  title: string;
  subtitle: string;
}

export interface BenchmarkEntry {
  name: string;
  time: string;
  /** Bar fill width, 0-100. */
  widthPercent: number;
  highlighted?: boolean;
}

export const site = {
  title: 'netpack — the fast, batteries-included bundler for the web',
  description:
    'Built on C#/.NET with Ahead-of-Time compilation. No JIT to warm up, no runtime to install — netpack starts at native speed and stays there.',
  repoUrl: 'https://github.com/FlorianRappl/netpack',
  repoLabel: 'github.com/FlorianRappl/netpack',
  // The docs site (www/docs) is deployed to the /docs subpath of this same
  // domain — see the "pages" job in .github/workflows/publish.yml.
  docsUrl: '/docs/',
};

export const terminal = {
  windowTitle: '~/netpack — zsh',
};

export const badge = {
  label: 'PRE-1.0 · EXPERIMENTAL BUILD',
};

export const hero = {
  headline: 'the fast, batteries-included bundler for the web',
  subtext:
    'Built on C#/.NET with Ahead-of-Time compilation. No JIT to warm up, no runtime to install — netpack starts at native speed and stays there.',
  installCommand: 'npm i -D netpack && npx netpack bundle .',
  installCommandPrompt: '$',
  installCommandJoiner: '&&',
  sourceLinkLabel: 'view source →',
};

export const benchmark = {
  commandLine: "$ hyperfine --warmup 3 'netpack' 'esbuild' 'rspack' 'vite'",
  scenario: 'small project · cold build',
  footnote: 'mean of 10 runs · npx netpack bundle src/small/index.html --minify',
  entries: [
    { name: 'netpack', time: '418ms', widthPercent: 25, highlighted: true },
    { name: 'esbuild', time: '670ms', widthPercent: 40 },
    { name: 'rspack', time: '913ms', widthPercent: 55 },
    { name: 'vite', time: '1.66s', widthPercent: 100 },
  ] satisfies BenchmarkEntry[],
};

export const diagram = {
  inputs: ['.js', '.ts', '.css', '.png', '.html'],
  outputs: ['bundle.js', 'styles.css', 'index.html', 'assets/'],
  caption: 'one graph in, one optimized output — resolved, transformed and written in a single pass',
};

export const featuresSection = {
  commandLine: '$ cat FEATURES.md',
  cards: [
    {
      icon: 'square',
      title: 'zero-config',
      description: 'Same entry point as Vite or Parcel — an index.html, or a JS file directly.',
    },
    {
      icon: 'circle-dot',
      title: 'AoT compiled',
      description: 'No JIT warmup, no runtime to install. Native startup, every time.',
    },
    {
      icon: 'overlap-circles',
      title: 'auto import maps',
      description: 'Importmap entries become externals automatically; shared deps get wired in for you.',
    },
    {
      icon: 'diamond',
      title: 'watch mode',
      description: 'Watches your filesystem, rebuilds, reloads the browser — no config needed.',
    },
    {
      icon: 'gradient-dash',
      title: 'npm-native',
      description: 'Install through npm, pnpm or yarn like any other bundler in your toolchain.',
    },
    {
      icon: 'ring-square',
      title: '.NET native',
      description: 'Drop it into an ASP.NET pipeline as a post-process or asset optimizer step.',
    },
  ] satisfies FeatureCard[],
};

export const whyDotnet = {
  commandLine: '$ man why-dotnet',
  paragraphs: [
    'Most bundlers reach for Rust or Go once JavaScript gets too slow. netpack bets on C#/.NET instead: with Ahead-of-Time compilation, the CLI starts at native speed with no runtime install and no JIT to warm up.',
    "The payoff is a language that reads easier than Rust and does more than Go, while still outrunning plain JavaScript tooling — even with a garbage collector still in the loop.",
  ],
  stats: [
    { icon: 'overlap-circles', title: 'huge ecosystem', subtitle: 'NuGet + npm, both reachable' },
    { icon: 'circle-dot', title: 'dev productivity', subtitle: 'reads cleaner than Rust' },
    { icon: 'ring-square', title: 'memory safety', subtitle: "managed, GC'd runtime" },
    { icon: 'diamond', title: 'near-native speed', subtitle: 'AoT, no JIT to warm up' },
  ] satisfies StatCard[],
};

export const footer = {
  tagline: "netpack — MIT licensed — early and evolving, star it on GitHub",
};
