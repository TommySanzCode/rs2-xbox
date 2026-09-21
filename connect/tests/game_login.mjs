// Integration fixture only: eight generated accounts in a disposable Connect world.
// Never point this at a personal world. Passwords are generated in memory, not CLI arguments.
import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { createPublicKey, randomBytes, randomInt } from 'node:crypto';
import { pathToFileURL } from 'node:url';
const [engine, portText] = process.argv.slice(2);
if (!engine || !portText || !fs.existsSync(path.join(engine, '../../connect-world.json'))) throw new Error('Requires a disposable Connect world.');
const port = Number(portText);
const Isaac = (await import(pathToFileURL(path.join(engine, 'src/io/Isaac.ts')).href)).default;
const key = createPublicKey(fs.readFileSync(path.join(engine, 'data/config/public.pem'))).export({format:'jwk'});
const number = bytes => BigInt('0x' + bytes.toString('hex'));
const exponent = number(Buffer.from(key.e, 'base64url')), modulus = number(Buffer.from(key.n, 'base64url'));
function pow(value, power, mod) { let result=1n; for (;power>0n;power>>=1n,value=value*value%mod) if(power&1n) result=result*value%mod; return result; }
function crc32(bytes) { let crc=0xffffffff; for(const b of bytes) { crc^=b; for(let j=0;j<8;j++) crc=(crc>>>1)^((crc&1)?0xedb88320:0); } return (crc^0xffffffff)>>>0; }
const crc = Buffer.alloc(36);
['title','config','interface','media','models','textures','wordenc','sounds'].forEach((name,i)=>crc.writeUInt32BE(crc32(fs.readFileSync(path.join(engine,'data/pack/client',name))),4*(i+1)));
const interfaces=fs.readFileSync(path.join(engine,'../content/pack/interface.pack'),'utf8');
const logout=Number(interfaces.match(/^(\d+)=logout:try_logout\s*$/m)[1]);
function packet(name,password,seed) {
 const plain=Buffer.alloc(21); plain[0]=10; seed.forEach((word,i)=>plain.writeUInt32BE(word>>>0,1+i*4));
 let hex=pow(number(Buffer.concat([plain,Buffer.from(name+'\n'+password+'\n')])),exponent,modulus).toString(16); if(hex.length%2) hex='0'+hex;
 const cipher=Buffer.concat([Buffer.from([0]),Buffer.from(hex,'hex')]);
 const body=Buffer.concat([Buffer.from([225,1]),crc,Buffer.from([cipher.length]),cipher]);
 return Buffer.concat([Buffer.from([16,body.length]),body]);
}
const accounts=Array.from({length:8},(_,i)=>({name:'ct'+randomBytes(4).toString('hex')+i,password:randomBytes(8).toString('hex')}));
function login(account) { return new Promise((resolve,reject)=>{
 const socket=net.createConnection({host:'127.0.0.1',port}); socket.setNoDelay(true);
 let buffer=Buffer.alloc(0),phase=0,isaac;
 const timer=setTimeout(()=>{socket.destroy();reject(new Error('Game login timed out'));},12000);
 socket.on('error',e=>{clearTimeout(timer);reject(new Error('Game socket failed: '+e.code));});
 socket.on('close',()=>{if(phase<3){clearTimeout(timer);reject(new Error('Game closed before initial data'));}});
 socket.on('data',chunk=>{
  buffer=Buffer.concat([buffer,chunk]);
  if(phase===0&&buffer.length>=8){const seed=[randomInt(100000000),randomInt(100000000),buffer.readUInt32BE(0),buffer.readUInt32BE(4)];buffer=buffer.subarray(8);isaac=new Isaac(seed);socket.write(packet(account.name,account.password,seed));phase=1;}
  if(phase===1&&buffer.length>=1){const reply=buffer[0];buffer=buffer.subarray(1);if(![2,18].includes(reply)){clearTimeout(timer);socket.destroy();reject(new Error('Game login response '+reply));return;}phase=2;}
  if(phase===2&&buffer.length>0){phase=3;clearTimeout(timer);resolve({socket,logout:async()=>{const bytes=Buffer.alloc(3);bytes[0]=(155+isaac.nextInt())&255;bytes.writeUInt16BE(logout,1);socket.write(bytes);await new Promise(done=>{if(socket.destroyed)return done();const t=setTimeout(()=>{socket.destroy();done();},6000);socket.once('close',()=>{clearTimeout(t);done();});});}});}
  if(phase===3) buffer=Buffer.alloc(0);
 });
}); }
let sessions=[];
try {
 sessions=await Promise.all(accounts.map(login));
 console.log('PASS: eight distinct revision-225 accounts logged in simultaneously through the gateway and received game data');
 await Promise.all(sessions.map(s=>s.logout()));
 console.log('PASS: normal logout packets sent for all eight characters');
 // Let the server's normal logout/save processing settle before reusing names.
 await new Promise(resolve=>setTimeout(resolve,2000));
 sessions=await Promise.all(accounts.map(login));
 console.log('PASS: all eight existing character credentials accepted on a second login');
 await Promise.all(sessions.map(s=>s.logout()));
} finally {for(const session of sessions)session.socket.destroy();}
