// Vector masters are based on the approved FA + leaf reference. Raster outputs are
// reproducible with Node.js and sharp: NODE_PATH=<node_modules> node scripts/Generate-Branding.cjs.
const fs = require('node:fs');
const path = require('node:path');
const sharp = require('sharp');
const root = path.resolve(__dirname, '..', 'assets', 'branding');
fs.mkdirSync(root, {recursive: true});
const mark = `<path d="M206 673 417 316Q443 270 497 270H760L688 349Q676 363 659 363H515Q497 363 486 382L464 421H646L579 494Q566 510 546 510H441Q417 510 403 534L336 641Q315 673 268 673Z"/>
<path d="M450 674 774 297Q800 267 831 267H841Q887 267 910 315L1040 608Q1056 641 1040 660Q1026 675 1000 675Q955 677 936 639L831 425Q817 397 794 416L608 625Q560 674 502 674Z"/>
<path d="m695 577 49-62q11-12 27-12h65q9 0 14 11l27 52q6 11-7 11Z"/>
<path d="M765 259C780 171 854 113 965 126 943 204 874 246 765 259Z"/>`;
const defs = `<defs><linearGradient id="purple" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#6455ff"/><stop offset="1" stop-color="#4c43f4"/></linearGradient><linearGradient id="tile" x1="0" y1="0" x2="1" y2="1"><stop stop-color="#303034"/><stop offset="1" stop-color="#161619"/></linearGradient></defs>`;
const svg = (w,h,body,extra='') => `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}" ${extra}><title>FruitsAtelier · 水果工坊</title>${defs}${body}</svg>\n`;
const markSvg = svg(1024,768,`<g fill="url(#purple)" transform="translate(26 45) scale(1.13) translate(-190 -100)">${mark}</g>`);
const appSvg = svg(1024,1024,`<rect x="64" y="64" width="896" height="896" rx="200" fill="url(#tile)"/><rect x="65" y="65" width="894" height="894" rx="199" fill="none" stroke="#ffffff" stroke-opacity=".1" stroke-width="2"/><g fill="#fafafa" transform="translate(112 250) scale(.93) translate(-190 -100)">${mark}</g>`);
fs.writeFileSync(path.join(root,'mark.svg'),markSvg);
fs.writeFileSync(path.join(root,'app-icon.svg'),appSvg);
for(const [language,text] of [['en','FruitsAtelier'],['zh','水果工坊']]) {
 const textSize = language === 'en' ? 94 : 100;
 const word = svg(language === 'en' ? 920 : 790,260,`<style>.word{fill:#202126}@media(prefers-color-scheme:dark){.word{fill:#f3f3fa}}</style><g fill="url(#purple)" transform="translate(6 24) scale(.35) translate(-190 -100)">${mark}</g><text class="word" x="348" y="164" font-family="Arial,Helvetica,PingFang SC,Microsoft YaHei,sans-serif" font-size="${textSize}" font-weight="700" letter-spacing="-3">${text}</text>`);
 fs.writeFileSync(path.join(root,`wordmark-${language}.svg`),word);
}
(async()=>{
 await sharp(Buffer.from(markSvg)).png().toFile(path.join(root,'mark.png'));
 await sharp(Buffer.from(appSvg)).png().toFile(path.join(root,'app-icon.png'));
 for(const language of ['en','zh']) await sharp(path.join(root,`wordmark-${language}.svg`)).png().toFile(path.join(root,`wordmark-${language}.png`));
 const sizes=[16,24,32,48,64,128,256];const icons=[];
 for(const size of sizes) icons.push(await sharp(Buffer.from(appSvg)).resize(size,size).png().toBuffer());
 const header=Buffer.alloc(6+16*sizes.length);header.writeUInt16LE(1,2);header.writeUInt16LE(sizes.length,4);let offset=header.length;
 icons.forEach((png,i)=>{const p=6+16*i;header[p]=sizes[i]===256?0:sizes[i];header[p+1]=header[p];header.writeUInt16LE(1,p+4);header.writeUInt16LE(32,p+6);header.writeUInt32LE(png.length,p+8);header.writeUInt32LE(offset,p+12);offset+=png.length;});
 fs.writeFileSync(path.join(root,'app-icon.ico'),Buffer.concat([header,...icons]));
 const iconset=path.resolve(__dirname,'..','artifacts','branding','app-icon.iconset');fs.mkdirSync(iconset,{recursive:true});
 for(const size of [16,32,128,256,512]) for(const scale of [1,2]) await sharp(Buffer.from(appSvg)).resize(size*scale,size*scale).png().toFile(path.join(iconset,`icon_${size}x${size}${scale===2?'@2x':''}.png`));
 console.log(root);
})();
