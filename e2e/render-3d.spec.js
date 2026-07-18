//3D (three.js / 3d-force-graph) rendering: the imported graph renders into the WebGL canvas
//and the corner X/Y/Z axis gizmo overlay is built and driven by the camera.
const { test, expect } = require('@playwright/test');
const { gotoApp, loadSampleGraph, switchTo3d, SAMPLE_NODE_COUNT } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);
    await loadSampleGraph(page);
    await switchTo3d(page, SAMPLE_NODE_COUNT);
});

test('renders the graph in the 3D view', async ({ page }) => {
    await expect(page.locator('#graph3d canvas').first()).toBeVisible();

    const counts = await page.evaluate(() => {
        const data = window.graph3DInterop.instance.graphData();

        return { nodes: data.nodes.length, links: data.links.length };
    });

    expect(counts.nodes).toBe(SAMPLE_NODE_COUNT);
    expect(counts.links).toBe(5);
});

test('shows the X/Y/Z axis gizmo overlay', async ({ page }) => {
    const gizmo = page.locator('#graph3d svg');
    await expect(gizmo).toHaveCount(1);
    await expect(gizmo.locator('line')).toHaveCount(3);
    await expect(gizmo.locator('text')).toHaveText(['X', 'Y', 'Z']);

    //The camera-projection loop has positioned the axis endpoints (x2 is only set by
    //updateAxisIndicator, not by setupAxisIndicator).
    await expect(gizmo.locator('line').first()).toHaveAttribute('x2', /.+/);
});
