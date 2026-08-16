import { randomBytes } from 'node:crypto';
import { rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { basename, dirname, join } from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const frontendRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const runNamespace = randomBytes(12).toString('hex');
const runRoot = join(tmpdir(), `transcript-lab-e2e-${runNamespace}`);
const buildOutputDir = join(runRoot, 'dist');
const testOutputDir = join(runRoot, 'test-results');

if (dirname(runRoot) !== tmpdir() || basename(runRoot) !== `transcript-lab-e2e-${runNamespace}`) {
  throw new Error('Invalid E2E run namespace.');
}

function run(command, args, env = process.env) {
  return new Promise((resolve, reject) => {
    const child = spawn(command, args, {
      cwd: frontendRoot,
      env,
      stdio: 'inherit',
    });
    child.once('error', reject);
    child.once('exit', (code, signal) => {
      if (signal) {
        reject(new Error(`E2E child process exited on signal ${signal}.`));
        return;
      }

      resolve(code ?? 1);
    });
  });
}

let exitCode = 1;
try {
  const buildExitCode = await run('npm', ['run', 'build', '--', '--outDir', buildOutputDir]);
  if (buildExitCode === 0) {
    const playwrightEnvironment = {
      ...process.env,
      TRANSCRIPT_LAB_E2E_BUILD_DIR: buildOutputDir,
      TRANSCRIPT_LAB_E2E_OUTPUT_DIR: testOutputDir,
      TRANSCRIPT_LAB_E2E_RUN_NAMESPACE: runNamespace,
    };
    const playwrightCli = join(frontendRoot, 'node_modules', 'playwright', 'cli.js');
    exitCode = await run(process.execPath, [playwrightCli, 'test', ...process.argv.slice(2)], playwrightEnvironment);
  } else {
    exitCode = buildExitCode;
  }
} finally {
  await rm(runRoot, { force: true, recursive: true });
}

process.exitCode = exitCode;
