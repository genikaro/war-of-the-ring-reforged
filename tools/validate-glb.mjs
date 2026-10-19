// Optional Khronos validation: node tools/validate-glb.mjs <gltf-validator package directory>
import fs from 'node:fs';
import path from 'node:path';
import {createHash} from 'node:crypto';
import {createRequire} from 'node:module';
import {fileURLToPath} from 'node:url';

const require = createRequire(import.meta.url);
const validator = require(process.argv[2] ? path.resolve(process.argv[2]) : 'gltf-validator');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', 'converted');
const report = {files: [], errors: 0, warnings: 0};
const customIndex = process.argv.indexOf('--index');
const indexes = customIndex >= 0 ? [path.resolve(process.argv[customIndex + 1])] :
  ['units', 'buildings', 'projectiles'].map(folder => path.join(root, folder, 'index.json'));
for (const indexPath of indexes) {
  const index = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
  for (const file of new Set(index.filter(row => row.file).map(row => row.file))) {
    const full = path.join(path.dirname(indexPath), file);
    const bytes = fs.readFileSync(full);
    const result = await validator.validateBytes(new Uint8Array(bytes), {maxIssues: 10000});
    report.files.push({file: path.relative(root, full).replaceAll('\\', '/'), sha256: createHash('sha256').update(bytes).digest('hex'), ...result.issues});
    report.errors += result.issues.numErrors;
    report.warnings += result.issues.numWarnings;
  }
}
const reportOption = process.argv.indexOf('--report');
const reportPath = reportOption >= 0 ? path.resolve(process.argv[reportOption + 1]) : path.join(root, 'gltf-validation.json');
fs.writeFileSync(reportPath, JSON.stringify(report, null, 2) + '\n');
console.log(`${report.files.length} GLBs: ${report.errors} errors, ${report.warnings} warnings`);
process.exitCode = report.errors ? 1 : 0;
