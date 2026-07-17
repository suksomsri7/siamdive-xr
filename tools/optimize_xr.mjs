// optimize_xr.mjs — generate Unity/glTFast-ready LOD variants from a raw master GLB.
//
// Unity glTFast can load: KTX2 (KHR_texture_basisu) + Draco (KHR_draco_mesh_compression).
// It CANNOT load EXT_texture_webp (the web pipeline's optimize.mjs uses webp — do NOT reuse it here).
// So XR variants use KTX2/ETC1S textures (toktx must be on PATH) with a JPEG/PNG fallback,
// plus Draco geometry compression.
//
// Pipeline per LOD (fresh read of the raw master each time, so simplify works on full-res):
//   dedup → flatten → join → weld → simplify(meshopt, ratio to hit target tris)
//   → resample → prune → sparse → resize textures (sharp, PNG intermediate, square POT)
//   → [etc1s KTX2 via gltf-transform CLI]  (fallback: JPEG q85 / PNG where alpha)
//   → draco (applied LAST so it never strips the KTX2 textures)
//
// Outputs: <OUTDIR>/<NAME>_xr0.glb (LOD0) and <OUTDIR>/<NAME>_xr1.glb (LOD1).
// Prints one JSON line to stdout: { name, source:{tris,sizeMB}, lod0:{...}, lod1:{...} }
//
// Usage: node optimize_xr.mjs <in.glb> <outdir> <name>
// RAM note: VPS is 2c/3GB — process ONE model at a time. If toktx/simplify OOMs on a
// 4K-texture model, lower the tex sizes in LODS below (2048→1024, 1024→512) and retry.

import { NodeIO } from '@gltf-transform/core';
import { ALL_EXTENSIONS } from '@gltf-transform/extensions';
import { dedup, weld, simplify, prune, flatten, join, textureCompress, resample, sparse } from '@gltf-transform/functions';
import { MeshoptSimplifier } from 'meshoptimizer';
import draco3d from 'draco3dgltf';
import sharp from 'sharp';
import { readFileSync, existsSync, unlinkSync, statSync } from 'node:fs';
import { execFileSync } from 'node:child_process';

const [,, IN, OUTDIR, NAME] = process.argv;
if (!IN || !OUTDIR || !NAME) { console.error('usage: node optimize_xr.mjs <in.glb> <outdir> <name>'); process.exit(1); }

const LODS = [
  { tag: 'xr0', targetTris: 35000, tex: 2048, etc1sQuality: 255, etc1sComp: 3, error: 0.008 },
  { tag: 'xr1', targetTris: 10000, tex: 1024, etc1sQuality: 128, etc1sComp: 2, error: 0.02  },
];

const io = new NodeIO().registerExtensions(ALL_EXTENSIONS).registerDependencies({
  'draco3d.decoder': await draco3d.createDecoderModule(),
  'draco3d.encoder': await draco3d.createEncoderModule(),
});
await MeshoptSimplifier.ready;

function triCount(doc) {
  let tris = 0;
  for (const m of doc.getRoot().listMeshes())
    for (const p of m.listPrimitives()) {
      const idx = p.getIndices(); const pos = p.getAttribute('POSITION');
      tris += idx ? idx.getCount() / 3 : (pos ? pos.getCount() / 3 : 0);
    }
  return Math.round(tris);
}
function hasAlpha(doc) {
  return doc.getRoot().listMaterials().some(m => m.getAlphaMode && m.getAlphaMode() !== 'OPAQUE');
}
const MB = f => +(statSync(f).size / 1048576).toFixed(2);
const CLI = ['-y', 'gltf-transform'];   // via npx

async function buildLOD(spec) {
  const tmpGeo = `/tmp/xr_${NAME}_${spec.tag}_geo.glb`;
  const tmpTex = `/tmp/xr_${NAME}_${spec.tag}_tex.glb`;
  const out    = `${OUTDIR}/${NAME}_${spec.tag}.glb`;

  const doc = await io.read(IN);
  const srcTris = triCount(doc);
  const ratio = Math.min(1, spec.targetTris / Math.max(1, srcTris));
  const alpha = hasAlpha(doc);

  await doc.transform(
    dedup(), flatten(), join(), weld(),
    simplify({ simplifier: MeshoptSimplifier, ratio, error: spec.error }),
    resample(), prune(), sparse(),
    // Normalise textures to resized square-POT PNG (good, lossless input for toktx).
    textureCompress({ encoder: sharp, targetFormat: 'png', resize: [spec.tex, spec.tex] }),
  );
  await io.write(tmpGeo, doc);
  const lodTris = triCount(doc);

  // Textures → KTX2/ETC1S (Unity glTFast reads KHR_texture_basisu). Fallback: JPEG/PNG.
  let texFormat;
  try {
    execFileSync('npx', [...CLI, 'etc1s', tmpGeo, tmpTex,
      '--quality', String(spec.etc1sQuality), '--compression', String(spec.etc1sComp)],
      { stdio: 'pipe', timeout: 15 * 60 * 1000 });
    texFormat = 'ktx2-etc1s';
  } catch (e) {
    process.stderr.write(`[${NAME} ${spec.tag}] etc1s failed, falling back to ${alpha ? 'png' : 'jpeg'}: ${String(e.message || e).slice(0, 120)}\n`);
    const d2 = await io.read(tmpGeo);
    await d2.transform(textureCompress({
      encoder: sharp,
      targetFormat: alpha ? 'png' : 'jpeg',
      ...(alpha ? {} : { quality: 85 }),
    }));
    await io.write(tmpTex, d2);
    texFormat = alpha ? 'png' : 'jpeg';
  }

  // Draco LAST — geometry-only, never touches the KTX2 textures.
  execFileSync('npx', [...CLI, 'draco', tmpTex, out,
    '--method', 'edgebreaker',
    '--quantize-position', '14', '--quantize-normal', '10', '--quantize-texcoord', '12'],
    { stdio: 'pipe', timeout: 10 * 60 * 1000 });

  for (const f of [tmpGeo, tmpTex]) if (existsSync(f)) unlinkSync(f);
  return { file: `${NAME}_${spec.tag}.glb`, tris: lodTris, sizeMB: MB(out), texFormat, srcTris };
}

const srcDoc = await io.read(IN);
const source = { tris: triCount(srcDoc), sizeMB: MB(IN) };

const lod0 = await buildLOD(LODS[0]);
const lod1 = await buildLOD(LODS[1]);

console.log(JSON.stringify({ name: NAME, source: `${IN}`, sourceTris: source.tris, sourceMB: source.sizeMB, lod0, lod1 }));
