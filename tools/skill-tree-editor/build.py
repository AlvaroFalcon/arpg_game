"""Builds the skill tree editor (docs/design/skills.md -> "The skill tree editor").

    python3 tools/skill-tree-editor/build.py [docs/design/skill-trees.json]

Injects the game's skill trees (written by scenes/tools/DumpSkillTrees.tscn to
user://skill_trees.json, copied to docs/design/skill-trees.json) into template.html and
writes tools/skill-tree-editor/index.html, a single file that opens in any browser.
"""
import hashlib
import json
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
src = Path(sys.argv[1]) if len(sys.argv) > 1 else here.parents[1] / 'docs/design/skill-trees.json'
data = json.loads(src.read_text(encoding='utf-8'))
for s in data['skills']:
    s.setdefault('note', '')
    for n in s['nodes']:
        n.setdefault('note', '')
# A version of the game's data: drafts made on older data are dropped instead of reloaded.
data['version'] = hashlib.sha1(json.dumps(data['skills'], sort_keys=True).encode('utf-8')).hexdigest()[:12]
blob = json.dumps(data, ensure_ascii=False, separators=(',', ':')).replace('<', '\\u003c')
page = (here / 'template.html').read_text(encoding='utf-8').replace('/*DATA*/', blob)
(here / 'index.html').write_text(page, encoding='utf-8')
print(f"wrote {here / 'index.html'} ({len(data['skills'])} skills)")
