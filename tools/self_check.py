from __future__ import annotations

from pathlib import Path
from tempfile import TemporaryDirectory

from scriborium.core.import_export import (
    ExportOptions,
    ExportService,
    ExportTemplate,
    ExportTemplateService,
    ImportOptions,
    ImportService,
)
from scriborium.core.project import ProjectService
from scriborium.core.scene_fields import default_scene_dict, normalize_scene_status
from scriborium.core.scene_move import apply_scene_move
from scriborium.core.search import SearchService


def check_project_cycle() -> None:
    ps = ProjectService()
    with TemporaryDirectory() as td:
        path = Path(td) / "demo.scri"
        doc = ps.create_new(path, "Demo", "Р РѕРјР°РЅ (Р±Р°Р·РѕРІС‹Р№)")
        doc.content["chapters"][0]["scenes"][0]["text"] = "alpha beta alpha"
        ps.save_document(doc)
        reopened = ps.open_document(path)
        assert reopened.content["chapters"][0]["scenes"][0]["text"] == "alpha beta alpha"


def check_search() -> None:
    ps = ProjectService()
    ss = SearchService()
    with TemporaryDirectory() as td:
        path = Path(td) / "demo.scri"
        doc = ps.create_new(path, "Demo", "Р РѕРјР°РЅ (Р±Р°Р·РѕРІС‹Р№)")
        doc.content["chapters"][0]["scenes"][0]["text"] = "one two one three"
        ps.save_document(doc)
        opened = ps.open_document(path)
        results = ss.search(opened, "one")
        assert len(results) == 2


def check_scene_move() -> None:
    content = {
        "chapters": [
            {"title": "A", "scenes": [{"id": "s1"}, {"id": "s2"}]},
            {"title": "B", "scenes": [{"id": "s3"}]},
            {"title": "C", "scenes": [{"id": "s4"}]},
        ]
    }
    result = apply_scene_move(content, 1, 0, 2, 1)
    assert result == (1, 1)
    assert [c["title"] for c in content["chapters"]] == ["A", "C"]
    assert [s["id"] for s in content["chapters"][1]["scenes"]] == ["s4", "s3"]


def check_scene_fields() -> None:
    base = default_scene_dict()
    assert "status" in base
    assert "targetWordCount" in base
    assert normalize_scene_status("done") == "done"
    assert normalize_scene_status("unknown") == "draft"


def check_import_export() -> None:
    importer = ImportService()
    exporter = ExportService()
    fmt_state = {fmt: (enabled, hint) for _, fmt, enabled, hint in exporter.available_formats()}
    assert "txt" in fmt_state and fmt_state["txt"][0]
    assert "html" in fmt_state and fmt_state["html"][0]
    assert "docx" in fmt_state and isinstance(fmt_state["docx"][0], bool)
    assert "pdf" in fmt_state and isinstance(fmt_state["pdf"][0], bool)
    with TemporaryDirectory() as td:
        root = Path(td)
        src_md = root / "draft.md"
        src_md.write_text("# Р“Р»Р°РІР° A\n## РЎС†РµРЅР° X\nРўРµРєСЃС‚ СЃС†РµРЅС‹.\n", encoding="utf-8")
        result = importer.import_file(src_md, ImportOptions(split_mode="headings"))
        assert len(result.content["chapters"]) >= 1
        out_txt = root / "out.txt"
        out_html = root / "out.html"
        first_scene_id = result.content["chapters"][0]["scenes"][0]["id"]
        opts = ExportOptions(
            include_preface=False,
            include_synopsis=False,
            selected_scene_ids=[first_scene_id],
        )
        exporter.export_document(result.content, "txt", out_txt, opts)
        exporter.export_document(result.content, "html", out_html, opts)
        assert out_txt.exists() and out_html.exists()
        txt = out_txt.read_text(encoding="utf-8")
        assert "Сцена" in txt or "Scene" in txt or "РЎС†РµРЅР°" in txt


def check_export_templates() -> None:
    service = ExportTemplateService()
    tmp_name = "selfcheck_template"
    tpl = ExportTemplate(
        name=tmp_name,
        fmt="txt",
        options=ExportOptions(include_preface=True, include_synopsis=False, selected_scene_ids=[]),
    )
    service.save_template(tpl)
    names = [t.name for t in service.load_all()]
    assert tmp_name in names


def main() -> int:
    check_project_cycle()
    check_search()
    check_scene_move()
    check_scene_fields()
    check_import_export()
    check_export_templates()
    print("SELF_CHECK_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

