# First users: local UI driver

This is an Avalonia headless test process with Skia rendering. It loads the production app, views, commands and SQLite storage. It does not control a live Windows window. The native file picker returns an isolated test path; application prompt/confirmation dialogs use their real AXAML and ViewModels. Cloud services and updates are replaced by explicit offline test services. External browser and Explorer actions are excluded.

Start from the repository root:

```powershell
dotnet run --project tools/FirstUsersQa/FirstUsersQa.csproj --configuration Release -- qa/first-users 38461
```

Interact from a separate shell (use the absolute repository as `workdir`):

```powershell
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op reset -Session before-impatient -Width 1440 -Height 768
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op observe
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op click -Target c11
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op click -X 400 -Y 300
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op type -Text 'Тестовый текст'
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op key -Key A -Modifiers Control
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op key -Key Enter
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op scroll -X 900 -Y 500 -Delta -4
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op doubleclick -Target c5
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op picker -Cancel
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op picker -FileName 'Тест.scri'
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op restart
& tools/FirstUsersQa/Invoke-Qa.ps1 -Op closewindow
```

Each response gives an actual PNG screenshot, visible labels and interactive control bounds from the active window, a step number and total pointer click count. View the PNG with `view_image`; a modal response also includes its owner screenshot. Target IDs refer only to the latest response, and belong to its active window. Coordinates are logical window coordinates. Use `observe -WaitMs 1500` if an asynchronous operation is still finishing. `type` enters text into the focused control; click the input or use normal Tab navigation first. `key` uses Avalonia Key/RawInputModifiers names such as `Escape`, `Back`, `S`, `Control`, `Shift`.

All images and `actions.jsonl` are saved under `qa/first-users/<session>/`. Settings and `.scri` files are isolated in that session. `restart` flushes edits through the same production Shell method used on normal app exit, then recreates the app container using the same test settings/files. Count only pointer clicks in the app; simulated native file selection, observations, typing and key presses are excluded. A double click counts as two clicks.

For small-screen testing use 1024×576 logical pixels: this approximates a 1366×768 desktop at 133% scaling. This desktop build is not a 375px mobile website. Network throttling is not supported; the core local writing path works offline. Do not report simulated picker, blocked network/auth or external browser behavior as product defects. Do not open real accounts or use real payments.

Only one persona may operate the driver at a time. Start every persona with a unique fresh session. A tester must inspect only its own screenshots and visible UI, not product source, another session, or other testers' findings.

The driver tracks actual secondary windows, including Help and Training. `closewindow` simulates closing the foremost secondary window using its window frame and does not add an in-app click. To close and reopen the main window, use `restart`. Initial app telemetry-consent and native OS dialogs are outside this harness; the journey begins at the main application screen. English/Chinese testers must find the app language setting through the UI. Product source, ViewModels and persisted settings must not be edited by a persona.

The click count records delivered pointer clicks. If the first click closes its dialog, the second cannot be delivered to that disposed window: the response explicitly reports this and counts one delivered click. The driver tracks one mouse across windows and sends its departure from the previous window before entering the next. Each fresh reset begins with the default process culture; restart retains that session's selected language.
