import fs from 'node:fs';
fs.mkdirSync('dist/server',{recursive:true});fs.mkdirSync('dist/.openai',{recursive:true});
const page=fs.readFileSync('worker/page.js','utf8').replace('export const html=','const html=');
const worker=fs.readFileSync('worker/index.js','utf8').replace("import { html } from './page.js';",page);
fs.writeFileSync('dist/server/index.js',worker);fs.copyFileSync('.openai/hosting.json','dist/.openai/hosting.json');
