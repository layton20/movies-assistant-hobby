// promptfoo afterEach hook: pushes each test's assertion results to Langfuse as scores on the
// trace the API produced for that request (trace ID comes from the SSE "trace" event, see
// transformResponse in promptfooconfig.yaml). One BOOLEAN score per assertion `metric`, plus
// `eval-pass` for the test as a whole. In Langfuse: filter scores by name = 0 to find failures.
const baseUrl = process.env.LANGFUSE_BASE_URL || 'https://cloud.langfuse.com';
const publicKey = process.env.LANGFUSE_PUBLIC_KEY;
const secretKey = process.env.LANGFUSE_SECRET_KEY;

const MAX_COMMENT = 500;
let warnedMissingKeys = false;

async function postScore(score) {
  const auth = Buffer.from(`${publicKey}:${secretKey}`).toString('base64');
  const res = await fetch(`${baseUrl}/api/public/scores`, {
    method: 'POST',
    headers: { Authorization: `Basic ${auth}`, 'Content-Type': 'application/json' },
    body: JSON.stringify({ ...score, dataType: 'BOOLEAN' }),
  });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
}

function scoresFor(result, traceId) {
  const description = result.description || result.testCase?.description || '';
  const byMetric = new Map();

  for (const c of result.gradingResult?.componentResults ?? []) {
    const metric = c.assertion?.metric;
    if (!metric) continue; // unnamed assertions still count towards eval-pass
    const entry = byMetric.get(metric) ?? { pass: true, reasons: [] };
    entry.pass &&= c.pass;
    if (!c.pass && c.reason) entry.reasons.push(c.reason);
    byMetric.set(metric, entry);
  }

  const comment = (reasons) => [description, ...reasons].join(' | ').slice(0, MAX_COMMENT);
  const scores = [...byMetric].map(([name, { pass, reasons }]) => ({ name, pass, comment: comment(reasons) }));
  scores.push({ name: 'eval-pass', pass: result.success, comment: comment([]) });

  // Deterministic id: re-posting for the same trace updates the score instead of duplicating it
  return scores.map(({ name, pass, comment }) => ({
    id: `${traceId}-${name}`,
    traceId,
    name,
    value: pass ? 1 : 0,
    comment,
  }));
}

async function afterEach({ result }) {
  const traceId = result.response?.metadata?.traceId;
  // Cached responses carry the trace ID of an old run; scoring it would mislabel that trace
  if (!traceId || result.response?.cached) return;

  if (!publicKey || !secretKey) {
    if (!warnedMissingKeys) {
      warnedMissingKeys = true;
      console.warn('[langfuse-scores] LANGFUSE_PUBLIC_KEY / LANGFUSE_SECRET_KEY not set; skipping score push');
    }
    return;
  }

  // Never fail the eval over reporting
  await Promise.all(
    scoresFor(result, traceId).map((s) =>
      postScore(s).catch((e) => console.warn(`[langfuse-scores] ${s.name} failed: ${e.message}`)),
    ),
  );
}

module.exports = { afterEach, scoresFor };
