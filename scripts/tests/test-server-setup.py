"""Exercise server initialization in disposable fixtures, without starting the game.

Pass --bun and --engine for an installed Bun executable and an existing engine
dependency directory. Only bcrypt and the migration SQL are read from that engine.
"""
import argparse
from contextlib import closing, contextmanager
import os
from pathlib import Path
import shutil
import socket
import sqlite3
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[2]
PACKAGE = ROOT / 'server-package'
MIGRATION = Path('prisma/singleworld/migrations/20251229170623_clean/migration.sql')
DEFAULTS = {
    'server_address': 'auto', 'game_port': '43594', 'members': '1', 'xp_rate': '1',
    'account_username': '', 'account_password': '',
}


def check(condition, message):
    if not condition:
        raise AssertionError(message)


def companion_ports(game_port):
    offset = game_port - 43594
    return [game_port, 43500 + offset, 45099 + offset, 43501 + offset,
            8888 + offset, 8898 + offset]


def available_port():
    for game_port in range(60000, 64000, 17):
        probes = []
        try:
            for port in companion_ports(game_port):
                probe = socket.socket()
                probes.append(probe)
                probe.bind(('0.0.0.0', port))
            return game_port
        except OSError:
            continue
        finally:
            for probe in probes:
                probe.close()
    raise RuntimeError('No unused test port set was found.')


def read_ini(path):
    return dict((key.strip(), value.strip()) for key, value in
                (line.split('=', 1) for line in path.read_text().splitlines()
                 if line.strip() and not line.lstrip().startswith(('#', ';'))))


class Fixture:
    def __init__(self, root, bun, engine_source):
        self.root = root
        self.bun = bun
        self.engine = root / 'Server225/engine'
        self.runtime = root / 'Server225/runtime'
        self.options = DEFAULTS | {'game_port': str(available_port())}
        self.environment = os.environ | {
            'RS2_TEST_BCRYPT_ENTRY': str(engine_source / 'node_modules/bcrypt'),
        }
        (root / 'scripts').mkdir(parents=True)
        shutil.copyfile(PACKAGE / 'scripts/initialize-server.mjs', root / 'scripts/initialize-server.mjs')
        (self.engine / MIGRATION).parent.mkdir(parents=True)
        shutil.copyfile(engine_source / MIGRATION, self.engine / MIGRATION)
        (self.engine / 'package.json').write_text('{"private":true}\n')
        proxy = self.engine / 'node_modules/bcrypt'
        proxy.mkdir(parents=True)
        (proxy / 'package.json').write_text('{"main":"index.cjs"}\n')
        (proxy / 'index.cjs').write_text('module.exports = require(process.env.RS2_TEST_BCRYPT_ENTRY);\n')
        self.write_options()

    def write_options(self):
        (self.root / 'options.ini').write_text(''.join(f'{key} = {value}\n' for key, value in self.options.items()))

    def run(self, success=True, detect=True):
        command = [str(self.bun), 'run', str(self.root / 'scripts/initialize-server.mjs')]
        if detect:
            command += ['--detected-address', '127.0.0.1']
        result = subprocess.run(command, cwd=self.root, env=self.environment,
                                capture_output=True, text=True, timeout=30)
        check((result.returncode == 0) == success,
              'Initializer returned an unexpected result: ' + result.stdout + result.stderr)
        return result

    def snapshot(self):
        paths = [self.runtime / 'setup.local.json', self.engine / '.env',
                 self.engine / 'db.sqlite', self.root / 'Xbox-config.ini',
                 self.engine / 'data/config/private.pem', self.engine / 'data/config/public.pem']
        return {str(path.relative_to(self.root)): path.read_bytes() for path in paths if path.exists()}

    def verify_crypto(self):
        verifier = self.root / 'scripts/verify-setup.mjs'
        verifier.write_text('''import fs from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';
import { createPublicKey } from 'node:crypto';
import { Database } from 'bun:sqlite';
const root = process.cwd();
const engine = path.join(root, 'Server225', 'engine');
const bcrypt = createRequire(path.join(engine, 'package.json'))('bcrypt');
const config = Object.fromEntries(fs.readFileSync('Xbox-config.ini', 'utf8').split(/\\r?\\n/)
    .filter(line => line.includes('=')).map(line => line.split('=').map(part => part.trim())));
const db = new Database(path.join(engine, 'db.sqlite'), { readonly: true });
const account = db.query('SELECT password FROM account WHERE username = ?').get(config.username);
if (!account || !bcrypt.compareSync(config.password.toLowerCase(), account.password)) throw new Error('Login hash mismatch');
const publicKey = createPublicKey(fs.readFileSync(path.join(engine, 'data/config/private.pem')));
const jwk = publicKey.export({ format: 'jwk' });
if (Buffer.from(jwk.e, 'base64url').toString('hex') !== config.rsa_exponent ||
    Buffer.from(jwk.n, 'base64url').toString('hex') !== config.rsa_modulus) throw new Error('Client RSA key mismatch');
db.close();
''')
        result = subprocess.run([str(self.bun), 'run', str(verifier)], cwd=self.root,
                                env=self.environment, capture_output=True, text=True, timeout=15)
        check(result.returncode == 0, 'Generated login/key compatibility check failed.')


