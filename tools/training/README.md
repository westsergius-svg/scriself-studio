# Обучение в справке

Текст уроков: `src/ScriSelfStudio.Presentation/Help/Training/training.json`.
`sources.json` фиксирует происхождение и кадрирование настоящих снимков программы.
`screens/` содержит их подготовленные копии для воспроизведения без QA-артефактов.

`tools/build-training-presentation.mjs` принимает путь к node_modules встроенного
Presentations, каталог навыка и новый путь итогового PPTX. Переменная
`TRAINING_PYTHON` задаёт Python для валидаторов. Нужны `@oai/artifact-tool` и `sharp`.
Это инструменты разработки, в зависимости приложения они не входят.

Генератор обновляет PNG в `Help/Training/Slides`, создаёт PPTX и проверяет его.
После визуального просмотра всех слайдов проверенный PPTX копируется в
`docs/training/ScriSelfStudio-Training.pptx`. При замене снимка нужно также обновить
соответствующий файл в `screens/`. Сам интерфейс не перерисовывается.
