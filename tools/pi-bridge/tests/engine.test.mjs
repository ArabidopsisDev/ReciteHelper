import test from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { copyFile, mkdtemp, unlink, rmdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { registerBunOAuthFlows } from '@earendil-works/pi-ai/bun-oauth';
import { createModels } from '@earendil-works/pi-ai/models';
import { builtinModels } from '@earendil-works/pi-ai/providers/all';
import { fauxProvider, fauxAssistantMessage } from '@earendil-works/pi-ai/providers/faux';
import { createCredentialStore, execute, oauthProviders, safeError } from '../src/engine.mjs';

const credential = (access = 'test-access-token', expires = Date.now() + 3600000) => ({
  type: 'oauth', access, refresh: 'test-refresh-token', expires,
});
const interaction = () => ({ signal: new AbortController().signal, notify() {}, async prompt() { return 'code'; } });
function fixture({ response, refresh, login, filterModels } = {}) {
  const faux = fauxProvider({ provider: 'test-oauth', models: [{ id: 'study' }, { id: 'other' }], tokensPerSecond: Infinity });
  faux.setResponses([response ?? fauxAssistantMessage('学习答案')]);
  const provider = { ...faux.provider, filterModels, auth: {
    ...faux.provider.auth,
    oauth: {
      name: 'Test OAuth',
      login: login ?? (async () => credential()),
      refresh: refresh ?? (async () => credential('rotated-access-token')),
      toAuth: async (stored) => ({ apiKey: stored.access, baseUrl: 'https://account.example/v1' }),
    },
  } };
  return { faux, factory: (options) => { const models = createModels(options); models.setProvider(provider); return models; } };
}
const chat = (credentials = { 'test-oauth': credential() }) => ({ command: 'chat', provider: 'test-oauth', model: 'study', prompt: '学习内容', instructions: '指导说明', credentials });

test('discovers every OAuth provider in the pinned official registry', async () => {
  const expected = oauthProviders(builtinModels()).map((provider) => provider.id);
  const actual = await execute({ command: 'providers', credentials: {} }, interaction(), async () => {});
  assert.deepEqual(actual.map((provider) => provider.id), expected);
  assert.equal(actual.length, 9);
  for (const provider of actual) {
    assert.equal(provider.loggedIn, false);
    const models = await execute({ command: 'models', provider: provider.id, credentials: {} }, interaction(), async () => {});
    assert.ok(models.length > 0, `${provider.id} chat catalog`);
    assert.ok(models.every((model) => model.id && model.name));
  }
});

test('metadata never contains credentials', async () => {
  const result = await execute({ command: 'providers', credentials: { anthropic: credential() } }, interaction(), async () => {});
  assert.equal(result.find((provider) => provider.id === 'anthropic').loggedIn, true);
  assert.ok(!JSON.stringify(result).includes('test-access-token'));
});

test('all official OAuth flow modules can derive request auth in the bundle registration', async () => {
  registerBunOAuthFlows();
  for (const provider of oauthProviders(builtinModels())) {
    const auth = await provider.auth.oauth.toAuth({ ...credential(), accountId: 'synthetic-account-id' });
    assert.ok(JSON.stringify(auth).includes('test-access-token'), provider.id);
  }
});

test('serializes parallel refreshes using the real Models auth resolution', async () => {
  let refreshCount = 0;
  const { factory } = fixture({ refresh: async () => {
    refreshCount++;
    await new Promise((resolve) => setTimeout(resolve, 15));
    return credential('rotated-token');
  } });
  const writes = [];
  const store = createCredentialStore({ 'test-oauth': credential('expired', 1) }, async (updated) => writes.push(updated));
  const models = factory({ credentials: store });
  const results = await Promise.all(Array.from({ length: 8 }, () => models.getAuth('test-oauth')));
  assert.equal(refreshCount, 1);
  assert.equal(writes.length, 1);
  assert.ok(results.every((result) => result.auth.apiKey === 'rotated-token'));
});

test('persists a rotated credential before completing a chat', async () => {
  const writes = [];
  const { factory } = fixture();
  assert.equal(await execute(chat({ 'test-oauth': credential('expired', 1) }), interaction(), async (updated) => writes.push(updated), factory), '学习答案');
  assert.equal(writes[0]['test-oauth'].access, 'rotated-access-token');
});

test('cancelled callers wait for an in-flight refresh to persist', async () => {
  const controller = new AbortController();
  let wrote = false;
  const { factory } = fixture({ refresh: async () => {
    controller.abort(new Error('User cancelled'));
    await new Promise((resolve) => setTimeout(resolve, 35));
    return credential('must-survive-cancellation');
  } });
  await assert.rejects(execute(chat({ 'test-oauth': credential('expired', 1) }), { ...interaction(), signal: controller.signal }, async (updated) => {
    assert.equal(updated['test-oauth'].access, 'must-survive-cancellation');
    wrote = true;
  }, factory));
  assert.equal(wrote, true);
});

test('failed persistence keeps the previous credential', async () => {
  const store = createCredentialStore({ account: credential('previous') }, async () => { throw new Error('Disk failed'); });
  await assert.rejects(store.modify('account', async () => credential('new-token')), /Disk failed/);
  assert.equal((await store.read('account')).access, 'previous');
  await assert.rejects(store.delete('account'), /Disk failed/);
  assert.equal((await store.read('account')).access, 'previous');
});

test('failed refresh preserves the account for retry', async () => {
  const { factory } = fixture({ refresh: async () => { throw new Error('invalid_grant'); } });
  const writes = [];
  await assert.rejects(execute(chat({ 'test-oauth': credential('expired', 1) }), interaction(), async (updated) => writes.push(updated), factory), /OAuth refresh failed/);
  assert.deepEqual(writes, []);
});

test('login uses Models.login and a stable app device ID', async () => {
  let loginOptions;
  const { factory } = fixture({ login: async (ui, options) => {
    loginOptions = options;
    ui.notify({ type: 'device_code', userCode: 'ABCD', verificationUri: 'https://example.com' });
    assert.equal(await ui.prompt({ type: 'text', message: 'Code' }), 'code');
    return credential();
  } });
  const writes = [];
  await execute({ command: 'login', provider: 'test-oauth', deviceId: 'stable-id', credentials: {} }, interaction(), async (updated) => writes.push(updated), factory);
  assert.equal(loginOptions.agentName, 'ReciteHelper');
  assert.equal(loginOptions.getDeviceId(), 'stable-id');
  assert.equal(writes[0]['test-oauth'].type, 'oauth');
});

test('logout removes only the selected provider', async () => {
  const { factory } = fixture();
  let saved;
  await execute({ command: 'logout', provider: 'test-oauth', credentials: { 'test-oauth': credential(), another: credential() } }, interaction(), async (updated) => { saved = updated; }, factory);
  assert.deepEqual(Object.keys(saved), ['another']);
});

test('routes instructions, text, and resolved account auth through Models', async () => {
  const { factory } = fixture({ response: (context, options, _state, model) => {
    assert.equal(context.messages[0].content, '指导说明');
    assert.equal(context.messages[0].role, 'system');
    assert.equal(context.messages[1].content, '学习内容');
    assert.equal(context.messages[1].role, 'user');
    assert.equal(options.apiKey, 'test-access-token');
    assert.equal(model.baseUrl, 'https://account.example/v1');
    assert.equal(options.transport, 'sse');
    return fauxAssistantMessage('完整答案');
  } });
  assert.equal(await execute(chat(), interaction(), async () => {}, factory), '完整答案');
});

test('account model filtering is respected', async () => {
  const { factory } = fixture({ filterModels: (models) => models.filter((model) => model.id === 'other') });
  await assert.rejects(execute(chat(), interaction(), async () => {}, factory), /not available/);
});

test('requires stored OAuth authorization instead of ambient credentials', async () => {
  const { factory } = fixture();
  await assert.rejects(execute(chat({}), interaction(), async () => {}, factory), /sign in/);
});

for (const reason of ['error', 'aborted', 'length']) {
  test(`rejects ${reason} model responses`, async () => {
    const { factory } = fixture({ response: fauxAssistantMessage('partial', { stopReason: reason, errorMessage: 'provider failure' }) });
    await assert.rejects(execute(chat(), interaction(), async () => {}, factory));
  });
}
test('rejects empty model responses', async () => {
  const { factory } = fixture({ response: fauxAssistantMessage('') });
  await assert.rejects(execute(chat(), interaction(), async () => {}, factory), /did not return/);
});
test('scrubs provider tokens from errors', () => {
  const message = safeError(new Error('secret-access secret-refresh Bearer abcdef sk-123456'), { p: { access: 'secret-access', refresh: 'secret-refresh' } });
  assert.ok(!message.includes('secret-access') && !message.includes('secret-refresh') && !message.includes('abcdef') && !message.includes('sk-123456'));
});

test('the standalone bundle works without its npm installation at runtime', async () => {
  const directory = await mkdtemp(join(tmpdir(), 'recitehelper-pi-bundle-'));
  const bundle = join(directory, 'bridge.mjs');
  try {
    await copyFile('dist/bridge.mjs', bundle);
    const child = spawn(process.execPath, [bundle], { cwd: directory, stdio: ['pipe', 'pipe', 'pipe'] });
    let stdout = '';
    child.stdout.on('data', (chunk) => { stdout += chunk; });
    child.stdin.write(`${JSON.stringify({ command: 'providers', credentials: {} })}\n`);
    const code = await new Promise((resolve, reject) => { child.on('error', reject); child.on('exit', resolve); });
    assert.equal(code, 0);
    assert.equal(JSON.parse(stdout).result.length, 9);
  } finally {
    await unlink(bundle);
    await rmdir(directory);
  }
});
