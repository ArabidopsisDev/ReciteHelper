import { createInterface } from 'node:readline';
const lines = createInterface({ input: process.stdin });
let request;
let stored = false;
const send = (message) => new Promise((resolve) => process.stdout.write(`${JSON.stringify(message)}\n`, resolve));
async function finish(result) { await send({ type: 'result', result }); process.exit(0); }
async function store() {
  stored = true;
  await send({ type: 'store', id: 2, credentials: {
    ...request.credentials, synthetic: { type: 'oauth', access: 'new-access-token', refresh: 'new-refresh-token', expires: Date.now() + 3600000, extra: { region: 'test' } },
  } });
}
lines.on('line', async (line) => {
  const message = JSON.parse(line);
  if (message.type === 'cancel') { await store(); return; }
  if (message.type === 'reply') {
    if (message.id === 1 && request.provider === 'manual') {
      if (message.value !== '用户授权码') process.exit(2);
      await store();
    }
    if (message.id === 2 && stored) await finish(request.command === 'chat' ? request.prompt : true);
    return;
  }
  request = message;
  if (message.command === 'providers') {
    await finish([{ id: 'synthetic', name: 'Synthetic', loggedIn: Boolean(message.credentials.synthetic) }]);
  } else if (message.command === 'login') {
    await send({ type: 'event', event: { type: 'device_code', userCode: 'TEST', verificationUri: 'https://example.com' } });
    await send({ type: 'prompt', id: 1, prompt: { type: 'manual_code', message: 'Enter a code' } });
    if (message.provider === 'callback') {
      await send({ type: 'cancel_prompt', id: 1 });
      await store();
    }
  } else if (message.command === 'chat') {
    if (message.model === 'rotate') await store();
    else if (message.model === 'cancel-refresh') { /* Wait for host cancellation; then persist the rotated token. */ }
    else await finish(message.prompt);
  }
});
