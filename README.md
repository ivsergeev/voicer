# Voicer

Кроссплатформенное приложение для голосового ввода текста. Работает в системном трее, распознаёт русскую речь офлайн с помощью модели GigaAM v3 (e2e) и отдаёт результат через WebSocket или вставляет текст в активное окно.

## Возможности

- **Офлайн-распознавание речи** — модель GigaAM v3 e2e (CTC + пунктуация + капитализация), работает без интернета
- **Push-to-talk** — удерживайте горячую клавишу для записи, отпустите для распознавания
- **Комбинации клавиш** — хоткеи поддерживают модификаторы (Ctrl+F6, Alt+Shift+F7 и т.д.)
- **Три режима работы:**
  - **Вставка в курсор** (F6) — вставляет текст в активное приложение через Ctrl+V / Cmd+V
  - **WebSocket** (F7) — транслирует распознанный текст подключённым клиентам
  - **WS + выделенный текст** (F8) — захватывает выделенный текст (или содержимое буфера обмена), добавляет его к надиктованному и отправляет по WebSocket
- **Всплывающие уведомления** — показ распознанного текста в углу экрана (отключаемо)
- **Настройки** — выбор микрофона, горячих клавиш, порта WebSocket, количества потоков
- **Автозапуск** — опциональный запуск вместе с системой
- **Кроссплатформенность** — Windows, macOS, Linux (Avalonia UI)

## Требования

- .NET 9.0 (включён в установщик при self-contained публикации)
- **Windows**: Windows 10/11 x64
- **macOS**: macOS 12+ (x64/arm64), требуется разрешение Accessibility для хоткеев
- **Linux**: X11, PulseAudio (Wayland: хоткеи требуют XWayland — запускайте с `GDK_BACKEND=x11`)

## Быстрый старт

### 1. Скачать модель

```powershell
# Windows (~215 MB)
./scripts/download-model.ps1
```

```bash
# macOS / Linux
./scripts/download-model.sh
```

### 2. Собрать и запустить

```powershell
dotnet build src/Voicer.Desktop/Voicer.Desktop.csproj
dotnet run --project src/Voicer.Desktop/Voicer.Desktop.csproj
```

### 3. Использование

1. Приложение запускается в системном трее
2. Удерживайте **F6** для записи (результат вставляется в активное окно)
3. Удерживайте **F7** для записи (результат отправляется по WebSocket)
4. Удерживайте **F8** для записи с захватом выделенного текста (выделенный текст + надиктованное отправляется по WebSocket)
5. Правый клик по иконке в трее — настройки и выход

## WebSocket API

Сервер слушает на `ws://localhost:5050` (порт настраивается). Поддерживается claim-протокол — только один клиент одновременно получает транскрипции, остальные получают статусы.

Сообщения от сервера:

```json
{"type": "transcription", "text": "Распознанный текст."}             // только активному клиенту
// Если был выделен текст: {"type": "transcription", "text": "выделенный текст надиктованный текст."}
{"type": "status", "status": "idle|recording|processing"}   // всем клиентам
{"type": "error", "message": "Описание ошибки"}             // всем клиентам
{"type": "claimed", "active": true}                          // подтверждение claim
```

Сообщения от клиента:

```json
{"type": "claim"}    // стать активным получателем транскрипций
{"type": "release"}  // отказаться от роли
```

Подробная документация: [docs/websocket-protocol.md](docs/websocket-protocol.md)

Тестовый клиент: [client/index.html](client/index.html)

## Сборка дистрибутивов

Все скрипты сборки находятся в папке `scripts/`:

```powershell
# Windows — установщик (Inno Setup 6)
./scripts/build-windows.ps1
```

```bash
# macOS — .app + .dmg (x64 + arm64)
./scripts/build-macos.sh

# Linux — .deb + .tar.gz
./scripts/build-linux.sh [Release] [linux-x64|linux-arm64]
```

Результат сборки: папка `output/`.

## Структура проекта

