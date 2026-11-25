// 把 docs 下的 Markdown 合并为单文件（带目录与锚点），便于通读与检索。
import fs from "node:fs";
import path from "node:path";

const docsRoot = path.resolve(process.argv[2]);
const output = path.resolve(process.argv[3]);

const ORDER = [
  "README.md",
  "05-进度与评审/27-完成与未完成清单.md",
  "05-进度与评审/26-阶段总结与下一步.md",
  "04-测试与接入/32-NowFrameWork框架使用手册.md",
  "06-模块API",
  "03-模块设计",
  "04-测试与接入",
  "02-开发规范",
  "01-架构与规划",
  "05-进度与评审",
];

function walk(rel, out) {
  const full = path.join(docsRoot, rel);
  const stat = fs.statSync(full);
  if (stat.isDirectory()) {
    for (const name of fs.readdirSync(full).sort()) {
      if (!name.endsWith(".md")) continue;
      if (name === "README.md") continue; // 目录页单独处理
      walk(path.join(rel, name), out);
    }
    return;
  }
  if (rel.endsWith(".md") && !out.includes(rel)) out.push(rel);
}

const files = [];
for (const item of ORDER) {
  const full = path.join(docsRoot, item);
  if (!fs.existsSync(full)) continue;
  if (fs.statSync(full).isDirectory()) walk(item, files);
  else if (!files.includes(item)) files.push(item);
}

const slug = (rel) => "doc-" + rel.replace(/[\\/]/g, "-").replace(/\.md$/, "").replace(/[^A-Za-z0-9\u4e00-\u9fff-]/g, "-");
const anchorOf = new Map(files.map((rel) => [rel, slug(rel)]));

function titleOf(text, rel) {
  const m = text.match(/^#\s+(.+)$/m);
  if (m) return m[1].trim();
  return path.basename(rel, ".md");
}

// 把 *.md 相对链接改写成单文件锚点；目标不在合集中的保持原样。
function rewriteLinks(text, fromRel) {
  return text.replace(/\]\(([^)\s]+\.md)(#[^)]*)?\)/g, (all, target, frag) => {
    if (/^[a-z]+:/i.test(target)) return all;
    const resolved = path.normalize(path.join(path.dirname(fromRel), target)).replace(/\\/g, "/");
    const anchor = anchorOf.get(resolved);
    return anchor ? `](#${anchor})` : all;
  });
}

function demote(text) {
  return text.replace(/^(#{1,5}) /gm, (m, hashes) => "#".repeat(Math.min(hashes.length + 1, 6)) + " ");
}

const sections = [];
const toc = [];
let index = 0;

for (const rel of files) {
  const raw = fs.readFileSync(path.join(docsRoot, rel), "utf8").replace(/\r\n/g, "\n");
  const title = titleOf(raw, rel);
  const anchor = anchorOf.get(rel);
  index += 1;

  let body = raw.replace(/^#[^\n]*\n+/, ""); // 去掉原标题
  body = demote(rewriteLinks(body, rel)).trim();

  // 二级标题加章节锚点，便于目录跳转
  const subs = [];
  let subIndex = 0;
  body = body.replace(/^### (.+)$/gm, (m, text) => {
    subIndex += 1;
    const id = `${anchor}-h${subIndex}`;
    subs.push({ id, text });
    return `<a id="${id}"></a>\n### ${text}`;
  });

  sections.push(`<a id="${anchor}"></a>\n\n## ${index}. ${title}\n\n> 源文件：\`docs/${rel}\`\n\n${body}`);

  toc.push({ index, title, anchor, subs, rel });
}

const lines = [];
lines.push("# NowFrameWork 文档总览（单文件版）", "");
lines.push("> 由 `Tools/build-docs-bundle.mjs` 自动生成，请勿手改。");
lines.push("> 源文档仍在 `docs/` 各自目录；本文用于通读、搜索与整体导航。", "");
lines.push("## 目录", "");

for (const item of toc) {
  lines.push(`- [${item.index}. ${item.title}](#${item.anchor})`);
  for (const sub of item.subs) lines.push(`  - [${sub.text}](#${sub.id})`);
}

lines.push("", "---", "");
lines.push(sections.join("\n\n---\n\n"));

fs.writeFileSync(output, lines.join("\n") + "\n", "utf8");
console.log(`合并 ${toc.length} 篇 -> ${output}`);
console.log(`目录条目 ${toc.length}，章节条目 ${toc.reduce((n, t) => n + t.subs.length, 0)}，行数 ${lines.length}`);
