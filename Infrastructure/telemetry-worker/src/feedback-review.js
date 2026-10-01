import { UUID_PATTERN } from './validation.js';

export const feedbackReviewKey = id => {
  if (!UUID_PATTERN.test(id ?? '')) throw new Error('反馈编号无效。');
  return `feedback-review/${id}`;
};
export function validateFeedbackReview(value) {
  if (!value || !['unresolved', 'resolved'].includes(value.status)
      || typeof value.reply !== 'string' || value.reply.length > 5000
      || Object.keys(value).some(key => !['status', 'reply'].includes(key)))
    throw new Error('请选择有效的解决状态，回应不能超过 5000 个字符。');
  return { status: value.status, reply: value.reply.trim() };
}
export function readFeedbackReview(value) {
  if (value == null) return { status: 'unresolved', reply: '', updatedAt: null };
  if (typeof value === 'string') value = JSON.parse(value);
  if (value.schemaVersion === 1 && value.updatedAt === null && value.status === 'unresolved' && value.reply === '')
    return { status: 'unresolved', reply: '', updatedAt: null };
  if (value.schemaVersion !== 1 || typeof value.updatedAt !== 'string' || !Number.isFinite(Date.parse(value.updatedAt)))
    throw new Error('反馈处理记录损坏，不能按未解决状态覆盖。');
  return { ...validateFeedbackReview({ status: value.status, reply: value.reply }), updatedAt: value.updatedAt };
}
export function feedbackExpiresAt(metadata) {
  const expires = Date.parse(metadata.receivedAtUtc) + 180 * 86400_000;
  if (!Number.isFinite(expires) || expires <= Date.now()) throw new Error('此反馈已过期。');
  return Math.floor(expires / 1000);
}
