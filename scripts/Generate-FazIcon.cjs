// Run with Node.js and sharp on NODE_PATH. The SVG is the editable master.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '../assets/branding');
(async () => {
    const name = process.argv[2] || 'faz-file';
    if (!['faz-file', 'fad-file', 'fpt-file'].includes(name)) throw new Error('Unknown file icon');
    const source = path.join(root, name + '.svg');
    await sharp(source).png().toFile(path.join(root, name + '.png'));
    const sizes = [16, 24, 32, 48, 64, 128, 256];
    const images = await Promise.all(sizes.map(size => sharp(source).resize(size, size).png().toBuffer()));
    const header = Buffer.alloc(6 + 16 * sizes.length);
    header.writeUInt16LE(1, 2); header.writeUInt16LE(sizes.length, 4);
    let offset = header.length;
    images.forEach((png, i) => {
        const p = 6 + i * 16;
        header[p] = sizes[i] === 256 ? 0 : sizes[i]; header[p + 1] = header[p];
        header.writeUInt16LE(1, p + 4); header.writeUInt16LE(32, p + 6);
        header.writeUInt32LE(png.length, p + 8); header.writeUInt32LE(offset, p + 12); offset += png.length;
    });
    fs.writeFileSync(path.join(root, name + '.ico'), Buffer.concat([header, ...images]));
    const chunks = [];
    for (const [name, size] of [['icp4', 16], ['icp5', 32], ['icp6', 64], ['ic07', 128], ['ic08', 256], ['ic09', 512], ['ic10', 1024]]) {
        const png = await sharp(source).resize(size, size).png().toBuffer();
        const h = Buffer.alloc(8); h.write(name); h.writeUInt32BE(png.length + 8, 4); chunks.push(h, png);
    }
    const h = Buffer.alloc(8); h.write('icns'); h.writeUInt32BE(8 + chunks.reduce((n, c) => n + c.length, 0), 4);
    fs.writeFileSync(path.join(root, name + '.icns'), Buffer.concat([h, ...chunks]));
})().catch(error => { console.error(error); process.exitCode = 1; });
