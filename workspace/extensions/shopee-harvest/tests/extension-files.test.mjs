// Lưới an toàn cho cả thư mục extension (workspace/tests không có bản ExtensionJsCuPhapTests như suite):
// - mọi file .js parse được (module: `node --input-type=module --check`, script thường: vm.Script) — một lỗi cú pháp là service worker chết ngay lúc nạp;
// - import tương đối trỏ tới file có thật và tên import có export tương ứng;
// - manifest trỏ tới file có thật, hook khai đúng world/run_at; không còn dấu vết BigSeller / tên extension cũ.
// Chạy: node --test extensions/shopee-harvest/tests/*.test.mjs (từ workspace/).
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import vm from "node:vm";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

// File thuộc extension (bỏ tests/ — manifest không nạp thư mục này).
const extFiles = (exts) => {
  const out = [];
  const walk = (dir) => {
    for (const ent of fs.readdirSync(dir, { withFileTypes: true })) {
      const full = path.join(dir, ent.name);
      if (ent.isDirectory()) { if (ent.name !== "tests") walk(full); }
      else if (exts.includes(path.extname(ent.name))) out.push(full);
    }
  };
  walk(ROOT);
  return out;
};

const JS_FILES = extFiles([".js"]);

test("có đủ file JS (chống xanh giả khi đường dẫn hỏng)", () => {
  assert.ok(JS_FILES.length >= 8, `chỉ thấy ${JS_FILES.length} file`);
});

// Script THƯỜNG (content script / executeScript files) — còn lại đều nạp dạng ES module (SW type=module, popup).
const CLASSIC_SCRIPTS = new Set(["pdp-hook.js", "pdp-store.js", "overlay.js"]);

test("mọi file .js parse được đúng loại (module qua node --check stdin, script thường qua vm.Script)", () => {
  // KHÔNG dùng `node --check file.js` cho module: Node 24 tự dò loại file và BỎ QUA lỗi cú pháp ở file .js có
  // `import` (đã thử: file có `import` + dấu `}` thừa vẫn exit 0) → đưa qua stdin với --input-type=module.
  const broken = [];
  for (const f of JS_FILES) {
    const src = fs.readFileSync(f, "utf8");
    if (CLASSIC_SCRIPTS.has(path.basename(f))) {
      try { new vm.Script(src, { filename: f }); } catch (e) { broken.push(`${f}: ${e.message}`); }
      continue;
    }
    const r = spawnSync(process.execPath, ["--input-type=module", "--check"], { input: src, encoding: "utf8" });
    if (r.status !== 0) broken.push(`${f}: ${(r.stderr || "").trim().split("\n").slice(0, 5).join(" | ")}`);
  }
  assert.deepEqual(broken, []);
});

// import { a, b as c } from "./x.js" / import x from "./x.js" / import "./x.js" (đủ cho các dạng repo dùng).
const IMPORT_RE = /^\s*import\s+(?:(\{[^}]*\})|([A-Za-z_$][\w$]*))?\s*(?:from\s+)?["'](\.{1,2}\/[^"']+)["']/gm;
const exportedNames = (src) => {
  const names = new Set();
  for (const m of src.matchAll(/^\s*export\s+(?:async\s+)?(?:const|let|var|function\*?|class)\s+([A-Za-z_$][\w$]*)/gm)) names.add(m[1]);
  for (const m of src.matchAll(/^\s*export\s*\{([^}]*)\}/gm)) {
    for (const part of m[1].split(",")) {
      const name = part.trim().split(/\s+as\s+/).pop();
      if (name) names.add(name);
    }
  }
  if (/^\s*export\s+default\b/m.test(src)) names.add("default");
  return names;
};

test("import tương đối trỏ tới file có thật và có export đúng tên", () => {
  const problems = [];
  for (const f of [...JS_FILES, ...extFiles([".mjs"])]) {
    const src = fs.readFileSync(f, "utf8");
    for (const m of src.matchAll(IMPORT_RE)) {
      const target = path.resolve(path.dirname(f), m[3]);
      if (!fs.existsSync(target)) { problems.push(`${f}: import "${m[3]}" → không có file`); continue; }
      const names = m[1]
        ? m[1].slice(1, -1).split(",").map((s) => s.trim().split(/\s+as\s+/)[0]).filter(Boolean)
        : m[2] ? ["default"] : [];
      const have = exportedNames(fs.readFileSync(target, "utf8"));
      for (const n of names) if (!have.has(n)) problems.push(`${f}: { ${n} } không được export bởi ${m[3]}`);
    }
  }
  assert.deepEqual(problems, []);
});

