//2D canvas editing tools added in the icon/UX pass: grid mode (a grid overlay plus snap-to-grid)
//and the confirm-before-clear guard on the Clear-results button. All offline — the graph is loaded
//through the Import panel (DOT), so no database is needed.
const { test, expect } = require('@playwright/test');
const { gotoApp, loadSampleGraph, cyScreenPoint, SAMPLE_NODE_COUNT } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);
    await loadSampleGraph(page);
});

//The inline background-image the grid overlay sets on the Cytoscape container (empty when off).
function gridBackground(page) {
    return page.evaluate(() => document.getElementById('cyGraph').style.backgroundImage);
}

//Node count currently in the 2D view, read through the interop global.
function nodeCount(page) {
    return page.evaluate(() => JSON.parse(getCytoscapePositions()).length);
}

test('grid mode draws an overlay on the canvas and toggles off again', async ({ page }) => {
    expect(await gridBackground(page)).toBe('');

    await page.getByRole('button', { name: /Grid mode/ }).click();
    await expect.poll(() => gridBackground(page)).toContain('linear-gradient');

    await page.getByRole('button', { name: /Grid mode/ }).click();
    await expect.poll(() => gridBackground(page)).toBe('');
});

test('grid mode snaps a dragged node to the grid', async ({ page }) => {
    await page.getByRole('button', { name: /Grid mode/ }).click();
    await expect.poll(() => gridBackground(page)).toContain('linear-gradient');

    //Grab Alice and drag her toward the canvas center (kept on-canvas). On release the node snaps
    //to the 50-unit grid, so both coordinates land on exact multiples of 50 — a non-snapped float
    //never would, so this also proves the drag actually moved and snapped the node.
    const point = await cyScreenPoint(page, 'Alice');
    const box = await page.locator('#cyGraph').boundingBox();

    await page.mouse.move(point.x, point.y);
    await page.mouse.down();
    await page.mouse.move(box.x + box.width / 2 + 33, box.y + box.height / 2 + 17, { steps: 12 });
    await page.mouse.up();

    await expect.poll(async () => {
        const positions = JSON.parse(await page.evaluate(() => getCytoscapePositions()));
        const alice = positions.find(p => p.id === 'Alice');
        return alice ? (alice.x % 50 === 0 && alice.y % 50 === 0) : false;
    }).toBe(true);
});

test('clearing the canvas asks for confirmation — cancel keeps the graph, OK empties it', async ({ page }) => {
    //Dismissing the confirm leaves the graph untouched.
    page.once('dialog', dialog => dialog.dismiss());
    await page.getByRole('button', { name: /Clear results/ }).click();
    expect(await nodeCount(page)).toBe(SAMPLE_NODE_COUNT);

    //Accepting the confirm empties the canvas.
    page.once('dialog', dialog => dialog.accept());
    await page.getByRole('button', { name: /Clear results/ }).click();
    await expect.poll(() => nodeCount(page)).toBe(0);
});
