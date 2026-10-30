// Builds Sanderling/Sanderling.rui (the Sanderling toolbar) from the SVG icons in toolbar/icons.
//
// Rhino opens a .rui file with the same name as the plug-in that sits next to the .rhp, so the toolbar is
// installed together with the Yak package. The icons are rasterized to 16 / 24 / 32 px bitmap strips.
//
// Usage (needs Node.js and Playwright with Chromium):  node toolbar/build_rui.js
// The GUIDs below are fixed: keep them, so Rhino treats a new version as the same toolbar.

const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const root = path.join(__dirname, '..');
const iconDir = path.join(__dirname, 'icons');
const output = path.join(root, 'Sanderling', 'Sanderling.rui');

const RUI_GUID = '1c23a7cb-69a6-47b9-9557-f6cc1a51adc9';
const GROUP_GUID = '91e6b7cd-9f75-4237-99c1-ef8bdb7111fb';
const GROUP_ITEM_GUID = 'ed1d2ba9-f738-4e36-a4f5-e6e224b2c373';
const TOOLBAR_GUID = 'e98d1fde-5ffa-4a9c-bd9e-396b2cb7f588';

// One entry per button, in toolbar order
const buttons = [
  {
    icon: 'sanderling', command: 'Sanderling',
    item: 'dbce5810-7177-42a6-a48a-664e43a38d4e', macro: '28f7ec90-e08c-4c0b-b7a5-726f6fb3aebc', bitmap: 'fef8c725-faa7-42ba-aadb-0d96371b464a',
    en: ['Sanderling', 'Start navigation in the default mode (Fly unless changed in settings)'],
    zh: ['Sanderling', '开始导航（使用设置中的默认模式，默认是飞行）'],
  },
  {
    icon: 'walk', command: 'SanderlingWalk',
    item: '6f72cfcd-ed9b-494d-8df3-5a85ee81ac11', macro: 'eeed5b4d-70e6-46ed-8f76-05253e1427cf', bitmap: '96c055b4-81b7-4898-bac0-77979eb7e316',
    en: ['Sanderling Walk', 'Start navigation in Walk mode: eye height, floors, stairs and collision'],
    zh: ['Sanderling 漫游', '以漫游模式开始导航：人眼高度、贴地行走、自动上楼梯、碰撞'],
  },
  {
    icon: 'fly', command: 'SanderlingFly',
    item: '1c163291-1289-4cb3-a038-e41ddff7e54f', macro: '422010c1-f875-4be8-baf2-589b32a686c0', bitmap: 'ed4b89fa-c286-41cb-a922-275249632257',
    en: ['Sanderling Fly', 'Start navigation in Fly mode: free flight anywhere in the model'],
    zh: ['Sanderling 飞行', '以飞行模式开始导航：在模型中自由飞行'],
  },
  {
    icon: 'settings', command: 'SanderlingSettings',
    item: '10dc7649-7199-4c6c-92af-60898e69b1a9', macro: '34c42708-6d99-4a6b-958f-83f7a0fa1e38', bitmap: '3fcbaf7d-7ff4-46eb-9a03-f8990d4ce886',
    en: ['Sanderling Settings', 'Navigation, Walk mode and collision settings'],
    zh: ['Sanderling 设置', '导航、漫游模式和碰撞的参数设置'],
  },
  {
    icon: 'layers', command: 'SanderlingLayers',
    item: 'c2ad3a4f-0996-4839-800f-ec92a11cddf1', macro: 'a529879d-e6f4-48a4-9aa9-806502d37ec5', bitmap: '3d07b543-734e-4f3c-8f4b-b5febe13c4cb',
    en: ['Sanderling Layers', 'Choose which layers take part in Walk mode ground and collision'],
    zh: ['Sanderling 图层', '选择哪些图层参与漫游模式的地面检测和碰撞'],
  },
  {
    icon: 'refresh', command: 'SanderlingRefresh',
    item: '2244b190-476c-40cc-b74b-8d17aeff945e', macro: '21a89a49-aa9b-4aa3-9ef7-1230fe14e887', bitmap: '65d83314-1b30-416c-9e74-7a4a200f54a7',
    en: ['Sanderling Refresh', 'Rebuild the cached Walk mode geometry of the active document'],
    zh: ['Sanderling 刷新', '重建当前文档的漫游几何缓存'],
  },
];

