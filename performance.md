# Performance Evaluation

## Standard Comparison

Summary:

| Bundler     |  Version  |        Large |       Medium |        Small |      Library |
| :---------- | :-------: | -----------: | -----------: | -----------: | -----------: |
| **NetPack** | **0.5.2** | **797.0 ms** | **762.7 ms** | **219.1 ms** |     152.1 ms |
| esbuild     |   0.28.1  |     894.8 ms |     818.6 ms |     238.0 ms | **149.9 ms** |
| rspack      |   1.1.8   |     972.0 ms |     961.0 ms |     328.7 ms |     220.0 ms |
| Vite        |   6.0.1   |    2807.0 ms |    2247.0 ms |     467.3 ms |     216.2 ms |
| rspack      | **2.1.5** | **450.0 ms** | **512.7 ms** | **274.9 ms** |     171.1 ms |
| Vite        | **8.1.5** |     452.5 ms |     550.8 ms |     294.1 ms |     200.0 ms |

Interpretation:

| Rank | Bundler        | Overall observation                                               |
| ---: | :------------- | :---------------------------------------------------------------- |
|   🥇 | rspack 2.1.5   | Fastest on large and medium projects.                             |
|   🥈 | NetPack 0.5.2  | Best on small projects and nearly ties esbuild on library builds. |
|   🥉 | esbuild 0.28.1 | Very competitive overall; fastest library bundling.               |
|    4 | Vite 8.1.5     | Huge improvement over Vite 6, approaching rspack performance.     |
|    5 | rspack 1.1.8   | Noticeably slower than v2.                                        |
|    6 | Vite 6.0.1     | Significantly slower than the newer implementations.              |

### NetPack v0.5.2

Benchmark 1: npx netpack bundle src/large/index.html --minify
  Time (mean ± σ):     797.0 ms ±  79.8 ms    [User: 947.0 ms, System: 2526.9 ms]
  Range (min … max):   734.7 ms … 1013.7 ms    10 runs
 
Benchmark 1: npx netpack bundle src/medium/index.html --minify
  Time (mean ± σ):     762.7 ms ± 209.3 ms    [User: 816.4 ms, System: 426.6 ms]
  Range (min … max):   684.9 ms … 1357.7 ms    10 runs
 
  Warning: The first benchmarking run for this command was significantly slower than the rest (1.358 s). This could be caused by (filesystem) caches that were not filled until after the first run. You should consider using the '--warmup' option to fill those caches before the actual benchmark. Alternatively, use the '--prepare' option to clear the caches before each timing run.
 
Benchmark 1: npx netpack bundle src/small/index.html --minify
  Time (mean ± σ):     219.1 ms ±   0.9 ms    [User: 148.7 ms, System: 56.6 ms]
  Range (min … max):   217.9 ms … 220.4 ms    13 runs
 
Benchmark 1: npx netpack bundle src/lib/index.mjs --minify
  Time (mean ± σ):     152.1 ms ±   2.3 ms    [User: 120.3 ms, System: 47.9 ms]
  Range (min … max):   146.8 ms … 155.2 ms    19 runs

### esbuild v0.28.1

Benchmark 1: node esbuild.large.mjs
  Time (mean ± σ):     894.8 ms ± 212.9 ms    [User: 424.0 ms, System: 46.1 ms]
  Range (min … max):   812.2 ms … 1500.1 ms    10 runs
 
Benchmark 1: node esbuild.medium.mjs
  Time (mean ± σ):     818.6 ms ±  18.2 ms    [User: 1161.3 ms, System: 753.0 ms]
  Range (min … max):   802.0 ms … 864.3 ms    10 runs
 
Benchmark 1: node esbuild.small.mjs
  Time (mean ± σ):     238.0 ms ±   3.8 ms    [User: 277.8 ms, System: 53.0 ms]
  Range (min … max):   234.6 ms … 249.2 ms    12 runs
 
Benchmark 1: npx esbuild --bundle src/lib/index.mjs --format=esm --outdir=dist
  Time (mean ± σ):     149.9 ms ±  50.6 ms    [User: 101.6 ms, System: 47.6 ms]
  Range (min … max):   130.3 ms … 293.7 ms    10 runs

### rspack v1.1.8

Benchmark 1: npx rspack build --config rspack.large.mjs
  Time (mean ± σ):     972.0 ms ± 500.7 ms    [User: 2469.5 ms, System: 1492.2 ms]
  Range (min … max):   786.9 ms … 2395.5 ms    10 runs
 
Benchmark 1: npx rspack build --config rspack.medium.mjs
  Time (mean ± σ):     961.0 ms ± 147.8 ms    [User: 1866.5 ms, System: 1957.1 ms]
  Range (min … max):   893.2 ms … 1380.5 ms    10 runs
 
Benchmark 1: npx rspack build --config rspack.small.mjs
  Time (mean ± σ):     328.7 ms ±   3.8 ms    [User: 344.2 ms, System: 142.3 ms]
  Range (min … max):   324.7 ms … 338.5 ms    10 runs
 
Benchmark 1: npx rspack build --config rspack.lib.mjs
  Time (mean ± σ):     220.0 ms ±   3.8 ms    [User: 204.6 ms, System: 68.5 ms]
  Range (min … max):   213.9 ms … 225.2 ms    13 runs

### Vite v6.0.1

Benchmark 1: npx vite build
  Time (mean ± σ):      2.807 s ±  0.084 s    [User: 7.455 s, System: 2.381 s]
  Range (min … max):    2.721 s …  3.010 s    10 runs
 
