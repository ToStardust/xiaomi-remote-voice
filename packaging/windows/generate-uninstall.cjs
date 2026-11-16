const fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../构建/payload/app'), files=[], dirs=[];
function walk(dir,rel=''){for(const entry of fs.readdirSync(dir,{withFileTypes:true})){const part=rel?`${rel}\\${entry.name}`:entry.name, full=path.join(dir,entry.name);if(entry.isDirectory()){walk(full,part);dirs.push(part)}else if(entry.isFile())files.push(part);else throw Error('Unexpected payload entry');}}
walk(root);dirs.sort((a,b)=>b.split('\\').length-a.split('\\').length);
const quote=s=>s.replaceAll('$','$$').replaceAll('"','$\\"');
const lines=[...files.map(p=>`Delete "$INSTDIR\\${quote(p)}"`),...dirs.map(p=>`RMDir "$INSTDIR\\${quote(p)}"`)];
fs.writeFileSync(path.resolve(__dirname,'../构建/uninstall-files.nsh'),'\ufeff'+lines.join('\r\n')+'\r\n','utf8');
console.log(`Generated safe uninstall manifest for ${files.length} files.`);
