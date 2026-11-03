# Madi AI — Native Windows assistant (development build)

Madi AI is a native .NET 8 WPF Windows desktop application, **not HTML, Electron or a web-based UI**. The original Madi AI futuristic image is the design reference. The dashboard includes a holographic assistant illustration, animated rings, dark-blue system panels, quick actions, app dock, and top-right START / minimize / maximize / close controls.

## Setup
The GitHub Actions workflow builds a **self-contained, dark-themed WPF installer and uninstaller**. Download the latest successful **Madi-AI-Native-Dark-Installer** artifact, extract the ZIP, then run `Madi-AI-Setup.exe`. The custom setup UI installs and creates desktop/Start Menu shortcuts without CMD.

On first app launch, enter a secret code of 6+ characters. The code is **never embedded in source code**: only a PBKDF2 salted hash is stored in `%LOCALAPPDATA%\MadiAI\unlock.json`. Later launches verify the same code. Updates and uninstall do not delete that profile. No code or credential should be committed to this public repo.

## Voice and languages
The START button records audio in segments and transcribes it **locally** with the free multilingual Whisper tiny model, supporting English, Urdu and Hindi transcription. On first voice use the app automatically downloads the model (about 75 MB); internet is needed for that first download. No paid API or subscription. Speech transcription quality varies by accent, language and noise; Windows speech replies depend on locally installed voices, so Urdu/Hindi spoken replies are not guaranteed on every machine.

## Working starter capabilities
- Basic app launch, supported websites, web searches, calculator, screenshots, files, Windows settings, time/date, and simple conversational greetings.
- Live CPU/RAM/disk metrics, animated dashboard, and native window controls.
- Safe confirmation before shutdown/restart.
- Spoken startup prompt and welcome message.

## Not yet implemented
- General-purpose local LLM chat, arbitrary desktop automation, fully automated eBay/AliExpress/Canva, WhatsApp personal voice call handling, unattended MT5 trading and broker integration.
- This alpha is **not** a fully autonomous assistant and must not be used to execute real financial transactions.
- The original image is a visual specification, but the dashboard is an original native recreation, not a pixel-identical screenshot.
- The installer is unsigned and may trigger SmartScreen warnings. Windows testing is required.

## Developer build
On Windows with .NET 8 SDK, generate assets with `python tools/generate_assets.py` (Pillow required), then build the WPF app. The CI workflow builds both application and installer automatically.
