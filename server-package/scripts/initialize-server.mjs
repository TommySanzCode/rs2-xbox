// First-run setup for the portable release. Run through Configure-Server.ps1,
// which holds the same exclusive control lock as Start and Stop.
import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { createPrivateKey, createPublicKey, generateKeyPairSync, randomBytes } from 'node:crypto';
import { Database } from 'bun:sqlite';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const engine = path.join(root, 'Server225', 'engine');
const runtime = path.join(root, 'Server225', 'runtime');
const statePath = path.join(runtime, 'setup.local.json');
const requireEngine = createRequire(path.join(engine, 'package.json'));
const bcrypt = requireEngine('bcrypt');

function atomicWrite(destination, value) {
    const temporary = `${destination}.tmp`;
    fs.writeFileSync(temporary, value, { mode: 0o600 });
    fs.renameSync(temporary, destination);
}

function readOptions() {
    const defaults = { server_address: 'auto', game_port: '43594', members: '1', xp_rate: '1', account_username: '', account_password: '' };
    const seen = new Set();
    for (const raw of fs.readFileSync(path.join(root, 'options.ini'), 'utf8').split(/\r?\n/)) {
        const line = raw.trim();
        if (!line || line.startsWith('#') || line.startsWith(';')) continue;
        const split = line.indexOf('=');
        const key = line.slice(0, split).trim();
        if (split < 0 || !Object.hasOwn(defaults, key) || seen.has(key)) {
            throw new Error('options.ini contains an unknown, duplicate, or malformed option. See Help.txt.');
        }
        defaults[key] = line.slice(split + 1).trim();
        seen.add(key);
    }
    return defaults;
}

async function checkPorts(ports) {
    // Probe before writing configuration. The real server remains the final
    // authority if another program takes a port after this check.
    for (const port of ports) {
        await new Promise((resolve, reject) => {
            const server = net.createServer();
            server.once('error', () => reject(new Error(`TCP port ${port} is unavailable. Stop the conflicting server or change game_port.`)));
            server.listen({ host: '0.0.0.0', port, exclusive: true }, () => server.close(resolve));
        });
    }
}

