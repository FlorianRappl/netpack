![netpack](./art/logo.svg)

# netpack

netpack is an experiment to see if .NET written tooling can perform on an equal level to tools written in Rust or Go.

Right now netpack is not production ready and the likelihood that it works for your project is low.

If you like the idea and want to see this become a real thing then either support via code contributions or by [sponsoring the project](https://github.com/sponsors/FlorianRappl).

## Performance

![Bundler performance comparison](./docs/comparison.png)

The whole project is mostly geared towards performance. The key idea is that .NET with AoT (Ahead-of-Time compilation, i.e., no runtime dependency) can be on the same level as Go or Rust. While it still contains a GC (garbage collector, i.e., automatic memory management), the startup time is much improved. There is no JIT that is required to be run, which instantly yields good performance.

| Test                | esbuild     | rspack      | Vite        | **netpack** |
| ------------------- | ----------- | ----------- | ----------- | ----------- |
| Small lib           | 326ms       | 611ms       | 601ms       | 359ms       |
| Small project       | 670ms       | 912ms       | 1658ms      | 418ms       |
| Medium project      | 1931ms      | 2877ms      | 10601ms     | 974ms       |
| Large project       | 2189ms      | 2422ms      | 13710ms     | 1357ms      |

Besides performance there are other reasons for choosing C#/.NET. It's arguably more readable than Rust, more powerful than Go, and better performing than JavaScript. The ecosystem, however, is lacking.

Another reason for having *another* bundler (but in .NET) is that it could be used *natively* within the .NET ecosystem, e.g., the post-process or optimize ASP.NET Core and / or Blazor web applications.

<details>
<summary>Benchmark Details</summary>
All benchmarks have been performed using [hyperfine](https://github.com/sharkdp/hyperfine) on Ubuntu 22.04 using an AMD Ryzen 7 3700X 8-Core processor with 32 GB of memory.

The benchmarks used netpack v0.0.1, rspack v1.1.8, esbuild v0.24.0, and Vite v6.0.1.

**Lib project**

*netpack*:

```plain
Benchmark 1: npx netpack bundle src/lib/index.mjs --minify
  Time (mean ± σ):     359.4 ms ±  18.3 ms    [User: 383.4 ms, System: 56.1 ms]
  Range (min … max):   335.4 ms … 386.5 ms    10 runs
```

*esbuild*:

```plain
Benchmark 1: npx esbuild --bundle src/lib/index.mjs --format=esm --outdir=dist
  Time (mean ± σ):     326.3 ms ±  19.4 ms    [User: 361.5 ms, System: 35.9 ms]
  Range (min … max):   305.1 ms … 361.8 ms    10 runs
```

*rspack*:

```plain
Benchmark 1: npx rspack build --config rspack.lib.mjs
  Time (mean ± σ):     611.0 ms ±  27.9 ms    [User: 626.4 ms, System: 109.2 ms]
  Range (min … max):   559.1 ms … 643.7 ms    10 runs
```

*Vite*:

```plain
Benchmark 1: npx vite build
  Time (mean ± σ):     601.8 ms ±  20.5 ms    [User: 603.1 ms, System: 87.5 ms]
  Range (min … max):   573.5 ms … 633.5 ms    10 runs
```

**Small project**

*netpack*:

```plain
Benchmark 1: npx netpack bundle src/small/index.html --minify
  Time (mean ± σ):     418.5 ms ±  30.3 ms    [User: 440.1 ms, System: 71.4 ms]
  Range (min … max):   388.2 ms … 475.7 ms    10 runs
```

*esbuild*:

```plain
Benchmark 1: node esbuild.small.mjs
  Time (mean ± σ):     670.3 ms ±  30.8 ms    [User: 728.2 ms, System: 57.9 ms]
  Range (min … max):   610.1 ms … 711.4 ms    10 runs
```

*rspack*:

```plain
Benchmark 1: npx rspack build --config rspack.small.mjs
  Time (mean ± σ):     912.8 ms ±  39.0 ms    [User: 1067.3 ms, System: 223.6 ms]
  Range (min … max):   866.0 ms … 1002.9 ms    10 runs
```

*Vite*:

```plain
Benchmark 1: npx vite build
  Time (mean ± σ):      1.658 s ±  0.096 s    [User: 2.467 s, System: 0.162 s]
  Range (min … max):    1.495 s …  1.827 s    10 runs
```

**Medium project**

*netpack*:

```plain
Benchmark 1: npx netpack bundle src/medium/index.html --minify
  Time (mean ± σ):     974.9 ms ±  65.2 ms    [User: 1409.9 ms, System: 398.3 ms]
  Range (min … max):   854.2 ms … 1070.1 ms    10 runs
```

*esbuild*:

```plain
Benchmark 1: node esbuild.medium.mjs
  Time (mean ± σ):      1.931 s ±  0.107 s    [User: 1.480 s, System: 0.091 s]
  Range (min … max):    1.779 s …  2.149 s    10 runs
```

*rspack*:

```plain
Benchmark 1: npx rspack build --config rspack.medium.mjs
  Time (mean ± σ):      2.877 s ±  0.117 s    [User: 6.919 s, System: 1.451 s]
  Range (min … max):    2.677 s …  3.025 s    10 runs
```

*Vite*:

```plain
Benchmark 1: npx vite build
  Time (mean ± σ):     10.601 s ±  0.229 s    [User: 13.159 s, System: 2.226 s]
  Range (min … max):   10.219 s … 11.014 s    10 runs
```

**Large project**

*netpack*:

```plain
Benchmark 1: npx netpack bundle src/large/index.html --minify
  Time (mean ± σ):      1.357 s ±  0.043 s    [User: 2.553 s, System: 0.716 s]
  Range (min … max):    1.286 s …  1.433 s    10 runs
```

*esbuild*:

```plain
Benchmark 1: node esbuild.large.mjs
  Time (mean ± σ):      2.189 s ±  0.097 s    [User: 1.391 s, System: 0.091 s]
  Range (min … max):    2.057 s …  2.334 s    10 runs
```

*Vite*:

```plain
Benchmark 1: npx vite build
  Time (mean ± σ):     13.710 s ±  0.236 s    [User: 17.474 s, System: 2.342 s]
  Range (min … max):   13.301 s … 14.237 s    10 runs
```

*rspack*:

```plain
Benchmark 1: npx rspack build --config rspack.large.mjs
  Time (mean ± σ):      2.422 s ±  0.133 s    [User: 8.956 s, System: 1.381 s]
  Range (min … max):    2.283 s …  2.651 s    10 runs
```

</details>

## Overview

The following items are features or topics that are relevant for bundlers - netpack in particular.

### General Features

- [x] Handle JavaScript
- [x] Handle JSX (right now only `React.createElement`)
- [ ] Handle TypeScript
- [x] Handle images
- [x] Handle any asset
- [x] Handle CSS
- [ ] Handle CSS modules
- [ ] Handle SASS
- [x] Handle HTML
- [x] Handle JSON
- [ ] Handle codegen

### Bundler Basics

- [ ] Sourcemaps
- [x] Minification
- [x] DevServer with reload
- [x] Bundle analyzer
- [ ] Image / asset variants (e.g., width/height optimized)
- [x] Copy public assets
- [x] Externals
- [ ] True HMR (not just refresh)

### More Advanced Topics

- [x] Importmap support
- [ ] Module Federation support
- [ ] Native Federation support
- [ ] React Fast Refresh support
- [ ] Platforms (web, npm)
- [ ] Tree shaking
- [ ] Other formats (esm, cjs, systemjs, umd)

## Idea Stash

Integration ideas / explorations:

- [Evaluate SASS from its official lib](https://github.com/Taritsyn/LibSassHost)
- [Port Acornima to support streaming + TypeScript](https://github.com/adams85/acornima/)
- [Integrate Codegen natively via Node.js](https://github.com/FlorianRappl/codegen-js/)
