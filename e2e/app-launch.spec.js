//Smoke test: the Blazor WASM app boots in the browser and shows its initial chrome.
const { test, expect } = require('@playwright/test');
const { gotoApp } = require('./helpers');

test('the app boots and shows the top bar and connection controls', async ({ page }) => {
    await gotoApp(page);

    await expect(page.getByRole('heading', { name: 'Graph DB Viewer' })).toBeVisible();
    await expect(page.getByText('Web edition')).toBeVisible();
    await expect(page.getByRole('button', { name: /Disconnected/ })).toBeVisible();
    await expect(page.getByRole('button', { name: /Import \/ Export/ })).toBeVisible();
});
