// Assemble /root/asset-masters/xr_lod/manifest.json from the driver's JSONL results.
import { readFileSync, writeFileSync } from 'node:fs';
const RES = '/tmp/xr_results.jsonl';
const OUT = '/root/asset-masters/xr_lod/manifest.json';

const rows = readFileSync(RES, 'utf8').trim().split('\n').filter(Boolean).map(l => JSON.parse(l));
const models = rows.map(r => ({
  name: r.name,
  source: r.source,
  sourceTris: r.sourceTris,
  sourceMB: r.sourceMB,
  lod0: { file: r.lod0.file, tris: r.lod0.tris, sizeMB: r.lod0.sizeMB, texFormat: r.lod0.texFormat },
  lod1: { file: r.lod1.file, tris: r.lod1.tris, sizeMB: r.lod1.sizeMB, texFormat: r.lod1.texFormat },
}));
const manifest = {
  generated: new Date().toISOString(),
  generator: 'optimize_xr.mjs',
  target: 'Unity glTFast (KTX2/KHR_texture_basisu + Draco)',
  lodSpec: { lod0: 'target ~30-40k tris, textures max 2048px', lod1: 'target ~8-12k tris, textures max 1024px' },
  count: models.length,
  totalOutputMB: +models.reduce((s, m) => s + m.lod0.sizeMB + m.lod1.sizeMB, 0).toFixed(2),
  models,
};
writeFileSync(OUT, JSON.stringify(manifest, null, 2));
console.log(`manifest.json: ${models.length} models, total ${manifest.totalOutputMB} MB output`);
