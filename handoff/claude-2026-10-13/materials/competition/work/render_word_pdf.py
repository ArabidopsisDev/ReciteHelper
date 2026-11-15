"""Use the canonical document rasterizer with a Word-produced PDF backend.

The managed environment has no LibreOffice binary. Word COM exports the PDF
before this script runs. All temporary and output paths are on D:.
"""
import os
from pathlib import Path
BASE = Path(__file__).resolve().parents[1]
for name in ('TMP','TEMP','TMPDIR'):
    os.environ[name] = str(BASE/'work'/'tmp')
os.environ['PYTHONDONTWRITEBYTECODE'] = '1'
import tempfile, importlib.util, shutil
tempfile.tempdir = str(BASE/'work'/'tmp')
path = Path('C:/Users/Arabid/.codex/plugins/cache/openai-primary-runtime/documents/26.1009.11318/skills/documents/render_docx.py')
spec=importlib.util.spec_from_file_location('canonical_docx_renderer',path)
renderer=importlib.util.module_from_spec(spec)
spec.loader.exec_module(renderer)
def from_word(input_path, user_profile, convert_tmp_dir, stem, verbose=False):
    target=Path(convert_tmp_dir)/'word-render.pdf'
    shutil.copy2(BASE/'qa'/'word-render.pdf',target)
    return str(target),'PDF generated with Microsoft Word; no LibreOffice executable available.'
renderer.convert_to_pdf = from_word
renderer.rasterize(str(BASE/'output'/'汇智云学_鲁信杯项目计划书.docx'),str(BASE/'qa'/'render'),150,False,False)
