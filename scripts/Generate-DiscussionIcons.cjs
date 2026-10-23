const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const directory = path.resolve(__dirname, '../assets/icons/discussion');
(async () => {
    for (const name of fs.readdirSync(directory).filter(name => name.endsWith('.svg'))) {
        const file = path.join(directory, name);
        const source = fs.readFileSync(file, 'utf8').replace('<svg ', '<svg fill="#ffffff" ');
        await sharp(Buffer.from(source)).resize(128, 128, {
            fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 }
        }).png().toFile(file.replace(/\.svg$/, '.png'));
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
