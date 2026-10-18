const { execFileSync } = require('node:child_process');

// Only a complete dev push run can supply evidence; PR runs can execute fork code.
module.exports = async ({ github, context, core, tree }) => {
  core.setOutput('reused', 'false');
  try {
    tree ??= execFileSync('git', ['rev-parse', 'HEAD^{tree}'], {
      cwd: process.env.GITHUB_WORKSPACE, encoding: 'utf8',
    }).trim();
    const { data } = await github.rest.actions.listWorkflowRuns({
      ...context.repo, workflow_id: 'desktop.yml', branch: 'dev',
      event: 'push', status: 'success', per_page: 30,
    });
    for (const run of data.workflow_runs) {
      if (run.id === context.runId || run.event !== 'push' || run.head_branch !== 'dev'
          || run.conclusion !== 'success' || run.head_repository?.full_name !== `${context.repo.owner}/${context.repo.repo}`) continue;
      const { data: commit } = await github.rest.git.getCommit({ ...context.repo, commit_sha: run.head_sha });
      if (commit.tree.sha !== tree) continue;
      const jobs = await github.paginate(github.rest.actions.listJobsForWorkflowRun, {
        ...context.repo, run_id: run.id, filter: 'latest', per_page: 100,
      });
      // Require actual regression execution, not a chain of jobs that reused evidence.
      const executed = (name, step) => jobs.some(job => job.name === name
        && job.conclusion === 'success'
        && job.steps?.some(item => item.name === step && item.conclusion === 'success'));
      if (!executed('windows', 'Existing platform-independent and editor regression')
          || !executed('macos', 'Mac regression')) continue;
      core.setOutput('reused', 'true');
      core.notice(`Identical source tree passed Windows and macOS regression: ${run.html_url}`);
      await core.summary.addRaw(`Reused desktop validation for source tree \`${tree}\`: ${run.html_url}\n`).write();
      return;
    }
    core.info('No completed desktop validation for this source tree; running regression.');
  } catch (error) {
    core.setOutput('reused', 'false');
    core.warning(`Cannot reuse desktop validation; running regression: ${error.message}`);
  }
};
