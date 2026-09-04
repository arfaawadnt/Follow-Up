/**
 * Tiny static server for the .xlsx verification harness. Run `node generate-sample.js` first,
 * then `node serve.js` and open http://localhost:8897 — viewer.html loads sample.xlsx via SheetJS
 * and reports whether it opens cleanly. Serves only this directory.
 */
const http = require('http');
const fs = require('fs');
const path = require('path');

const DIR = __dirname;
const PORT = process.env.PORT || 8897;
const TYPES = {
  '.html': 'text/html',
  '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  '.js': 'text/javascript',
};

http.createServer((req, res) => {
  const rel = req.url === '/' ? '/viewer.html' : req.url.split('?')[0];
  const file = path.join(DIR, path.normalize(rel).replace(/^(\.\.[/\\])+/, ''));
  fs.readFile(file, (err, data) => {
    if (err) { res.writeHead(404); res.end('not found'); return; }
    res.writeHead(200, { 'Content-Type': TYPES[path.extname(file)] || 'application/octet-stream' });
    res.end(data);
  });
}).listen(PORT, () => console.log('xlsx-verify serving ' + DIR + ' on http://localhost:' + PORT));
