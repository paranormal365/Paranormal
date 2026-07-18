/**
 * rollup.blazor.config.js
 *
 * Builds ESM bundles for Blazor JS interop consumption.
 * Output goes to wwwroot/js/wavesurfer/ (served by Ben.Web.WebApp).
 *
 * Usage: npm run build:blazor
 */

import { glob } from 'glob'
import typescript from '@rollup/plugin-typescript'
import terser from '@rollup/plugin-terser'
import webWorkerLoader from 'rollup-plugin-web-worker-loader'

// Relative to this file: ts/wavesurfer/ → ../../js/wavesurfer/
const OUTPUT_DIR = '../../js/wavesurfer'

// outDir must contain all rollup output files.
// @rollup/plugin-typescript validates that each output 'file' is inside outDir.
const buildPlugins = [
  webWorkerLoader(),
  typescript({ declaration: false, declarationDir: null, outDir: OUTPUT_DIR }),
  terser({ format: { comments: false } }),
]

export default [
  // ── Main WaveSurfer ESM bundle ─────────────────────────────────────────────
  {
    input: 'src/wavesurfer.ts',
    output: {
      file: `${OUTPUT_DIR}/wavesurfer.esm.js`,
      format: 'esm',
    },
    plugins: buildPlugins,
  },

  // ── Plugin ESM bundles (one per plugin, worker files excluded) ────────────
  ...glob
    .sync('src/plugins/*.ts')
    .filter((p) => !p.includes('worker'))
    .map((plugin) => ({
      input: plugin,
      output: {
        file: plugin
          .replace('src/plugins/', `${OUTPUT_DIR}/plugins/`)
          .replace('.ts', '.esm.js'),
        format: 'esm',
      },
      plugins: buildPlugins,
    })),
]