async function main() {
    const options = readOptions();
    const detectedIndex = process.argv.indexOf('--detected-address');
    const address = options.server_address === 'auto' ? process.argv[detectedIndex + 1] : options.server_address;
    if ((options.server_address === 'auto' && detectedIndex < 0) || net.isIP(address ?? '') !== 4 || address === '0.0.0.0') {
        throw new Error('Set server_address in options.ini to the server PC LAN IPv4 address; automatic detection was unavailable.');
    }
    if (!/^\d+$/.test(options.game_port) || !/^\d+$/.test(options.xp_rate)) throw new Error('game_port and xp_rate must be positive whole numbers.');
    const gamePort = Number(options.game_port);
    const offset = gamePort - 43594;
    const ports = [gamePort, 43500 + offset, 45099 + offset, 43501 + offset, 8888 + offset, 8898 + offset];
    if (ports.some(port => !Number.isSafeInteger(port) || port < 1024 || port > 65535)) throw new Error('game_port must be between 35730 and 64030 so all companion ports remain valid.');
    if (!['0', '1'].includes(options.members)) throw new Error('members must be 0 or 1.');
    if (Number(options.xp_rate) < 1 || Number(options.xp_rate) > 1000) throw new Error('xp_rate must be between 1 and 1000.');
    const saved = fs.existsSync(statePath) ? JSON.parse(fs.readFileSync(statePath, 'utf8')) : {};
    const username = (options.account_username || saved.username || 'xboxplayer').toLowerCase();
    const password = options.account_password || (saved.username === username ? saved.password : '') || randomBytes(8).toString('hex');
    if (!/^[a-z0-9]{1,12}$/.test(username)) throw new Error('account_username must contain 1-12 letters or digits.');
    if (!/^[a-zA-Z0-9 _-]{1,20}$/.test(password)) throw new Error('account_password must contain 1-20 letters, digits, spaces, underscores, or hyphens.');
    await checkPorts(ports);

    const keyDirectory = path.join(engine, 'data', 'config');
    const privatePath = path.join(keyDirectory, 'private.pem');
    const publicPath = path.join(keyDirectory, 'public.pem');
    if (fs.existsSync(privatePath) !== fs.existsSync(publicPath)) throw new Error('Only one RSA key exists. Restore the matching key pair from your backup.');
    fs.mkdirSync(keyDirectory, { recursive: true });
    fs.mkdirSync(runtime, { recursive: true });
    if (!fs.existsSync(privatePath)) {
        const keys = generateKeyPairSync('rsa', { modulusLength: 1024, publicExponent: 65537,
            privateKeyEncoding: { type: 'pkcs1', format: 'pem' }, publicKeyEncoding: { type: 'spki', format: 'pem' } });
        atomicWrite(privatePath, keys.privateKey);
        atomicWrite(publicPath, keys.publicKey);
    }
    const privateKey = createPrivateKey(fs.readFileSync(privatePath));
    const publicKey = createPublicKey(privateKey);
    const expected = publicKey.export({ type: 'spki', format: 'der' });
    const actual = createPublicKey(fs.readFileSync(publicPath)).export({ type: 'spki', format: 'der' });
    if (!expected.equals(actual)) throw new Error('The local RSA key files do not match. Restore the matching pair from your backup.');
    const jwk = publicKey.export({ format: 'jwk' });

    const databasePath = path.join(engine, 'db.sqlite');
    const freshDatabase = !fs.existsSync(databasePath);
    const database = new Database(databasePath, { create: freshDatabase, readwrite: true });
    try {
        if (freshDatabase) database.exec(fs.readFileSync(path.join(engine, 'prisma', 'singleworld', 'migrations', '20251229170623_clean', 'migration.sql'), 'utf8'));
        const account = database.query('SELECT password FROM account WHERE username = ?').get(username);
        if (account && !bcrypt.compareSync(password.toLowerCase(), account.password)) {
            throw new Error('The configured password does not match this existing account. Restore the previous setting, leave it blank to reuse saved setup, or choose a new username.');
        }
        if (!account) database.query('INSERT INTO account (username, password, members) VALUES (?, ?, ?)').run(username, bcrypt.hashSync(password.toLowerCase(), 10), Number(options.members));
    } finally { database.close(); }

    atomicWrite(statePath, JSON.stringify({ username, password }, null, 2) + '\n');
    const environment = {
        EASY_STARTUP: true, WEBSITE_REGISTRATION: false,
        WEB_HOST: '127.0.0.1', WEB_PORT: ports[4], WEB_MANAGEMENT_HOST: '127.0.0.1', WEB_MANAGEMENT_PORT: ports[5],
        ENGINE_REVISION: 225, NODE_ID: 10, NODE_PORT: gamePort, NODE_MEMBERS: options.members === '1',
        NODE_AUTO_SUBSCRIBE_MEMBERS: true, NODE_XPRATE: Number(options.xp_rate), NODE_PRODUCTION: true,
        NODE_DEBUG: false, NODE_DEBUG_PROFILE: false, NODE_DEBUG_SOCKET: false, NODE_SUBMIT_INPUT: false, NODE_PROFILE: 'main',
        LOGIN_SERVER: true, LOGIN_HOST: '127.0.0.1', LOGIN_BIND_HOST: '127.0.0.1', LOGIN_PORT: ports[1],
        FRIEND_SERVER: true, FRIEND_HOST: '127.0.0.1', FRIEND_BIND_HOST: '127.0.0.1', FRIEND_PORT: ports[2],
        LOGGER_SERVER: true, LOGGER_HOST: '127.0.0.1', LOGGER_BIND_HOST: '127.0.0.1', LOGGER_PORT: ports[3],
        DB_BACKEND: 'sqlite', BUILD_STARTUP: false, BUILD_SRC_DIR: '../content'
    };
    atomicWrite(path.join(engine, '.env'), Object.entries(environment).map(([key, value]) => `${key}=${value}`).join('\n') + '\n');
    const config = {
        socketip: address, portoff: offset, nodeid: 1, lowmem: 1, username, password,
        // The inherited client INI parser inverts this field: 0 means members.
        members: options.members === '1' ? 0 : 1,
        remember_username: 1, remember_password: 1,
        rsa_exponent: Buffer.from(jwk.e, 'base64url').toString('hex'), rsa_modulus: Buffer.from(jwk.n, 'base64url').toString('hex'),
        show_performance: 1, chat_era: 2, allow_commands: 0, allow_debugprocs: 0
    };
    atomicWrite(path.join(root, 'Xbox-config.ini'), '# Private local configuration. Copy beside default.xbe as config.ini.\n' + Object.entries(config).map(([key, value]) => `${key} = ${value}`).join('\n') + '\n');
    console.log('Configuration ready. Copy Xbox-config.ini into the Xbox game folder as config.ini.');
}

try { await main(); } catch (error) {
    console.error(`Setup failed: ${error.message}`);
    process.exitCode = 1;
}
