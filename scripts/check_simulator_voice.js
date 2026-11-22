const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');

const root = path.resolve(__dirname, '..');
const simulator = process.env.CHIPI_SIMULATOR_UDID || 'F00BA7A6-BBDF-4C1D-960C-CD299A39D614';
const app = path.join(root, 'build/XcodeSimulator-live/Build/Products/Release-iphonesimulator/Chipi.app');
const logs = fs.mkdtempSync(path.join(root, 'build/voice-check-'));
fs.chmodSync(logs, 0o755);
const wav = fs.readFileSync(path.join(root, 'backend/src/main/resources/demo.wav')).toString('base64');
const pcm = Buffer.alloc(16000);
for (let frame = 0; frame < 8000; frame++) {
    pcm.writeInt16LE(Math.round(6000 * Math.sin(frame * 2 * Math.PI * 330 / 16000)), frame * 2);
}
const event = value => JSON.stringify(value) + '\n';
let recoveryRequests = 0;
let multiTurnRequests = 0;

const fixture = http.createServer((request, response) => {
    const body = [];
    request.on('data', chunk => body.push(chunk));
    request.on('end', async () => {
        if (request.url.startsWith('/multiturn/')) {
            try {
                const form = await new Response(Buffer.concat(body), { headers: { 'Content-Type': request.headers['content-type'] } }).formData();
                const historyText = form.get('history');
                const expected = [0, 1, 2, 0][multiTurnRequests];
                const history = historyText ? JSON.parse(historyText).turns : [];
                if (!form.get('audio') || expected === undefined || !Array.isArray(history) || history.length !== expected ||
                    history.some(turn => turn.transcript !== 'fixture' || turn.reply !== 'fixture reply')) throw new Error('Invalid history');
                multiTurnRequests++;
            } catch (error) {
                response.writeHead(400);
                response.end();
                return;
            }
        }
        if (request.url.startsWith('/recovery/')) {
            const input = Buffer.concat(body).toString('utf8');
            if (!input.includes('fixture previous learner') || !input.includes('fixture previous tutor')) {
                response.writeHead(400);
                response.end();
                return;
            }
            if (++recoveryRequests === 1) {
                response.writeHead(200, { 'Content-Type': 'application/x-ndjson' });
                response.end(event({ type: 'ready' }) + event({ type: 'error', status: 429 }));
                return;
            }
        }
        if (request.url.startsWith('/legacy/')) {
            response.writeHead(200, { 'Content-Type': 'application/json' });
            response.end(JSON.stringify({ transcript: 'fixture', reply: 'fixture reply', audioBase64: wav, mode: 'demo' }));
            return;
        }
        response.writeHead(200, { 'Content-Type': 'application/x-ndjson' });
        const delayed = request.url.startsWith('/slow/');
        const gap = request.url.startsWith('/gap/');
        const limited = request.url.startsWith('/limited/');
        const fail = request.url.startsWith('/failed/') || limited;
        const first = event({ type: 'ready' }) +
            event({ type: 'metadata', transcript: 'fixture', reply: 'fixture reply', rate: 16000, mode: 'demo' }) +
            event({ type: 'audio', data: pcm.toString('base64') }) +
            (delayed || fail ? event({ type: 'audio', data: pcm.toString('base64') }) : '');
        for (let offset = 0; offset < first.length; offset += 113) response.write(first.slice(offset, offset + 113));
        const timer = setTimeout(() => {
            if (response.destroyed) return;
            if (fail) response.end(event({ type: 'error', status: limited ? 429 : 502 }));
            else response.end(event({ type: 'audio', data: pcm.toString('base64') }) + event({ type: 'complete' }));
        }, delayed ? 5000 : gap ? 1600 : 300);
        response.on('close', () => clearTimeout(timer));
    });
});

function simctl(args, env = process.env) {
    const result = spawnSync('xcrun', ['simctl', ...args], { cwd: root, env, encoding: 'utf8' });
    if (result.status !== 0) throw new Error(result.stderr || result.stdout || 'simctl failed');
}

function waitForLog(file, marker, timeout = 30000) {
    return new Promise((resolve, reject) => {
        const deadline = setTimeout(() => finish(new Error('Timed out waiting for ' + marker)), timeout);
        function finish(error, text) {
            clearTimeout(deadline);
            fs.unwatchFile(file, check);
            if (error) reject(error); else resolve(text);
        }
        function check() {
            if (!fs.existsSync(file)) return;
            const text = fs.readFileSync(file, 'utf8');
            if (text.includes(marker)) finish(null, text);
        }
        fs.watchFile(file, { interval: 200 }, check);
        check();
    });
}

async function check(name, variables, marker, validate) {
    const file = path.join(logs, name + '.log');
    const env = { ...process.env };
    for (const key of Object.keys(env)) if (key.startsWith('SIMCTL_CHILD_CHIPI_')) delete env[key];
    for (const [key, value] of Object.entries(variables)) env['SIMCTL_CHILD_' + key] = value;
    simctl(['launch', '--terminate-running-process', '--stdout=' + file, simulator, 'com.chipi.prototype'], env);
    const text = await waitForLog(file, marker);
    if (!validate(text)) throw new Error(name + ' failed; log: ' + file);
    console.log('PASS ' + name);
}

