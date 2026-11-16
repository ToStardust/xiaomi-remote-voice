import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { fileURLToPath } from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const inputPath=process.argv[2];
if(!inputPath)throw Error('Usage: node packaging/windows/build-asar.mjs <original-0.5.18-app.asar>');
const input=fs.readFileSync(inputPath), hash=b=>crypto.createHash('sha256').update(b).digest('hex');
if(hash(input)!=='572588703344d12394912de507abe66f84a58c0abe277679c4523efb2854f228') throw Error('Expected unmodified VibeCoding Voice 0.5.18 app.asar');
const header=JSON.parse(input.subarray(16,16+input.readUInt32LE(12))), start=8+input.readUInt32LE(4), entries=[];
function visit(node,prefix=''){for(const [name,child] of Object.entries(node.files||{})){const file=prefix?`${prefix}/${name}`:name;if(child.files)visit(child,file);else if(!child.unpacked&&child.offset!==undefined)entries.push({file,entry:child,oldOffset:Number(child.offset)})}}
visit(header);entries.sort((a,b)=>a.oldOffset-b.oldOffset);
const changed=new Set(['desktop/index.html','desktop/main.mjs','desktop/preload.cjs','desktop/renderer.js','scripts/xiaomi-remote-input.mjs','src/desktop-settings.mjs','src/paths.mjs','src/server.mjs','src/windows-remote-broker-client.mjs']);
let offset=0;const chunks=[];
for(const x of entries){const e=x.entry,old=input.subarray(start+x.oldOffset,start+x.oldOffset+e.size);if(old.length!==e.size||(e.integrity?.algorithm==='SHA256'&&hash(old)!==e.integrity.hash))throw Error(`Invalid source archive: ${x.file}`);const data=changed.has(x.file)?fs.readFileSync(path.join(root,...x.file.split('/'))):old;if(changed.has(x.file)){changed.delete(x.file);e.integrity.hash=hash(data);e.integrity.blocks=[];for(let i=0;i<data.length;i+=e.integrity.blockSize)e.integrity.blocks.push(hash(data.subarray(i,i+e.integrity.blockSize)))}e.offset=String(offset);e.size=data.length;offset+=data.length;chunks.push(data)}
if(changed.size)throw Error(`Missing source entries: ${[...changed]}`);
const json=Buffer.from(JSON.stringify(header)),padded=(json.length+3)&~3,size=8+padded,head=Buffer.alloc(8+size);head.writeUInt32LE(4,0);head.writeUInt32LE(size,4);head.writeUInt32LE(size-4,8);head.writeUInt32LE(json.length,12);json.copy(head,16);
const output=path.join(root,'packaging','构建','share-app.asar');fs.mkdirSync(path.dirname(output),{recursive:true});const result=Buffer.concat([head,...chunks]);fs.writeFileSync(output,result);console.log(`Generated ${output}\nSHA-256 ${hash(result)}`);
