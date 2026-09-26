export class MockR2 {
  objects = new Map();
  writes = [];
  failNextSuffix = null;
  blockedPut = null;

  async put(key, value, options = {}) {
    if (this.blockedPut && key.endsWith(this.blockedPut.suffix)) {
      const blocked = this.blockedPut;
      this.blockedPut = null;
      blocked.startedResolve();
      await blocked.released;
    }
    if (this.failNextSuffix && key.endsWith(this.failNextSuffix)) {
      this.failNextSuffix = null;
      throw new Error('injected R2 put failure');
    }
    this.writes.push(key);
    this.objects.set(key, { value: await new Response(value).arrayBuffer(), ...structuredClone(options) });
  }

  async get(key) {
    const object = this.objects.get(key);
    return object ? { ...object, body: new Response(object.value).body,
      arrayBuffer: async () => object.value.slice(0) } : null;
  }

  async head(key) { return this.get(key); }
  async delete(keys) {
    for (const key of Array.isArray(keys) ? keys : [keys]) this.objects.delete(key);
  }

  blockNextSuffix(suffix) {
    let startedResolve, release;
    const started = new Promise(resolve => { startedResolve = resolve; });
    const released = new Promise(resolve => { release = resolve; });
    this.blockedPut = { suffix, startedResolve, released };
    return { started, release };
  }
}
