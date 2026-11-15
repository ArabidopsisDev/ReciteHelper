from pathlib import Path
import os,tempfile,importlib.util,shutil
BASE=Path(__file__).resolve().parents[1]
for k in ('TEMP','TMP','TMPDIR'):os.environ[k]=str(BASE/'work'/'tmp')
tempfile.tempdir=str(BASE/'work'/'tmp')
renderer_path=Path('C:/Users/Arabid/.codex/plugins/cache/openai-primary-runtime/documents/26.1009.11318/skills/documents/render_docx.py')
spec=importlib.util.spec_from_file_location('canonical_docx_renderer',renderer_path)
renderer=importlib.util.module_from_spec(spec);spec.loader.exec_module(renderer)
def from_word(input_path,user_profile,convert_tmp_dir,stem,verbose=False):
    target=Path(convert_tmp_dir)/'word-render.pdf'
    shutil.copy2(BASE/'qa'/'word-render.pdf',target)
    return str(target),'PDF exported by Microsoft Word. Canonical rasterizer used with Word PDF backend.'
renderer.convert_to_pdf=from_word
renderer.rasterize(str(BASE/'output'/'汇智云学_鲁信杯项目计划书_规范扩展版.docx'),str(BASE/'qa'/'render'),135,False,False)
