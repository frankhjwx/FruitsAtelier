const { test } = require('node:test');
const assert = require('node:assert/strict');
const validate = require('./find-desktop-validation.cjs');

function fixture() {
  const run = { id: 1, event: 'push', head_branch: 'dev', conclusion: 'success',
    head_repository: { full_name: 'owner/repo' }, head_sha: 'source', html_url: 'https://example.test/run/1' };
  const jobs = [
    { name: 'windows', conclusion: 'success', steps: [
      { name: 'Existing platform-independent and editor regression', conclusion: 'success' },
    ] },
    { name: 'macos', conclusion: 'success', steps: [{ name: 'Mac regression', conclusion: 'success' }] },
  ];
  const outputs = {};
  const messages = [];
  const core = { setOutput: (key, value) => outputs[key] = value,
    notice: text => messages.push(text), info() {}, warning() {},
    summary: { addRaw() { return this; }, async write() {} } };
  const github = { rest: {
    actions: { async listWorkflowRuns() { return { data: { workflow_runs: [run] } }; },
      listJobsForWorkflowRun() {} },
    git: { async getCommit() { return { data: { tree: { sha: 'tree' } } }; } },
  }, async paginate() { return jobs; } };
  return { run, jobs, outputs, messages, args: { github, core, tree: 'tree',
    context: { repo: { owner: 'owner', repo: 'repo' }, runId: 2 } } };
}

test('identical merge tree reuses a complete dev run even with a different commit SHA', async () => {
  const f = fixture();
  await validate(f.args);
  assert.equal(f.outputs.reused, 'true');
  assert.match(f.messages[0], /https:\/\/example.test\/run\/1/);
});

for (const [name, change] of Object.entries({
  'changed merge tree': f => f.args.tree = 'different',
  'failed run': f => f.run.conclusion = 'failure',
  'cancelled run': f => f.run.conclusion = 'cancelled',
  'unfinished run': f => f.run.conclusion = null,
  'PR run': f => f.run.event = 'pull_request',
  'other branch': f => f.run.head_branch = 'main',
  'fork source': f => f.run.head_repository.full_name = 'fork/repo',
  'current run': f => f.run.id = 2,
  'missing Mac job': f => f.jobs.pop(),
  'failed platform job': f => f.jobs[0].conclusion = 'failure',
  'reused platform step': f => f.jobs[0].steps[0].conclusion = 'skipped',
  'missing history': f => f.args.github.rest.actions.listWorkflowRuns = async () => ({ data: { workflow_runs: [] } }),
  'API failure': f => f.args.github.rest.actions.listWorkflowRuns = async () => { throw new Error('403'); },
})) {
  test(`${name} falls back to regression`, async () => {
    const f = fixture(); change(f);
    await validate(f.args);
    assert.equal(f.outputs.reused, 'false');
  });
}

test('continues past a reuse-only run to original executed evidence', async () => {
  const f = fixture();
  f.args.github.rest.actions.listWorkflowRuns = async () => ({ data: {
    workflow_runs: [{ ...f.run, id: 3 }, f.run],
  } });
  f.args.github.paginate = async (_, params) => params.run_id === 3 ? [] : f.jobs;
  await validate(f.args);
  assert.equal(f.outputs.reused, 'true');
});
