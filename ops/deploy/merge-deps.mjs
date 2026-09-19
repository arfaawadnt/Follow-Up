#!/usr/bin/env node
// Merges the NuGet packages a Release build added into the PRODUCTION FollowUp.Api.deps.json (2026-09-20).
//
// Production runs a SELF-CONTAINED publish (coreclr.dll + runtime pack listed in its deps.json, runtimeTarget
// ".NETCoreApp,Version=v8.0/win-x64"), while the deploy scripts ship DLLs from a framework-dependent Release build whose
// deps.json has no runtime pack. Overwriting the production file with the build's one leaves the host unable to resolve
// CoreCLR ("Could not resolve CoreCLR path") and the service does not start. So the production deps.json is kept and only
// the entries of the packages named on the command line (targets + libraries, and the dependency edges of the FollowUp.*
// projects that reference them) are copied in from the build's file.
//
//   node merge-deps.mjs <prod deps.json> <build deps.json> <out deps.json> <package prefix>...
//   e.g. node merge-deps.mjs C:\FollowUp\app\FollowUp.Api.deps.json bin\Release\net8.0\FollowUp.Api.deps.json merged.json SkiaSharp HarfBuzzSharp
import fs from 'node:fs';

const [prodPath, buildPath, outPath, ...prefixes] = process.argv.slice(2);
if (!prodPath || !buildPath || !outPath || prefixes.length === 0) {
  console.error('usage: merge-deps.mjs <prod deps.json> <build deps.json> <out deps.json> <package prefix>...');
  process.exit(2);
}
const prod = JSON.parse(fs.readFileSync(prodPath, 'utf8'));
const build = JSON.parse(fs.readFileSync(buildPath, 'utf8'));
const prodKey = Object.keys(prod.targets).find((k) => Object.keys(prod.targets[k]).length > 0) ?? Object.keys(prod.targets)[0];
const buildKey = Object.keys(build.targets).find((k) => Object.keys(build.targets[k]).length > 0) ?? Object.keys(build.targets)[0];
const pt = prod.targets[prodKey], bt = build.targets[buildKey];
const wanted = (name) => prefixes.some((p) => name.toLowerCase().startsWith(p.toLowerCase()));

let added = 0, edges = 0;
for (const [name, entry] of Object.entries(bt)) {
  if (!wanted(name)) continue;
  if (!pt[name]) { pt[name] = entry; added++; }
  if (build.libraries[name] && !prod.libraries[name]) prod.libraries[name] = build.libraries[name];
}
// Dependency edges: every project / package in the build that depends on a wanted package gets the same edge in prod.
for (const [name, entry] of Object.entries(bt)) {
  if (!entry.dependencies || !pt[name]) continue;
  for (const [dep, ver] of Object.entries(entry.dependencies)) {
    if (!wanted(dep)) continue;
    pt[name].dependencies ??= {};
    if (!pt[name].dependencies[dep]) { pt[name].dependencies[dep] = ver; edges++; }
  }
}
fs.writeFileSync(outPath, JSON.stringify(prod, null, 2));
console.log(`merged into ${outPath}: ${added} package entries added, ${edges} dependency edges added (target ${prodKey})`);
