import { builtinModels } from '@earendil-works/pi-ai/providers/all';

export function createCredentialStore(initial, persist) {
  let credentials = structuredClone(initial ?? {});
  let pending = Promise.resolve();
  const serialize = (fn) => {
    const task = pending.then(fn);
    pending = task.catch(() => {});
    return task;
  };
  return {
    async read(id, { signal } = {}) {
      signal?.throwIfAborted();
      await pending;
      return structuredClone(credentials[id]);
    },
    async list({ signal } = {}) {
      signal?.throwIfAborted();
      await pending;
      return Object.entries(credentials).map(([providerId, value]) => ({ providerId, type: value.type }));
    },
    modify(id, fn, { signal } = {}) {
      return serialize(async () => {
        signal?.throwIfAborted();
        const next = await fn(structuredClone(credentials[id]));
        if (next !== undefined) {
          const updated = { ...credentials, [id]: structuredClone(next) };
          // The host holds a cross-process file lock for the entire command.
          // Await durable DPAPI persistence before acknowledging a rotated token.
          await persist(updated);
          credentials = updated;
        }
        return structuredClone(credentials[id]);
      });
    },
    delete(id, { signal } = {}) {
      return serialize(async () => {
        signal?.throwIfAborted();
        const updated = { ...credentials };
        delete updated[id];
        await persist(updated);
        credentials = updated;
      });
    },
    async flush() { await pending; },
  };
}

export function oauthProviders(models) {
  return models.getProviders().filter((provider) => provider.auth.oauth);
}

export async function execute(request, interaction, persist, factory = builtinModels) {
  const store = createCredentialStore(request.credentials, persist);
  const models = factory({ credentials: store });
  try {
    return await executeCommand(request, interaction, store, models);
  } finally {
    // Models races caller cancellation against refresh, but a refresh that has
    // already started may rotate credentials. Drain it before exiting the host.
    await store.flush();
  }
}

async function executeCommand(request, interaction, store, models) {
  const options = { signal: interaction.signal };
  if (request.command === 'providers') {
    const stored = await store.list(options);
    return oauthProviders(models).map((provider) => ({
      id: provider.id,
      name: provider.auth.oauth.name,
      loggedIn: stored.some((entry) => entry.providerId === provider.id),
    }));
  }

  const provider = models.getProvider(request.provider);
  if (!provider?.auth.oauth) throw new Error('This provider does not support pi OAuth.');
  if (request.command === 'login') {
    await models.login(provider.id, 'oauth', interaction, {
      agentName: 'ReciteHelper',
      getDeviceId: () => request.deviceId,
    });
    return true;
  }
  if (request.command === 'logout') {
    await models.logout(provider.id, options);
    return true;
  }

  const credential = await store.read(provider.id, options);
  if (credential) {
    const refreshed = await models.refresh({ providers: [provider.id], ...options });
    interaction.signal?.throwIfAborted();
    if (refreshed.errors?.size) {
      interaction.notify({ type: 'progress', message: 'Model catalog refresh failed; using pi’s last-known catalog.' });
    }
  }
  const available = credential ? await models.getAvailable(provider.id, options) : models.getModels(provider.id);
  if (request.command === 'models') {
    return available.map((model) => ({ id: model.id, name: model.name }));
  }
  if (request.command !== 'chat') throw new Error('Unknown pi bridge command.');
  if (!credential) throw new Error('Please sign in to this provider again.');
  const model = available.find((candidate) => candidate.id === request.model);
  if (!model) throw new Error('This model is not available for the selected provider.');
  const response = await models.completeSimple(model, {
    systemPrompt: request.instructions ?? 'You are an assistant who is good at extracting knowledge.',
    messages: [{ role: 'user', content: request.prompt, timestamp: Date.now() }],
  }, { signal: interaction.signal, transport: 'sse' });
  if (response.stopReason === 'error' || response.stopReason === 'aborted') {
    throw new Error(response.errorMessage || 'The model request failed or was cancelled.');
  }
  if (response.stopReason === 'length') throw new Error('The model response was truncated. Choose a model with a larger output limit.');
  const text = response.content.filter((block) => block.type === 'text').map((block) => block.text).join('');
  if (!text.trim()) throw new Error('The model did not return any text.');
  return text;
}

export function safeError(error, credentials) {
  let message = error instanceof Error ? error.message : 'pi operation failed.';
  const scrub = (value) => {
    if (typeof value === 'string' && value.length >= 8) message = message.split(value).join('[redacted]');
    else if (value && typeof value === 'object') Object.values(value).forEach(scrub);
  };
  scrub(credentials);
  return message.replace(/Bearer\s+\S+/gi, 'Bearer [redacted]').replace(/sk-[\w-]+/g, '[redacted]');
}
