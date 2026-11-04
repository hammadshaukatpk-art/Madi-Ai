# Madi AI — JARVIS-style Windows assistant roadmap

## User requirements

Madi AI should accept both typed and spoken requests, keep a consistent dark navy/cyan/purple brand, converse naturally in English, Urdu, Hindi, and Roman Urdu, remember approved context locally, perform Windows actions, handle multiple Chrome profiles, and use free local models without a paid API or subscription.

## Implemented in this branch (first repair pass)

- Detect installed Ollama models through `http://127.0.0.1:11434/api/tags`; select an installed model in Settings without automatically downloading it.
- Prefer the selected model (or `llama3.2:latest`) and display the actual connected model in the status panel.
- Stream Ollama responses into a scrollable conversation panel rather than waiting to display the entire answer.
- Save the last 100 local AI conversation messages under `%LOCALAPPDATA%\MadiAI\conversation.json` and restore them on restart. Only recent messages are sent as AI context.
- Smooth CPU measurements to avoid transient spikes in the dashboard. Compare on the same sampling interval with Task Manager.
- Keep the existing Windows application launcher, Chrome profile selection, and explicit confirmation for shutdown/restart and app close.
- Add pull-request Windows build checks.

## Not implemented yet — do not advertise these as working

1. **Natural, low-latency multilingual speech.** Current Whisper Tiny transcribes completed utterances, and Windows SpeechSynthesizer voices may not support Urdu or Hindi. Replace with a tested local streaming STT + language-aware offline TTS pipeline, voice activity detection, barge-in, and audio cancellation. Offline models will require one-time downloads and use CPU/GPU.
2. **Full JARVIS-style planning and computer use.** The current dispatcher supports specific commands. Add explicit, permission-scoped Windows automation tools, safe action feedback, and confirmations for destructive/financial actions.
3. **MT5 trading automation.** The existing MT5 button only launches MT5. Any trading integration must identify the correct account and symbol, use read-only account/position inspection first, and require user confirmation before placing, modifying, or closing orders. Never infer order parameters from vague speech or promise execution without terminal acknowledgment.
4. **Semantic memory.** This branch adds persistent chat history, not long-term semantic retrieval or autonomous memory. Add memory controls, encryption, retention policy, user inspection and deletion.
5. **Responsive UI redesign.** The existing 1648x928 fixed Canvas inside a Viewbox still scales poorly on smaller screens. Replace with responsive WPF Grid layouts, proper minimum sizes, keyboard navigation, accessible controls, and a redesigned but recognizable Madi visual identity.
6. **System-wide natural interaction.** Always-on wake word, foreground app context, screenshots, clipboard, browser automation, and interruption handling require separate user permissions and testing.

## Testing checklist

- `ollama list` shows existing models; Settings should list them without downloading.
- With Ollama running, select `llama3.2:latest`; chat output should appear progressively.
- Close/reopen Madi AI; the local conversation should be restored.
- Compare CPU readings over at least 30 seconds against Windows Task Manager.
- Test microphone in Settings and English/Urdu/Hindi/Auto recognition; record misrecognitions and audio latency.
- Verify Chrome profile selection, file dialogs, app launch, and safety confirmation dialogs.
- Build and smoke-test the Windows installer through GitHub Actions before distributing it.

## Privacy and security

Conversation history is stored in plain text on the user's own PC in this first pass. Do not put passwords, broker credentials, API tokens, or personal secrets in AI chat. Before broad automation and financial integration, add encrypted local storage and an explicit permission/audit system.

This project uses Ollama locally. No paid cloud AI API is required for the features listed as implemented, but the computer must have enough resources and the selected models must already be installed.
