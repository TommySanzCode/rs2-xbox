import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const serverRoot = path.dirname(fileURLToPath(import.meta.url));
const runtime = path.join(serverRoot, 'runtime');
const engine = path.join(serverRoot, 'engine');
const runIdIndex = process.argv.indexOf('--run-id');
const runId = runIdIndex >= 0 ? process.argv[runIdIndex + 1] : '';
if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(runId ?? '')) {
    throw new Error('Run this server through scripts/Start-RS2-Server.ps1.');
}
if (path.resolve(process.cwd()).toLowerCase() !== engine.toLowerCase()) {
    throw new Error('The server working directory must be Server225/engine.');
}

const statusPath = path.join(runtime, `server-${runId}.status.json`);
const stopPath = path.join(runtime, `server-${runId}.stop-request`);
let phase = 'starting';
let appLoaded = false;
let stopSent = false;
let nodePort: number | undefined;

function writeStatus(extra: Record<string, unknown> = {}) {
    const temporaryPath = `${statusPath}.tmp`;
    fs.writeFileSync(temporaryPath, JSON.stringify({
        pid: process.pid, runId, status: phase, nodePort,
        updatedAt: new Date().toISOString(), ...extra
    }, null, 2) + '\n');
    fs.renameSync(temporaryPath, statusPath);
}

function checkStopRequest() {
    if (!appLoaded || stopSent || !fs.existsSync(stopPath)) return;
    if (fs.readFileSync(stopPath, 'utf8').trim() !== runId) return;
    stopSent = true;
    phase = 'stopping';
    writeStatus();
    console.log('Local control: requesting the engine\'s normal save-and-shutdown sequence.');
    // app.ts registers SIGTERM after startup. Emitting locally uses that same
    // handler without Windows' forceful external process termination behavior.
    process.emit('SIGTERM');
}

fs.mkdirSync(runtime, { recursive: true });
writeStatus();
process.on('exit', (exitCode) => {
    phase = stopSent && exitCode === 0 ? 'stopped' : 'exited';
    try { writeStatus({ exitCode }); } catch { /* Preserve the original exit. */ }
});

await import('./engine/src/app.ts');
const { default: environment } = await import('./engine/src/util/Environment.ts');
nodePort = environment.NODE_PORT;
appLoaded = true;
phase = 'ready';
writeStatus();
checkStopRequest();
setInterval(checkStopRequest, 500).unref();