```
Voicer/
├── src/
│   ├── Voicer.Core/                    # Кроссплатформенное ядро
│   │   ├── Interfaces/                 # IAudioCaptureService, IHotkeyService, ...
│   │   ├── Models/
│   │   │   └── AppSettings.cs          # Настройки (JSON)
│   │   └── Services/
│   │       ├── SpeechRecognitionService.cs  # Распознавание (sherpa-onnx)
│   │       └── VoicerWebSocketServer.cs     # WebSocket-сервер
│   │
│   ├── Voicer.Desktop/                 # Avalonia UI приложение
│   │   ├── Program.cs                  # Точка входа, DI
│   │   ├── App.axaml.cs               # TrayIcon, NativeMenu
│   │   ├── AppOrchestrator.cs          # Стейт-машина (запись → распознавание → отправка)
│   │   ├── Views/
│   │   │   ├── SettingsWindow.axaml    # Окно настроек
│   │   │   └── TranscriptionPopup.axaml # Всплывающее уведомление
│   │   └── Services/
│   │       └── SkiaTrayIconGenerator.cs # Генерация иконок трея (SkiaSharp)
│   │
│   ├── Voicer.Platform.Windows/        # Windows: NAudio, P/Invoke, Registry
│   ├── Voicer.Platform.macOS/          # macOS: CoreAudio, CGEvent, LaunchAgent
│   └── Voicer.Platform.Linux/          # Linux: PulseAudio, X11, .desktop
│
├── client/index.html                   # Тестовый WebSocket-клиент
├── docs/websocket-protocol.md          # Документация протокола
├── installer/
│   ├── icons/                         # Иконки приложения (общие)
│   ├── windows/voicer.iss             # Inno Setup скрипт
│   ├── macos/                         # Info.plist, entitlements.plist
│   └── linux/                         # .desktop, DEBIAN/*, install.sh
├── models/                             # ML-модели (не в git)
└── scripts/
    ├── download-model.ps1             # Скачивание модели (PowerShell)
    ├── download-model.sh              # Скачивание модели (bash)
    ├── build-windows.ps1              # Сборка установщика Windows
    ├── build-macos.sh                 # Сборка .app + .dmg
    ├── build-linux.sh                 # Сборка .deb + .tar.gz
    └── generate-icon.py               # Генерация иконок (Pillow)
```

## Настройки

| Параметр | По умолчанию | Описание |
|----------|-------------|----------|
| Microphone | Системный | Устройство захвата |
| Threads | 4 | Потоки для inference |
| Insert hotkey | F6 | Горячая клавиша (режим вставки) |
| WebSocket hotkey | F7 | Горячая клавиша (режим WS) |
| WS + selection hotkey | F8 | Горячая клавиша (WS + выделенный текст) |
| WS Port | 5050 | Порт WebSocket-сервера |
| Show popup | Вкл | Всплывающее уведомление |
| Autostart | Выкл | Запуск с системой |

Настройки хранятся в `settings.json` рядом с исполняемым файлом.

## Интеграция с OpenCode

Voicer можно использовать как голосовой ввод для [OpenCode](https://opencode.ai) — AI-кодинг агента. Голосовые команды отправляются как промпты в активную сессию OpenCode.

### Установка

1. Скопируйте `.opencode/plugins/voicer.ts` в `.opencode/plugins/` вашего проекта (или используйте глобально через `~/.config/opencode/plugins/`)
2. Установите переменную окружения `VOICER_ENABLED=1` (или `VOICER_URL=ws://...`)
3. Перезапустите OpenCode
4. Запустите Voicer
5. Нажмите **F7**, произнесите команду — текст появится как промпт в OpenCode

### Настройка

| Переменная | По умолчанию | Описание |
|-----------|-------------|----------|
| `VOICER_ENABLED` | — | `1` или `true` для активации плагина |
| `VOICER_URL` | `ws://localhost:5050` | Адрес WebSocket-сервера (также активирует плагин) |
| `VOICER_PORT` | `5050` | Порт (используется только при автоопределении URL) |

При запуске из WSL2 плагин автоматически определяет IP Windows-хоста из `/etc/resolv.conf`.

### Claim-протокол

При нескольких запущенных экземплярах OpenCode только один получает голосовой ввод:

- Последний подключённый экземпляр автоматически становится активным
- При взаимодействии с сессией (создание, переключение) экземпляр автоматически перехватывает claim
- LLM может вызвать инструмент `voicer_claim` для явного переключения

### Инструменты

| Инструмент | Описание |
|-----------|----------|
| `voicer_status` | Статус подключения, claim, счётчик транскрипций, состояние микрофона |
| `voicer_claim` | Сделать текущий экземпляр активным получателем голосового ввода |

## Используемые технологии

- **.NET 9.0 / Avalonia UI** — кроссплатформенный UI и системный трей
- **GigaAM v3 e2e** — офлайн-распознавание русской речи с пунктуацией (CTC, ONNX, BPE)
- **sherpa-onnx** — обёртка для запуска ONNX-моделей распознавания
- **NAudio / WASAPI** — захват аудио (Windows)
- **PulseAudio** — захват аудио (Linux)
- **CoreAudio** — захват аудио (macOS)
- **Fleck** — WebSocket-сервер
- **SkiaSharp** — генерация иконок трея
- **Inno Setup** — установщик для Windows

## Лицензии

Информация о лицензиях используемых компонентов: [THIRD_PARTY_LICENSES](THIRD_PARTY_LICENSES)
