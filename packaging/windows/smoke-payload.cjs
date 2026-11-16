const fs=require('node:fs'),path=require('node:path'),net=require('node:net'),assert=require('node:assert/strict');
const {spawn,spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'..'), build=path.join(root,'构建'), app=process.argv[2]||path.join(build,'payload','app');
const profile=fs.mkdtempSync(path.join(build,'smoke profile with spaces-'));
const exe=path.join(app,'VibeCoding Voice.exe'), log=path.join(profile,'desktop.log');
function wait(ms){return new Promise(r=>setTimeout(r,ms))}
async function run(){
  const server=net.createServer();await new Promise((resolve,reject)=>server.listen(0,'127.0.0.1',resolve).on('error',reject));const port=server.address().port;await new Promise(r=>server.close(r));
  const child=spawn(exe,['--hidden'],{env:{...process.env,VIBE_REMOTE_CONFIG_HOME:profile,XIAOMI_REMOTE_ENABLED:'0',LAN_VOICE_PORT:String(port)},stdio:'ignore',windowsHide:true});
  try {
    let ready=false;
    for(let n=0;n<70;n++){await wait(500);if(child.exitCode!==null)throw Error(`Test app exited: ${child.exitCode}`);if(fs.existsSync(log)&&fs.readFileSync(log,'utf8').includes('server ready')){ready=true;break}}
    assert.ok(ready,'app and bridge did not start');
    const config=fs.readFileSync(path.join(profile,'config.env'),'utf8');
    assert.match(config,/XIAOMI_REMOTE_VOICE_MODE=wechat/);assert.match(config,/LAN_VOICE_BIND=127\.0\.0\.1/);
    const map=config.match(/^XIAOMI_REMOTE_BUTTON_MAP=(.*)$/m)?.[1]; assert.ok(map,'missing button mapping');
    for(const item of map.split(',')){if(!item.includes('=app:'))continue;const file=decodeURIComponent(item.slice(item.indexOf('=app:')+5));assert.ok(file.startsWith(path.join(app,'resources','remote-helper')+path.sep),`unexpected helper path`);assert.ok(fs.existsSync(file),`missing helper file`)}
    const prefs=JSON.parse(fs.readFileSync(path.join(profile,'desktop-settings.json'),'utf8'));assert.equal(prefs.recordTranscripts,false);
    console.log('Isolated first launch, configuration, helper paths and local bridge passed.');
  } finally {if(child.pid)spawnSync('taskkill',['/PID',String(child.pid),'/T','/F'],{windowsHide:true,stdio:'ignore'});}
}
run().catch(e=>{console.error(e.message);process.exitCode=1});
