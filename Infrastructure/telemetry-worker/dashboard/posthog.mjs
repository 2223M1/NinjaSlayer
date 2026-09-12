import { parseData } from './data.mjs';

export const QUERY_HOSTS = ['https://us.posthog.com', 'https://eu.posthog.com'];

export async function loadTelemetry({ host, projectId, key }, fetcher = fetch) {
  if (!QUERY_HOSTS.includes(host) || !/^\d+$/.test(projectId) || !key)
    throw new Error('请配置 PostHog 区域、项目 ID 和具有查询权限的个人 API key。');
  const results = [];
  const pageSize = 1000, limit = 50000;
  const before = new Date().toISOString().replace('T', ' ').slice(0, 23);
  const literal = value => `'${value.replaceAll('\\', '\\\\').replaceAll("'", "\\'")}'`;
  let cursor = '';
  while (results.length < limit) {
    const response = await fetcher(`${host}/api/projects/${projectId}/query/`, {
      method: 'POST', signal: AbortSignal.timeout(90000),
      headers: { Authorization: `Bearer ${key}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: { kind: 'HogQLQuery', query:
        `SELECT uuid, timestamp, properties FROM events WHERE event = 'run_history.completed' AND properties.applicant_id = 'NinjaSlayer' AND timestamp < ${literal(before)} ${cursor} ORDER BY timestamp DESC, uuid DESC LIMIT ${pageSize}` } }),
    });
    if (!response.ok) throw new Error(`PostHog 查询失败（HTTP ${response.status}）。请检查项目 ID 与个人 key 的查询权限。`);
    const data = parseData(await response.text());
    if (!Array.isArray(data.results)) throw new Error('PostHog 没有返回已完成的查询结果。');
    results.push(...data.results);
    if (data.results.length < pageSize) return { results, truncated: false };
    const [id, timestamp] = data.results.at(-1);
    if (typeof id !== 'string' || typeof timestamp !== 'string') throw new Error('PostHog 分页缺少事件 ID 或时间。');
    // Late uploads must not shift offsets and make us skip already existing events.
    cursor = `AND (timestamp < ${literal(timestamp)} OR (timestamp = ${literal(timestamp)} AND uuid < ${literal(id)}))`;
  }
  return { results, truncated: true };
}
