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


## Windows 10 user feedback / current status
- **v0.2.5 installer repair:** WPF PasswordBox values are read only on the UI thread and passed as plain immutable data to the installation worker. The installer no longer reads WPF controls on a worker thread. A failed installation keeps a **RETRY INSTALL** button instead of showing **CLOSE**. Automated installation tests exercise both a fresh code and an existing code replacement.
- **Private code:** the installer asks for a code on a clean first install. On a machine with an existing `%LOCALAPPDATA%\\MadiAI\\unlock.json`, the saved code is reused by default. On updates, the code fields are optional: enter and confirm a new code to replace the existing one, or leave both blank to keep it. The installer never displays a GitHub/code-detection warning. This does not delete other profile settings. No fixed secret is in source control.
- **Microphone:** Settings → **TEST MICROPHONE (3 SECONDS)** measures actual input signal on the chosen device without saving the recording. The mic selector must match the Windows recording device.
- **Web profiles:** opening WhatsApp, YouTube, Google, eBay, AliExpress or Canva asks which Chrome profile when multiple local Chrome profiles exist. Reply with a number or a profile name. "Open WhatsApp desktop" attempts the installed Windows application instead.
- **Offline conversation:** Settings → **ENABLE FREE OFFLINE AI (OLLAMA)** opens the official Ollama Windows installer if missing, or offers to download a local model (about 1.3 GB) if Ollama is installed. The initial download requires internet. Until a local model is running, ordinary conversational requests cannot be answered. This is **not** a built-in JARVIS-level reasoning agent.
- **Voice speed:** the included multilingual Whisper engine detects silence and then transcribes audio on the local CPU. It is **not** a streaming word-by-word speech engine, and a guaranteed 1–2 second reply is not supported. Installed Windows voices determine spoken Hindi/Urdu output.
- **Trading and e-commerce:** MT5 launch, eBay/AliExpress websites and WhatsApp Web are **not** autonomous trading, listing, ordering, messaging or calling integrations. They must not be presented as completed features.
- **UI:** holographic head brightness, circular rings, neon panels and dashboard proportions were updated against the supplied reference. The procedural head is **not yet a pixel-identical reproduction** of the supplied design.

## Current development branch: improved local assistant
The draft branch **fix/local-ai-detection-memory-voice-ui** replaces the fixed-size Canvas dashboard with a responsive three-column WPF layout. It keeps the repository's original Madi branding assets and dark neon visual identity, adds a scrolling conversation panel, a compact system telemetry card and an automatically scaled holographic core.

- **Conversation:** Uses an already-installed Ollama model, streaming text and beginning Windows speech output after the first complete sentence. If Ollama is installed but stopped, Madi attempts to start its local service. This is not guaranteed zero-latency speech. The installer now includes the archived MIT Piper Windows engine and a permissively licensed local English neural voice (LJ Speech, public domain). Urdu/Hindi currently use available Windows voices. An Urdu neural voice was investigated but failed compatibility testing with the bundled Piper Windows engine and is therefore not shipped. See THIRD_PARTY_VOICES.md for provenance. Neural voice speed varies by CPU.
- **Memory:** "remember that ...", "show memory", "forget ...", "clear memory" work locally. Memories are opt-in and saved to the local Windows MadiAI profile as memory.json. Common secrets are refused. This is not a semantic database or automatic memory extraction.
- **Natural actions:** A local model can classify some natural commands into a strict allowlist. Unsupported actions remain chat, not arbitrary shell execution.
- **Companion mode:** Optional 20-minute check-ins when the user is active; disable in Settings or by saying "stop check-ins". The app does not speak into its own microphone.
- **Routines:** "save routine morning: open google; open notepad", "run routine morning", "list routines", "delete routine morning". Only allowlisted local actions can be saved; no scripts or trading.
- **Privacy:** Say or type "clear chat" to delete locally saved conversation history after confirmation, without deleting separate saved memories or routines.\n- **Interrupt:** Say or type "stop" to cancel a streaming model response and stop speech; use Ctrl+L to focus the chat box, Ctrl+M to toggle listening, or Ctrl+Space to interrupt speech and resume listening. Pressing the microphone button during speech interrupts it instead of turning off listening.
- **Window management:** "close notepad" asks for confirmation and requests a normal close (never force-quits or bypasses unsaved-work prompts). "minimize notepad" and "maximize notepad" change the visible window. System, Madi and MetaTrader windows are excluded.
- **Additional controls:** Focus visible apps, list visible windows, clipboard text, find files in common folders, system status and confirmed Unicode text typing into a chosen open app.
- **MT5 bridge:** An optional MQL5 Expert Advisor in `MT5/MadiAI_Bridge.mq5` publishes local live account balance, equity, positions, active chart and quotes. Say "connect MT5" to install source into detected terminal data folders (with confirmation); MetaEditor compilation and chart attachment are required. Say "MT5 balance", "MT5 positions", "MT5 price", "MT5 chart EURUSD H1", or "analyze MT5 chart". The optional bridge exports up to 80 completed chart candles for local SMA20, SMA50, RSI14 and ATR14 statistics; no prediction, advice or trading is performed. No trade placement, modification or closing is enabled. Broker-connected integration has not been end-to-end tested.
- **Validation:** Windows CI tests local memory persistence, voice engine initialization, the actual WPF dashboard opening, installer opening and install/uninstall.

**This branch is not the final all-features JARVIS installer.** Exact reference-image fidelity, high-quality multilingual offline voice output, full arbitrary Windows app automation and verified broker-connected MT5 controls remain outstanding.

## Developer build
On Windows with .NET 8 SDK, generate assets with `python tools/generate_assets.py` (Pillow required), then build the WPF app. The CI workflow builds both application and installer automatically.
