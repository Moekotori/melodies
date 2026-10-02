import fs from 'node:fs';
import path from 'node:path';

// Development only: import geometry from the Lucide version used by ECHO.
const source = process.argv[2] ?? 'D:/ECHO';
const destination = path.resolve('src/Echo.Desktop/Resources/Icons/glyph');
const glyphs = { search:'search', close:'x', minus:'minus', maximize:'square', fullscreen:'expand',
  hide:'panel-bottom-close', collapse:'chevrons-left', 'chevron-left':'chevron-left',
  'chevron-right':'chevron-right', refresh:'refresh-cw', sun:'sun', moon:'moon', settings:'settings',
  music:'music-2', play:'play', library:'library', album:'album', disc:'disc-3', history:'history',
  queue:'list-music', shuffle:'shuffle', users:'users-round', folder:'folder-open', headphones:'headphones',
  sparkles:'sparkles', user:'user-round', message:'message-square', palette:'palette', keyboard:'keyboard',
  accessibility:'accessibility', zap:'zap', download:'download', captions:'captions', link:'link-2',
  gamepad:'gamepad-2', gauge:'gauge', database:'database', info:'info', trash:'trash-2', plus:'plus',
  reset:'rotate-ccw', help:'circle-help', book:'book-open' };
fs.mkdirSync(destination, {recursive:true});
for (const [name, original] of Object.entries(glyphs)) {
  const sourceCode = fs.readFileSync(path.join(source, 'node_modules/lucide-react/dist/esm/icons', original+'.js'), 'utf8');
  const literal = sourceCode.match(/createLucideIcon\("[^"]+", (\[[\s\S]*?\])\s*\);/)?.[1];
  if (!literal) throw new Error(`Missing source geometry: ${original}`);
  const nodes = JSON.parse(literal.replace(/\b([a-zA-Z]\w*):\s/g, '"$1": '));
  const shapes = nodes.map(([tag, attrs]) => `<${tag} ${Object.entries(attrs).filter(([k])=>k!=='key').map(([k,v])=>`${k}="${String(v).replaceAll('&','&amp;').replaceAll('"','&quot;')}"`).join(' ')} />`).join('\n');
  fs.writeFileSync(path.join(destination, name+'-base.svg'), `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">\n${shapes}\n</svg>\n`);
}
fs.copyFileSync(path.join(source,'node_modules/lucide-react/LICENSE'), path.join(destination,'LICENSE'));
console.log(`Imported ${Object.keys(glyphs).length} original glyphs.`);
