# ApexCharts — vendored

**Version 4.7.0 · MIT · https://github.com/apexcharts/apexcharts.js**

`apexcharts.esm.js` is the untouched ESM build from the npm package
(`apexcharts-4.7.0.tgz`, `dist/apexcharts.esm.js`). `LICENSE.txt` is that package's licence file.

## Why 4.7.0 and not the newest

**4.7.0 is the last MIT release.** From v5 the project moved to a dual licence: free only for
individuals, non-profits and companies under **$2 million** annual revenue, and payable above it.
This site is intended to make money, and a dependency whose terms change the moment it succeeds is
a bad trade for a minor-version bump. MIT has no such ceiling and cannot be revoked from a version
already published.

Revisit only with a deliberate decision — and if a newer version is ever wanted, read its LICENSE
file rather than the npm `license` field, which read `SEE LICENSE IN LICENSE` for exactly the
releases where the terms changed.

## Why local rather than a CDN

The site's own shell must not depend on a third party being reachable, and the UAT and production
boxes are not guaranteed outbound internet. Loaded from `/plugins/apexcharts/` like the other
vendored libraries.

## Peity

Ben asked about Peity for tiny inline charts. It is not vendored: it requires jQuery, which this
site does not load, and ApexCharts covers the same job through its `sparkline` mode. One library
for both roles.