test("manifest: tên/phiên bản, file trỏ tới có thật, hook MAIN + kho ISOLATED, document_start trên host Shopee", () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(ROOT, "manifest.json"), "utf8"));
  assert.equal(manifest.name, "Shopee Harvest Runner");
  assert.equal(manifest.version, "1.0.0");
  assert.equal(manifest.manifest_version, 3);
  assert.equal(manifest.background.type, "module");
  assert.ok(fs.existsSync(path.join(ROOT, manifest.background.service_worker)));
  assert.equal(manifest.action.default_popup, "popup.html"); // C# nhận extension runner qua popup.html
  assert.ok(fs.existsSync(path.join(ROOT, manifest.action.default_popup)));

  const cs = manifest.content_scripts;
  assert.equal(cs.length, 2);
  const byFile = Object.fromEntries(cs.map((c) => [c.js.join(","), c]));
  assert.equal(byFile["pdp-hook.js"]?.world, "MAIN");
  assert.equal(byFile["pdp-store.js"]?.world, "ISOLATED");
  for (const c of cs) {
    assert.equal(c.run_at, "document_start");
    assert.equal(c.all_frames, false);
    assert.deepEqual(c.matches, ["*://shopee.vn/*", "*://*.shopee.vn/*"]);
    for (const f of c.js) assert.ok(fs.existsSync(path.join(ROOT, f)), f);
  }

  const html = fs.readFileSync(path.join(ROOT, "popup.html"), "utf8");
  for (const m of html.matchAll(/<script[^>]*src="([^"]+)"/g)) assert.ok(fs.existsSync(path.join(ROOT, m[1])), m[1]);
});

test("không còn dấu vết BigSeller / tên extension cũ / content.js", () => {
  assert.equal(fs.existsSync(path.join(ROOT, "content.js")), false);
  const hits = [];
  for (const f of extFiles([".js", ".json", ".html"])) {
    const src = fs.readFileSync(f, "utf8");
    if (/Shopee Data Runner/.test(src)) hits.push(`${f}: "Shopee Data Runner"`);
    if (/Bigseller Account|log in BigSeller|crawl_btn|big_crawl|needLogin|alreadyScraped|__launcherExecuteScrapeStep|hasScrapeStep\s*:/i.test(src)) hits.push(`${f}: dấu vết BigSeller/scrape`);
  }
  assert.deepEqual(hits, []);
});

test("background: có __launcherExecuteHarvestStep và probe trả cờ hasHarvestStep", () => {
  const src = fs.readFileSync(path.join(ROOT, "background.js"), "utf8");
  assert.match(src, /globalThis\.__launcherExecuteHarvestStep\s*=/);
  assert.match(src, /hasHarvestStep:\s*\n?\s*typeof globalThis\.__launcherExecuteHarvestStep === "function"/);
});

test("background/popup KHÔNG gọi thẳng get_pc (không fetch, không URL get_pc) — chống thêm lại đường gọi thẳng", () => {
  for (const name of ["background.js", "popup.js"]) {
    const src = fs.readFileSync(path.join(ROOT, name), "utf8");
    // Bỏ comment để không dính chữ "fetch"/"get_pc" trong giải thích.
    const code = src.replace(/\/\/[^\n]*/g, "").replace(/\/\*[\s\S]*?\*\//g, "");
    // get_pc chỉ được nhắc trong CHUỖI thông báo/log, không được dựng URL endpoint hay gọi fetch.
    assert.equal(/\bfetch\s*\(/.test(code), false, `${name}: không được gọi fetch(`);
    assert.equal(/\/api\/v4\/pdp\/get_pc/.test(code), false, `${name}: không được dựng URL endpoint get_pc`);
    assert.equal(/credentials\s*:/.test(code), false, `${name}: không được dựng request get_pc`);
  }
});

test("popup: tên dùng từ pdp-core.js nạp động có export thật", () => {
  const popup = fs.readFileSync(path.join(ROOT, "popup.js"), "utf8");
  assert.match(popup, /import\("\.\/pdp-core\.js"\)/);
  const have = exportedNames(fs.readFileSync(path.join(ROOT, "pdp-core.js"), "utf8"));
  // `core.xxx(` — biến giữ module nạp động (bỏ qua chữ "pdp-core.js" trong chuỗi/comment).
  const used = [...popup.matchAll(/(?<![\w$.-])core\.([A-Za-z_$][\w$]*)\s*\(/g)].map((m) => m[1]);
  assert.ok(used.length >= 2);
  for (const n of used) assert.ok(have.has(n), `pdp-core.js không export ${n}`);
});
