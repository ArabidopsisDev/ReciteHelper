import { createInterface } from 'node:readline';
import { registerBunOAuthFlows } from '@earendil-works/pi-ai/bun-oauth';
import { execute, safeError } from './engine.mjs';

// pi deliberately hides Node-only OAuth imports from bundlers. Its exported
// loader registration embeds all official flows into this standalone bundle.
registerBunOAuthFlows();
const output = (message) => new Promise((resolve, reject) => {
  process.stdout.write(`${JSON.stringify(message)}\n`, (error) => error ? reject(error) : resolve());
});
console.log = console.info = console.warn = console.error = () => {};
const controller = new AbortController();
const pending = new Map();
let nextId = 0;
let active = false;
let currentCredentials = [];
const lines = createInterface({ input: process.stdin });

function rpc(type, payload, signal) {
  signal?.throwIfAborted();
  const id = ++nextId;
  return new Promise((resolve, reject) => {
    const abort = () => {
      pending.delete(id);
      void output({ type: 'cancel_prompt', id });
      reject(signal.reason ?? new Error('Login cancelled.'));
    };
    pending.set(id, {
      resolve(value) { signal?.removeEventListener('abort', abort); resolve(value); },
      reject(error) { signal?.removeEventListener('abort', abort); reject(error); },
    });
    signal?.addEventListener('abort', abort, { once: true });
    void output({ type, id, ...payload }).catch(reject);
  });
}

lines.on('line', (line) => {
  let message;
  try { message = JSON.parse(line); } catch { controller.abort(new Error('Invalid bridge input.')); return; }
  if (message.type === 'cancel') { controller.abort(new Error('Operation cancelled.')); return; }
  if (message.type === 'reply') {
    const waiter = pending.get(message.id);
    if (waiter) {
      pending.delete(message.id);
      if (message.error) waiter.reject(new Error(message.error));
      else waiter.resolve(message.value);
    }
    return;
  }
  if (active) return;
  active = true;
  currentCredentials = [message.credentials ?? {}];
  void (async () => {
    try {
      const result = await execute(message, {
        signal: controller.signal,
        prompt: ({ signal, ...prompt }) => rpc('prompt', { prompt }, signal ?? controller.signal),
        notify: (event) => { void output({ type: 'event', event }); },
      }, async (credentials) => {
        // Track rotated secrets for error redaction even if persistence fails.
        currentCredentials.push(credentials);
        await rpc('store', { credentials });
      });
      await output({ type: 'result', result });
      process.exit(0);
    } catch (error) {
      await output({ type: 'error', message: safeError(error, currentCredentials) });
      process.exit(1);
    }
  })();
});
lines.on('close', () => controller.abort(new Error('The host disconnected.')));
