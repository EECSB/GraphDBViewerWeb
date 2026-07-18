//2D (Cytoscape) rendering and canvas interaction: a DOT graph imported offline renders on the
//canvas, clicking a node selects it into the property panel, clicking empty space deselects.
const { test, expect } = require('@playwright/test');
const { gotoApp, loadSampleGraph, cyPositions, cyScreenPoint, SAMPLE_NODE_COUNT } = require('./helpers');

test.beforeEach(async ({ page }) => {
    await gotoApp(page);
    await loadSampleGraph(page);
});

test('renders the imported DOT graph on the 2D canvas', async ({ page }) => {
    await expect(page.locator('#cyGraph canvas').first()).toBeVisible();

    const positions = await cyPositions(page);
    expect(positions).toHaveLength(SAMPLE_NODE_COUNT);
    expect(positions.map(p => p.id).sort()).toEqual(['Acme', 'Alice', 'Bob', 'Carol', 'Globex']);

    //Edges made it into the graph too (id format: source->target:label).
    const edge = await page.evaluate(() => getCytoscapeElementInfo('Alice->Bob:knows'));
    expect(edge).not.toBeNull();
    expect(edge.data.source).toBe('Alice');
    expect(edge.data.target).toBe('Bob');
});

test('clicking a node on the canvas selects it into the property panel', async ({ page }) => {
    const point = await cyScreenPoint(page, 'Alice');
    await page.mouse.click(point.x, point.y);

    const panel = page.locator('.card', { hasText: 'Component Properties' }).last();
    await expect(panel).toBeVisible();
    await expect(panel.getByText('node', { exact: true })).toBeVisible();
    await expect(panel.getByText('Alice')).toBeVisible();
});

test('clicking empty canvas space deselects the element', async ({ page }) => {
    const point = await cyScreenPoint(page, 'Bob');
    await page.mouse.click(point.x, point.y);
    await expect(page.getByText('Component Properties')).toBeVisible();

    //The layout is fitted with 50px padding, so the container's top-left corner is empty.
    const box = await page.locator('#cyGraph').boundingBox();
    await page.mouse.click(box.x + 8, box.y + 8);
    await expect(page.getByText('Component Properties')).toBeHidden();
});