@contextmanager
def fixture(bun, engine_source):
    parent = (ROOT / 'build/server-setup-tests').resolve()
    parent.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix='package with spaces ', dir=parent))
    try:
        yield Fixture(directory, bun, engine_source)
    finally:
        target = directory.resolve()
        check(target != parent and target.is_relative_to(parent), 'Unsafe test cleanup path.')
        shutil.rmtree(target)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--bun', type=Path, required=True)
    parser.add_argument('--engine', type=Path, required=True)
    args = parser.parse_args()
    bun = args.bun.resolve(strict=True)
    engine_source = args.engine.resolve(strict=True)
    check((engine_source / 'node_modules/bcrypt').is_dir(), 'The engine bcrypt dependency is missing.')
    check((engine_source / MIGRATION).is_file(), 'The engine migration SQL is missing.')

    with fixture(bun, engine_source) as test:
        test.run(success=False, detect=False)
        check(not test.snapshot(), 'Failed address detection wrote persistent setup.')
        test.run()
        config = read_ini(test.root / 'Xbox-config.ini')
        check(config['socketip'] == '127.0.0.1' and config['nodeid'] == '1' and config['lowmem'] == '1', 'Client world configuration mismatch.')
        check(config['members'] == '0', 'Members mode does not match the inherited client parser.')
        check(config['username'] == 'xboxplayer' and len(config['password']) == 16, 'First-run account generation failed.')
        check(int(config['portoff']) + 43594 == int(test.options['game_port']), 'Client game port offset mismatch.')
        test.verify_crypto()
        snapshot = test.snapshot()
        test.run()
        check(test.snapshot() == snapshot, 'Repeated blank setup changed saved credentials, keys, or database.')

        test.options |= {'members': '0', 'xp_rate': '7'}
        test.write_options()
        test.run()
        config = read_ini(test.root / 'Xbox-config.ini')
        environment = read_ini(test.engine / '.env')
        check(config['members'] == '1' and environment['NODE_MEMBERS'] == 'false', 'Free-world configuration mismatch.')
        check(environment['NODE_XPRATE'] == '7', 'XP multiplier was not applied.')
        expected_ports = companion_ports(int(test.options['game_port']))
        check([int(environment[key]) for key in ('NODE_PORT', 'LOGIN_PORT', 'FRIEND_PORT', 'LOGGER_PORT', 'WEB_PORT', 'WEB_MANAGEMENT_PORT')] == expected_ports,
              'Companion port offsets do not match the game port.')
        for key in ('WEB_HOST', 'WEB_MANAGEMENT_HOST', 'LOGIN_BIND_HOST', 'FRIEND_BIND_HOST', 'LOGGER_BIND_HOST'):
            check(environment[key] == '127.0.0.1', 'A supporting endpoint is not bound to loopback.')

        snapshot = test.snapshot()
        test.options['account_password'] = 'WrongTestPassword'
        test.write_options()
        test.run(success=False)
        check(test.snapshot() == snapshot, 'A rejected password changed existing account setup.')

        test.options |= {'account_username': 'TestPlayer', 'account_password': 'Mixed Pass_123-'}
        test.write_options()
        test.run()
        test.verify_crypto()
        with closing(sqlite3.connect(test.engine / 'db.sqlite')) as database:
            check(database.execute('SELECT count(*) FROM account').fetchone()[0] == 2, 'New account setup lost the existing account.')
        test.options |= {'account_username': '', 'account_password': ''}
        test.write_options()
        snapshot = test.snapshot()
        test.run()
        check(test.snapshot() == snapshot, 'Blank options did not retain the last selected account.')

    with fixture(bun, engine_source) as test:
        for key, value in [('game_port', '35729'), ('game_port', '64031'), ('xp_rate', '0'),
                           ('xp_rate', '1001'), ('members', '2'), ('account_username', 'name-too-long'),
                           ('account_password', 'x' * 21)]:
            previous = test.options[key]
            test.options[key] = value
            test.write_options()
            test.run(success=False)
            check(not test.snapshot(), 'Invalid options created persistent state.')
            test.options[key] = previous
        test.write_options()
        with socket.socket() as busy:
            busy.bind(('0.0.0.0', int(test.options['game_port'])))
            busy.listen()
            test.run(success=False)
            check(not test.snapshot(), 'A port conflict created persistent state.')

    print('PASS: isolated first-run, persistence, bcrypt/RSA, membership, ports, validation, and conflict checks.')
    print('No game server was started; fixture data was removed.')


if __name__ == '__main__':
    main()