const SIZES = [['small_bitmap', 16], ['normal_bitmap', 24], ['large_bitmap', 32]];

function esc(s) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function text(tag, en, zh, indent) {
  return `${indent}<${tag}>\n${indent}  <locale_1033>${esc(en)}</locale_1033>\n${indent}  <locale_2052>${esc(zh)}</locale_2052>\n${indent}</${tag}>`;
}

// Renders all icons side by side at the given size on a transparent background
async function renderStrip(page, size) {
  const cells = buttons.map(b => {
    const svg = fs.readFileSync(path.join(iconDir, b.icon + '.svg'), 'utf8')
      .replace(/<metadata>[\s\S]*?<\/metadata>/, '')
      .replace('width="24" height="24"', `width="${size}" height="${size}"`);
    return `<div style="width:${size}px;height:${size}px">${svg}</div>`;
  }).join('');
  await page.setViewportSize({ width: size * buttons.length, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent"><div style="display:flex">${cells}</div></body></html>`);
  const png = await page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size * buttons.length, height: size } });
  return png.toString('base64');
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ deviceScaleFactor: 1 });
  const strips = {};
  for (const [tag, size] of SIZES) strips[tag] = await renderStrip(page, size);
  await browser.close();

  const toolbarItems = buttons.map(b => `      <tool_bar_item guid="${b.item}" button_display_mode="control_only" button_style="normal">
        <left_macro_id>${b.macro}</left_macro_id>
      </tool_bar_item>`).join('\n');

  const macros = buttons.map(b => `    <macro_item guid="${b.macro}" bitmap_id="${b.bitmap}">
${text('text', b.en[0], b.zh[0], '      ')}
${text('tooltip', b.en[1], b.zh[1], '      ')}
${text('help_text', b.en[1], b.zh[1], '      ')}
${text('button_text', b.en[0], b.zh[0], '      ')}
${text('menu_text', b.en[0], b.zh[0], '      ')}
      <script>! _${b.command}</script>
    </macro_item>`).join('\n');

  const bitmaps = SIZES.map(([tag, size]) => `    <${tag} item_width="${size}" item_height="${size}">
      <bitmap>${strips[tag]}</bitmap>
    </${tag}>`).join('\n');
  const bitmapItems = buttons.map((b, i) => `    <bitmap_item guid="${b.bitmap}" index="${i}" />`).join('\n');

  const rui = `<?xml version="1.0" encoding="utf-8"?>
<RhinoUI major_ver="3" minor_ver="0" guid="${RUI_GUID}" localize="False" default_language_id="1033" dpi_scale="100">
  <extend_rhino_menus />
  <menus />
  <tool_bar_groups>
    <tool_bar_group guid="${GROUP_GUID}" dock_bar_guid32="00000000-0000-0000-0000-000000000000" dock_bar_guid64="00000000-0000-0000-0000-000000000000" active_tool_bar_group="${GROUP_ITEM_GUID}" single_file="False" hide_single_tab="False" point_floating="200,200">
${text('text', 'Sanderling', 'Sanderling', '      ')}
      <tool_bar_group_item guid="${GROUP_ITEM_GUID}" major_version="1" minor_version="1">
${text('text', 'Sanderling', 'Sanderling', '        ')}
        <tool_bar_id>${TOOLBAR_GUID}</tool_bar_id>
      </tool_bar_group_item>
    </tool_bar_group>
  </tool_bar_groups>
  <tool_bars>
    <tool_bar guid="${TOOLBAR_GUID}" bitmap_id="00000000-0000-0000-0000-000000000000" item_display_style="control_only">
${text('text', 'Sanderling', 'Sanderling', '      ')}
${toolbarItems}
    </tool_bar>
  </tool_bars>
  <macros>
${macros}
  </macros>
  <bitmaps>
${bitmaps}
${bitmapItems}
  </bitmaps>
  <scripts />
</RhinoUI>
`;
  fs.writeFileSync(output, rui);
  console.log('Wrote ' + path.relative(root, output));
})();
