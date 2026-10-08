import { build } from 'esbuild';
import { readFile, readdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

await build({
  entryPoints: ['src/bridge.mjs'],
  outfile: 'dist/bridge.mjs',
  bundle: true,
  platform: 'node',
  format: 'esm',
  target: 'node22.19',
  legalComments: 'linked',
  banner: { js: 'import { createRequire } from "node:module"; const require = createRequire(import.meta.url);' },
});

// esbuild retains license comments. Include complete dependency license texts
// too, since several SDKs do not carry their notices in every source file.
const notices = [await readFile('PI-LICENSE.txt', 'utf8')];
async function collect(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (!entry.isDirectory() || entry.name.startsWith('.')) continue;
    const path = join(directory, entry.name);
    if (entry.name.startsWith('@')) { await collect(path); continue; }
    const files = await readdir(path, { withFileTypes: true });
    const manifest = JSON.parse(await readFile(join(path, 'package.json'), 'utf8'));
    notices.push(`\n--- ${manifest.name}@${manifest.version} (${manifest.license ?? 'see license below'}) ---\n`);
    for (const file of files) {
      if (file.isFile() && /^(license|licence|copying|notice)(\.|$)/i.test(file.name)) {
        notices.push(await readFile(join(path, file.name), 'utf8'));
      }
    }
    if (files.some((file) => file.isDirectory() && file.name === 'node_modules')) await collect(join(path, 'node_modules'));
  }
}
await collect('node_modules');
await writeFile('dist/THIRD-PARTY-NOTICES.txt', notices.join('\n'));
