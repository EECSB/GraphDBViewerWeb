//Playwright e2e configuration. Starts the Blazor WASM app itself (dotnet run on port 5000 —
//the same command the manual preview workflow uses) and runs the specs in e2e/ against it.
//The first startup compiles the app, so the webServer timeout is generous.
const { defineConfig, devices } = require('@playwright/test');

module.exports = defineConfig({
    testDir: './e2e',
    timeout: 90000,
    expect: { timeout: 15000 },
    fullyParallel: true,
    reporter: [['list']],
    use: {
        baseURL: 'http://localhost:5000',
        viewport: { width: 1400, height: 900 },
        trace: 'on-first-retry'
    },
    projects: [
        {
            name: 'chromium',
            use: { ...devices['Desktop Chrome'] }
        }
    ],
    webServer: {
        command: 'dotnet run --no-launch-profile --project GraphDBViewerWeb',
        url: 'http://localhost:5000',
        timeout: 300000,
        reuseExistingServer: !process.env.CI
    }
});
