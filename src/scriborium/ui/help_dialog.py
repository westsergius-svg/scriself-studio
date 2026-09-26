from __future__ import annotations

from PySide6.QtWidgets import QDialog, QLabel, QTabWidget, QTextBrowser, QVBoxLayout, QWidget

from scriborium.core.i18n import I18nService


HELP_TEXTS: dict[str, dict[str, object]] = {
    "ru": {
        "title": "Справка Scriborium",
        "intro": "Краткое руководство по работе с проектом, сценами, таймлайном и экспортом.",
        "tabs": [
            (
                "Начало работы",
                """
                <h2>Быстрый старт</h2>
                <p><b>1.</b> В лаунчере создайте новый проект или откройте существующий <code>.scri</code>.</p>
                <p><b>2.</b> Заполните карточку книги: название, жанр, предисловие, аннотацию.</p>
                <p><b>3.</b> Добавьте главы и сцены, затем заполните цель, конфликт, результат и основной текст сцены.</p>
                <p><b>4.</b> Свяжите сцену с персонажами, локациями, объектами и сюжетными линиями.</p>
                <p><b>5.</b> Когда структура готова, сформируйте документ через кнопку <b>Сформировать документ</b>.</p>
                """,
            ),
            (
                "Структура проекта",
                """
                <h2>Как устроен проект</h2>
                <p>Слева находится дерево структуры: книга, главы, сцены, библиотека и сюжетные линии.</p>
                <p>Справа открывается редактор выбранного элемента.</p>
                <p>Сцены могут содержать дату/время, цель, конфликт, результат, основной текст и связи с сущностями.</p>
                <p>Библиотека хранит карточки персонажей, локаций и объектов, чтобы проект не превращался в хаос текста.</p>
                """,
            ),
            (
                "Таймлайн",
                """
                <h2>Работа с таймлайном</h2>
                <p>Таймлайн строится по полю <b>Дата/время</b> сцены.</p>
                <p>Сюжетные линии отображаются цветными дорожками, а сцены — метками на этих дорожках.</p>
                <p>Колесо мыши прокручивает шкалу влево-вправо, <b>Ctrl + колесо</b> меняет масштаб.</p>
                <p>Если сцена — флешбек, укажите раннюю дату, и она автоматически встанет раньше на временной шкале.</p>
                """,
            ),
            (
                "Экспорт",
                """
                <h2>Формирование документа</h2>
                <p>Кнопка <b>Сформировать документ</b> открывает экспортный центр.</p>
                <p>Там можно выбрать формат, состав документа, шрифт, интервалы и набор сцен.</p>
                <p>Если у сцены указано время, оно автоматически попадёт в заголовок сцены при экспорте.</p>
                <p>Для финальной рукописи обычно используют <b>DOCX</b>.</p>
                """,
            ),
            (
                "Подсказки",
                """
                <h2>Полезные советы</h2>
                <p>Сохраняйте проект часто: автосохранение помогает, но ручное сохранение всё ещё важно.</p>
                <p>Если нужно быстро открыть другой проект, используйте пункт <b>Вернуться в лаунчер</b>.</p>
                <p>Если какой-то блок кажется перегруженным, начните с книги, затем заполните главы, и только потом углубляйтесь в сцены и библиотеку.</p>
                """,
            ),
        ],
    },
    "en": {
        "title": "Scriborium Help",
        "intro": "A short guide to projects, scenes, timeline and export.",
        "tabs": [
            (
                "Getting Started",
                """
                <h2>Quick Start</h2>
                <p><b>1.</b> Create a new project in the launcher or open an existing <code>.scri</code> file.</p>
                <p><b>2.</b> Fill in the book card: title, genre, preface and synopsis.</p>
                <p><b>3.</b> Add chapters and scenes, then fill in the goal, conflict, outcome and scene text.</p>
                <p><b>4.</b> Link scenes to characters, locations, objects and plotlines.</p>
                <p><b>5.</b> When the structure is ready, use <b>Build Document</b> to export the manuscript.</p>
                """,
            ),
            (
                "Project Structure",
                """
                <h2>Project Layout</h2>
                <p>The left side contains the project tree: book, chapters, scenes, library and plotlines.</p>
                <p>The right side shows the editor for the selected item.</p>
                <p>Scenes may contain date/time, goal, conflict, outcome, body text and links to entities.</p>
                <p>The library stores characters, locations and objects so the project stays structured.</p>
                """,
            ),
            (
                "Timeline",
                """
                <h2>Using the Timeline</h2>
                <p>The timeline is based on the scene <b>Date/Time</b> field.</p>
                <p>Plotlines are shown as colored tracks, and scenes appear as markers on those tracks.</p>
                <p>Use the mouse wheel to scroll horizontally and <b>Ctrl + wheel</b> to zoom.</p>
                <p>If a scene is a flashback, enter an earlier date and it will move earlier on the timeline automatically.</p>
                """,
            ),
            (
                "Export",
                """
                <h2>Building the Document</h2>
                <p>The <b>Build Document</b> button opens the export center.</p>
                <p>There you can choose the format, document contents, fonts, spacing and selected scenes.</p>
                <p>If a scene has a time value, it is automatically added to the scene heading during export.</p>
                <p><b>DOCX</b> is usually the best choice for a final manuscript draft.</p>
                """,
            ),
            (
                "Tips",
                """
                <h2>Useful Tips</h2>
                <p>Save often: autosave helps, but manual saves are still important.</p>
                <p>To switch projects quickly, use <b>Return to Launcher</b>.</p>
                <p>If the project feels overwhelming, start with the book card, then chapters, and only then deepen the scenes and library.</p>
                """,
            ),
        ],
    },
    "fr": {
        "title": "Aide Scriborium",
        "intro": "Guide rapide pour les projets, les scènes, la chronologie et l’export.",
        "tabs": [
            (
                "Démarrage",
                """
                <h2>Démarrage rapide</h2>
                <p><b>1.</b> Créez un nouveau projet dans le lanceur ou ouvrez un fichier <code>.scri</code> existant.</p>
                <p><b>2.</b> Renseignez la fiche du livre : titre, genre, préface et synopsis.</p>
                <p><b>3.</b> Ajoutez des chapitres et des scènes, puis remplissez l’objectif, le conflit, le résultat et le texte.</p>
                <p><b>4.</b> Reliez les scènes aux personnages, lieux, objets et lignes narratives.</p>
                <p><b>5.</b> Quand la structure est prête, utilisez <b>Générer le document</b>.</p>
                """,
            ),
            (
                "Structure",
                """
                <h2>Structure du projet</h2>
                <p>À gauche se trouve l’arborescence : livre, chapitres, scènes, bibliothèque et lignes narratives.</p>
                <p>À droite s’ouvre l’éditeur de l’élément sélectionné.</p>
                <p>Les scènes peuvent contenir une date/heure, un objectif, un conflit, un résultat, un texte principal et des liens.</p>
                """,
            ),
            (
                "Chronologie",
                """
                <h2>Utiliser la chronologie</h2>
                <p>La chronologie est basée sur le champ <b>Date/Heure</b> de la scène.</p>
                <p>Les lignes narratives apparaissent comme des pistes colorées, et les scènes comme des marqueurs.</p>
                <p>La molette fait défiler horizontalement, <b>Ctrl + molette</b> change le zoom.</p>
                """,
            ),
            (
                "Export",
                """
                <h2>Générer le document</h2>
                <p>Le bouton <b>Générer le document</b> ouvre le centre d’export.</p>
                <p>Vous pouvez y choisir le format, le contenu, la police, l’interligne et les scènes incluses.</p>
                <p>Si une scène possède une date/heure, elle sera ajoutée à son titre lors de l’export.</p>
                """,
            ),
            (
                "Conseils",
                """
                <h2>Conseils utiles</h2>
                <p>Enregistrez souvent : l’enregistrement automatique aide, mais l’enregistrement manuel reste important.</p>
                <p>Pour changer rapidement de projet, utilisez <b>Retour au lanceur</b>.</p>
                """,
            ),
        ],
    },
    "zh": {
        "title": "Scriborium 帮助",
        "intro": "关于项目、场景、时间轴和导出的快速指南。",
        "tabs": [
            (
                "开始使用",
                """
                <h2>快速开始</h2>
                <p><b>1.</b> 在启动器中创建新项目，或打开已有的 <code>.scri</code> 文件。</p>
                <p><b>2.</b> 填写书籍信息：标题、体裁、前言和简介。</p>
                <p><b>3.</b> 添加章节和场景，并填写目标、冲突、结果和正文。</p>
                <p><b>4.</b> 将场景与角色、地点、物件和剧情线关联。</p>
                <p><b>5.</b> 结构完成后，使用 <b>生成文档</b> 导出稿件。</p>
                """,
            ),
            (
                "项目结构",
                """
                <h2>项目结构</h2>
                <p>左侧是项目树：书籍、章节、场景、资料库和剧情线。</p>
                <p>右侧显示当前所选内容的编辑器。</p>
                <p>场景可以包含日期/时间、目标、冲突、结果、正文和实体关联。</p>
                """,
            ),
            (
                "时间轴",
                """
                <h2>时间轴使用方式</h2>
                <p>时间轴基于场景中的 <b>日期/时间</b> 字段。</p>
                <p>剧情线显示为彩色轨道，场景显示为轨道上的标记。</p>
                <p>鼠标滚轮可左右滚动，<b>Ctrl + 滚轮</b> 可缩放。</p>
                """,
            ),
            (
                "导出",
                """
                <h2>生成文档</h2>
                <p><b>生成文档</b> 按钮会打开导出中心。</p>
                <p>你可以选择格式、文档内容、字体、行距和要包含的场景。</p>
                <p>如果场景设置了时间，导出时会自动加入场景标题。</p>
                """,
            ),
            (
                "提示",
                """
                <h2>实用提示</h2>
                <p>请经常保存：自动保存很有帮助，但手动保存仍然重要。</p>
                <p>如果需要快速切换项目，请使用 <b>返回启动器</b>。</p>
                """,
            ),
        ],
    },
    "ja": {
        "title": "Scriborium ヘルプ",
        "intro": "プロジェクト、シーン、タイムライン、書き出しに関するクイックガイドです。",
        "tabs": [
            (
                "はじめに",
                """
                <h2>クイックスタート</h2>
                <p><b>1.</b> ランチャーで新しいプロジェクトを作成するか、既存の <code>.scri</code> を開きます。</p>
                <p><b>2.</b> 書籍カードにタイトル、ジャンル、前書き、概要を入力します。</p>
                <p><b>3.</b> 章とシーンを追加し、目的、対立、結果、本文を入力します。</p>
                <p><b>4.</b> シーンを登場人物、場所、オブジェクト、プロットラインに関連付けます。</p>
                <p><b>5.</b> 構成が整ったら <b>ドキュメントを生成</b> を使って書き出します。</p>
                """,
            ),
            (
                "構成",
                """
                <h2>プロジェクト構造</h2>
                <p>左側には本、章、シーン、ライブラリ、プロットラインのツリーがあります。</p>
                <p>右側には選択した項目のエディタが表示されます。</p>
                <p>シーンには日時、目的、対立、結果、本文、関連情報を持たせることができます。</p>
                """,
            ),
            (
                "タイムライン",
                """
                <h2>タイムラインの使い方</h2>
                <p>タイムラインはシーンの <b>日時</b> フィールドをもとに構築されます。</p>
                <p>プロットラインは色付きのレーン、シーンはその上のマーカーとして表示されます。</p>
                <p>マウスホイールで左右スクロール、<b>Ctrl + ホイール</b> でズームします。</p>
                """,
            ),
            (
                "書き出し",
                """
                <h2>ドキュメント生成</h2>
                <p><b>ドキュメントを生成</b> ボタンで書き出しセンターを開きます。</p>
                <p>形式、内容、フォント、行間、含めるシーンを選択できます。</p>
                <p>シーンに日時がある場合、書き出し時に見出しへ自動追加されます。</p>
                """,
            ),
            (
                "ヒント",
                """
                <h2>便利なヒント</h2>
                <p>こまめに保存してください。自動保存は便利ですが、手動保存も重要です。</p>
                <p>別のプロジェクトへ素早く戻るには <b>ランチャーに戻る</b> を使います。</p>
                """,
            ),
        ],
    },
}


class HelpDialog(QDialog):
    def __init__(self, parent: QWidget, language: str) -> None:
        super().__init__(parent)
        code = I18nService().resolve_code(language)
        payload = HELP_TEXTS.get(code, HELP_TEXTS["en"])

        self.setWindowTitle(str(payload["title"]))
        self.resize(920, 680)

        layout = QVBoxLayout(self)
        intro = QLabel(str(payload["intro"]))
        intro.setWordWrap(True)
        layout.addWidget(intro)

        tabs = QTabWidget()
        for title, html in payload["tabs"]:  # type: ignore[index]
            browser = QTextBrowser()
            browser.setOpenExternalLinks(False)
            browser.setHtml(str(html))
            tabs.addTab(browser, str(title))
        layout.addWidget(tabs, 1)