Benchmark 1: npx vite build
  Time (mean ± σ):      2.247 s ±  0.066 s    [User: 6.426 s, System: 2.520 s]
  Range (min … max):    2.172 s …  2.381 s    10 runs
 
Benchmark 1: npx vite build
  Time (mean ± σ):     467.3 ms ±  12.1 ms    [User: 659.4 ms, System: 126.3 ms]
  Range (min … max):   454.0 ms … 490.7 ms    10 runs
 
Benchmark 1: npx vite build
  Time (mean ± σ):     216.2 ms ±   7.1 ms    [User: 198.2 ms, System: 58.0 ms]
  Range (min … max):   209.1 ms … 231.1 ms    13 runs

### rspack v2.1.5

Benchmark 1: npx rspack build --config rspack.large.mjs
  Time (mean ± σ):     450.0 ms ±  17.4 ms    [User: 1578.6 ms, System: 952.9 ms]
  Range (min … max):   430.8 ms … 482.3 ms    10 runs
 
Benchmark 1: npx rspack build --config rspack.medium.mjs
  Time (mean ± σ):     512.7 ms ±  11.1 ms    [User: 1226.4 ms, System: 1492.1 ms]
  Range (min … max):   495.3 ms … 531.4 ms    10 runs
 
Benchmark 1: npx rspack build --config rspack.small.mjs
  Time (mean ± σ):     274.9 ms ±   7.4 ms    [User: 261.6 ms, System: 144.3 ms]
  Range (min … max):   267.7 ms … 288.4 ms    10 runs
 
Benchmark 1: npx rspack build --config rspack.lib.mjs
  Time (mean ± σ):     171.1 ms ±   2.9 ms    [User: 136.5 ms, System: 60.9 ms]
  Range (min … max):   168.5 ms … 180.5 ms    17 runs

### Vite v8.1.5

Benchmark 1: npx vite build
  Time (mean ± σ):     452.5 ms ±  25.1 ms    [User: 926.7 ms, System: 866.5 ms]
  Range (min … max):   433.8 ms … 507.3 ms    10 runs
 
  Warning: Statistical outliers were detected. Consider re-running this benchmark on a quiet system without any interferences from other programs. It might help to use the '--warmup' or '--prepare' options.
 
Benchmark 1: npx vite build
  Time (mean ± σ):     550.8 ms ±  36.2 ms    [User: 712.7 ms, System: 747.0 ms]
  Range (min … max):   525.4 ms … 638.2 ms    10 runs
 
Benchmark 1: npx vite build
  Time (mean ± σ):     294.1 ms ±   8.7 ms    [User: 268.9 ms, System: 135.0 ms]
  Range (min … max):   288.6 ms … 317.0 ms    10 runs
 
Benchmark 1: npx vite build
  Time (mean ± σ):     200.0 ms ±   2.3 ms    [User: 168.1 ms, System: 88.6 ms]
  Range (min … max):   196.6 ms … 205.2 ms    14 runs

## Large Project

Use `data/projects/large` as basis.

### Netpack v0.5.2

**No config needed**

Running:

```sh
hyperfine 'npx netpack bundle src/index.html --minify --sourcemap' 'npx netpack bundle src/index.html --minify' 'npx netpack bundle src/index.html'
```

Results:

```
Benchmark 1: npx netpack bundle src/index.html --minify --sourcemap
  Time (mean ± σ):     718.9 ms ±   7.3 ms    [User: 812.2 ms, System: 394.7 ms]
  Range (min … max):   705.7 ms … 728.4 ms    10 runs
 
Benchmark 2: npx netpack bundle src/index.html --minify
  Time (mean ± σ):     685.5 ms ±   7.3 ms    [User: 774.9 ms, System: 391.4 ms]
  Range (min … max):   676.7 ms … 700.6 ms    10 runs
 
Benchmark 3: npx netpack bundle src/index.html
  Time (mean ± σ):     483.4 ms ±   5.2 ms    [User: 562.3 ms, System: 383.3 ms]
  Range (min … max):   476.1 ms … 490.5 ms    10 runs
```

### esbuild v0.28.1

**Configuration (build.mjs)**:

```js
import esbuild from "esbuild";
import { sassPlugin } from "esbuild-sass-plugin";
import htmlPlugin from "@chialab/esbuild-plugin-html";

const minify = process.argv.includes("--minify");
const sourcemap = process.argv.includes("--sourcemap");

await esbuild.build({
  bundle: true,
  minify,
  splitting,
  format: "esm",
  outdir: "dist",
  entryPoints: ["./src/index.html"],
  metafile: true,
  loader: {
    ".jpg": "file",
    ".png": "file",
    ".css": "css",
  },
  plugins: [htmlPlugin(), sassPlugin()],
});
```

Running:

```sh
hyperfine 'node build.mjs --minify --sourcemap' 'node build.mjs --minify' 'node build.mjs'
```

Results:

```
Benchmark 1: node build.mjs --minify --sourcemap
  Time (mean ± σ):     792.1 ms ±   7.0 ms    [User: 762.5 ms, System: 290.4 ms]
  Range (min … max):   775.6 ms … 799.1 ms    10 runs
 
Benchmark 2: node build.mjs --minify
  Time (mean ± σ):     787.8 ms ±   9.6 ms    [User: 1155.5 ms, System: 678.4 ms]
  Range (min … max):   774.5 ms … 802.7 ms    10 runs
 
Benchmark 3: node build.mjs
  Time (mean ± σ):     738.8 ms ±   8.6 ms    [User: 548.9 ms, System: 149.1 ms]
  Range (min … max):   724.4 ms … 752.9 ms    10 runs
```
