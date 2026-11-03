# Madi AI — Native Windows assistant (development build)

Madi AI is a native .NET 8 WPF Windows desktop application, **not HTML, Electron or a web-based UI**. The original Madi AI futuristic image is the design reference. The dashboard includes a holographic assistant illustration, animated rings, dark-blue system panels, quick actions, app dock, and top-right START / minimize / maximize / close controls.

## Setup
The GitHub Actions workflow builds a **self-contained, dark-themed WPF installer and uninstaller**. Download the latest successful **Madi-AI-Native-Dark-Installer** artifact, extract the ZIP, then run `Madi-AI-Setup.exe`. The custom setup UI installs and creates desktop/Start Menu shortcuts without CMD.

On **first installation**, the installer requires you to enter and confirm a new secret code of 6+ characters **before installation can begin**. When Madi AI opens, enter that same code to unlock the dashboard. Existing users retain their previously saved code during updates. The code is **never embedded in source code**: only a PBKDF2 salted hash is stored in `%LOCALAPPDATA%\MadiAI\unlock.json`. Later launches verify the same code. Updates and uninstall do not delete that profile. No code or credential should be committed to this public repo.

## Voice and languages
After unlocking, Madi AI attempts to start listening automatically. The START/STOP button controls the microphone. Silence detection ends each utterance after a short pause instead of a fixed seven-second delay. The cached Whisper engine transcribes **locally** using the multilingual tiny model bundled in the installer; the fallback first-use download requires internet if the model is missing. English, Urdu, Hindi and automatic language selection are available in a dark dropdown. You can choose the microphone in Settings. Voice response time depends on microphone quality and CPU performance; truly instantaneous recognition cannot be guaranteed. Speech transcription quality varies by accent, language and noise; Windows speech replies depend on locally installed voices, so Urdu/Hindi spoken replies are not guaranteed on every machine.

## Supported actions and limitations
- Launch Windows applications through Start Menu shortcuts or Open App picker; WhatsApp desktop is tried before the web fallback. MetaTrader 5 can be launched if installed, but **trading is not connected**.
- Google Chrome profile discovery from the local Chrome profile configuration. Say "open Google", then "profile 1" / "profile 2" or the profile name.
- Supported websites, web searches, calculator, screenshots, files, Windows settings, time/date, volume, mute, media playback and simple conversational greetings.
- Optional natural-language chat through a local Ollama installation with an installed model. Ollama and its model are **not** bundled; without them, general AI chat is unavailable.
- Live CPU/RAM/disk metrics, animated dashboard, and native window controls.
- Safe confirmation before shutdown/restart.
- First-install private secret-code entry and confirmation, with local salted PBKDF2 hash verification and persistence across updates.

## Not yet implemented
- Built-in general-purpose LLM without a separate local model, arbitrary desktop automation, fully automated eBay/AliExpress/Canva, WhatsApp personal voice call handling, unattended MT5 trading and broker integration.
- This alpha is **not** a fully autonomous assistant and must not be used to execute real financial transactions.
- The original image is a visual specification, but the dashboard is an original native recreation, not a pixel-identical screenshot.
- The installer is unsigned and may trigger SmartScreen warnings. Windows testing is required.

## Developer build
On Windows with .NET 8 SDK, generate assets with `python tools/generate_assets.py` (Pillow required), then build the WPF app. The CI workflow builds both application and installer automatically.