async function main() {
    await new Promise(resolve => fixture.listen(0, '127.0.0.1', resolve));
    const endpoint = 'http://127.0.0.1:' + fixture.address().port;
    simctl(['install', simulator, app]);
    await check('stream-tail', { CHIPI_CHECK_ROUND_TRIP: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/normal' },
        'Chipi round trip finished:', text => {
            const match = text.match(/Chipi playback frames: received=(\d+), lastAudio=(\d+), position=(\d+), empty=(\d+), rate=(\d+)/);
            if (!match || !text.includes('state Ready') || !text.includes('Chipi streaming voice finished')) return false;
            const [, received, last, position, empty, rate] = match.map(Number);
            console.log(`  PCM received=${received}, last=${last}, played=${position}, empty=${empty}, rate=${rate}`);
            return received === 16000 && position >= last && last >= received;
        });
    await check('stream-gap', { CHIPI_CHECK_ROUND_TRIP: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/gap' },
        'Chipi round trip finished:', text => {
            const match = text.match(/Chipi playback frames: received=(\d+), lastAudio=(\d+), position=(\d+), empty=(\d+), rate=(\d+)/);
            if (!match || !text.includes('state Ready')) return false;
            const [, received, last, position, , rate] = match.map(Number);
            console.log(`  PCM gap=${last - received} frames, rate=${rate}`);
            return position >= last && last - received <= rate / 10;
        });
    await check('legacy-wav', { CHIPI_CHECK_ROUND_TRIP: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/legacy' },
        'Chipi round trip finished:', text => text.includes('Chipi voice finished') && text.includes('state Ready'));
    await check('provider-error', { CHIPI_CHECK_ROUND_TRIP: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/failed' },
        'Chipi round trip finished:', text => text.includes('Chipi streaming voice started') &&
            text.includes('streamError=502') && text.includes('The voice provider is unavailable.') && !text.includes('Chipi streaming voice finished'));
    await check('provider-rate-limit', { CHIPI_CHECK_ROUND_TRIP: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/limited' },
        'Chipi round trip finished:', text => text.includes('streamError=429') &&
            text.includes('The voice server is busy.') && !text.includes('Chipi streaming voice finished'));
    await check('turn-recovery', { CHIPI_CHECK_RECOVERY: '1', CHIPI_CHECK_MOCK_MIC: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/recovery' },
        'Chipi recovery check:', text => recoveryRequests === 2 && text.includes('Chipi recovery check: PASS') &&
            text.includes('streamError=429') && text.includes('Chipi fixture microphone rearmed'));
    await check('multi-turn', { CHIPI_CHECK_MULTITURN: '1', CHIPI_CHECK_MOCK_MIC: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/multiturn' },
        'Chipi multi-turn check:', text => multiTurnRequests === 4 && text.includes('Chipi multi-turn check: PASS'));
    for (const mode of ['listening', 'no-speech', 'speak-again', 'thinking', 'speaking', 'cancel-response', 'interrupt', 'interrupt-muted']) {
        await check('controls-' + mode, { CHIPI_CHECK_CONTROLS: mode, CHIPI_CHECK_MOCK_MIC: '1',
            CHIPI_CHECK_ENDPOINT: endpoint + '/slow' }, 'Chipi controls check ' + mode + ':',
            text => text.includes('Chipi controls check ' + mode + ': PASS'));
    }
    await check('controls-permission-background', { CHIPI_CHECK_CONTROLS: 'permission-background',
        CHIPI_CHECK_MOCK_MIC: '1', CHIPI_CHECK_MOCK_MIC_DELAY: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/slow' },
        'Chipi controls check permission-background:', text => text.includes('Chipi controls check permission-background: PASS') &&
            !text.includes('Chipi fixture microphone rearmed'));
    await check('controls-permission-dialog', { CHIPI_CHECK_CONTROLS: 'permission-dialog',
        CHIPI_CHECK_MOCK_MIC: '1', CHIPI_CHECK_MOCK_MIC_DELAY: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/slow' },
        'Chipi controls check permission-dialog:', text => text.includes('Chipi controls check permission-dialog: PASS') &&
            text.includes('Chipi fixture microphone rearmed'));
    for (const mode of ['permission-end', 'permission-mute']) {
        await check('controls-' + mode, { CHIPI_CHECK_CONTROLS: mode, CHIPI_CHECK_MOCK_MIC: '1',
            CHIPI_CHECK_MOCK_MIC_DELAY: '1', CHIPI_CHECK_ENDPOINT: endpoint + '/slow' },
            'Chipi controls check ' + mode + ':', text => text.includes('Chipi controls check ' + mode + ': PASS'));
    }
    for (const mode of ['thinking', 'stop', 'pause']) {
        await check('cancel-' + mode, { CHIPI_CHECK_STREAM_CANCEL: mode, CHIPI_CHECK_ENDPOINT: endpoint + '/slow' },
            'Chipi streaming cancellation check:', text => text.includes('Chipi streaming cancellation check: PASS') &&
                (mode === 'thinking' || text.includes('Chipi streaming voice started')));
    }
    console.log('Logs: ' + logs);
}

main().catch(error => { console.error('FAIL ' + error.message + '; logs: ' + logs); process.exitCode = 1; })
    .finally(() => fixture.close());